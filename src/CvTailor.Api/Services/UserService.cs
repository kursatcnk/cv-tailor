using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using CvTailor.Api.Data;
using CvTailor.Api.Dtos;
using CvTailor.Api.Models;

namespace CvTailor.Api.Services
{
    // Kayıt, giriş, şifre sıfırlama ve JWT üretimi.
    public class UserService : IUserService
    {
        public const int MinPasswordLength = 8;
        // BCrypt 72 byte'tan sonrasını zaten okumuyor; çok uzun şifreyle hash'i yormanın da anlamı yok.
        public const int MaxPasswordLength = 128;

        // Kullanıcı bulunamadığında da bir BCrypt doğrulaması yapıyorum ki cevap süresinden
        // "bu mail kayıtlı mı" anlaşılmasın. Hash'in ne olduğu önemli değil, maliyeti aynı olsun yeter.
        private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("timing-equalizer");

        private readonly CvTailorDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly OneTimeCodeService _codes;
        private readonly IEmailSender _email;
        private readonly ILogger<UserService> _logger;

        public UserService(CvTailorDbContext context, IConfiguration configuration, OneTimeCodeService codes,
            IEmailSender email, ILogger<UserService> logger)
        {
            _context = context;
            _configuration = configuration;
            _codes = codes;
            _email = email;
            _logger = logger;
        }

        // Mail gitmedi diye kayıt/sıfırlama yarıda kalmasın; logla, kullanıcı "tekrar gönder" diyebilir.
        private async Task<bool> TrySendAsync(string to, EmailMessage message)
        {
            try
            {
                await _email.SendAsync(to, message);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "E-posta gönderilemedi: {Subject} → {To}", message.Subject, to);
                return false;
            }
        }

        // Şifre değiştirme ve sıfırlama da bunu kullanıyor, kural tek yerde kalsın.
        public static string? ValidatePassword(string? password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
                return $"Şifre en az {MinPasswordLength} karakter olmalıdır.";
            if (password.Length > MaxPasswordLength)
                return $"Şifre en fazla {MaxPasswordLength} karakter olabilir.";
            return null;
        }

        public async Task<AuthResult> RegisterAsync(string email, string password, string displayName)
        {
            if (!IsValidEmail(email))
                return AuthResult.Fail("Geçerli bir e-posta adresi gir.");
            if (ValidatePassword(password) is { } passwordError)
                return AuthResult.Fail(passwordError);

            email = NormalizeEmail(email);
            if (await _context.Users.AnyAsync(u => u.Email == email))
                return AuthResult.Fail("Bu email zaten kayıtlı.");

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                // Ad girilmediyse mailin @ öncesi: ahmet@x.com → ahmet
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            await SendEmailVerificationAsync(user);

            return new AuthResult(true, GenerateJwtToken(user), null, user);
        }

        public async Task<AuthResult> LoginAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return AuthResult.Fail("Email ve şifre gerekli.");

            email = NormalizeEmail(email);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            // Kullanıcı yok ya da şifre yanlış: ikisinde de aynı mesaj ve aynı süre.
            var passwordOk = BCrypt.Net.BCrypt.Verify(password, user?.PasswordHash ?? DummyHash);
            if (user == null || !passwordOk)
                return AuthResult.Fail("Email veya şifre yanlış.");
            if (!user.IsActive)
                return AuthResult.Fail("Hesap deaktive edilmiş.");

            return new AuthResult(true, GenerateJwtToken(user), null, user);
        }

        public async Task RequestPasswordResetAsync(string email, string appBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(email)) return;
            email = NormalizeEmail(email);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
            if (user == null) return;

            var token = await _codes.CreateSecretTokenAsync(user.Id, OneTimeCodeService.PasswordReset, TimeSpan.FromMinutes(30));
            var link = $"{appBaseUrl}/auth/recover-password.html?email={Uri.EscapeDataString(email)}&token={token}";
            await TrySendAsync(email, EmailTemplates.PasswordReset(user.DisplayName, link));
        }

        public async Task<(bool success, string? error)> ResetPasswordAsync(string email, string token, string newPassword)
        {
            if (ValidatePassword(newPassword) is { } passwordError) return (false, passwordError);

            email = NormalizeEmail(email ?? string.Empty);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null || !await _codes.ConsumeAsync(user.Id, OneTimeCodeService.PasswordReset, token))
                return (false, "Sıfırlama bağlantısı geçersiz veya süresi dolmuş. Yeni bir bağlantı iste.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.UpdatedAt = DateTime.UtcNow;
            // Link maile geldiyse mail adresi de kanıtlanmış oluyor.
            user.EmailConfirmed = true;
            await _context.SaveChangesAsync();
            return (true, null);
        }

        public async Task<bool> SendEmailVerificationAsync(User user)
        {
            var code = await _codes.CreateNumericCodeAsync(user.Id, OneTimeCodeService.EmailVerify, TimeSpan.FromMinutes(30));
            return await TrySendAsync(user.Email, EmailTemplates.VerificationCode(user.DisplayName, code));
        }

        // Dışarı çıkan tek kullanıcı modeli bu; hash burada yok.
        public static UserInfo ToUserInfo(User user) => new()
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            EmailConfirmed = user.EmailConfirmed,
            Plan = user.Plan,
            CreatedAt = DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc)
        };

        private static bool IsValidEmail(string? email) =>
            !string.IsNullOrWhiteSpace(email) && email.Length <= 255 && MailAddress.TryCreate(email.Trim(), out var parsed) && parsed.Address == email.Trim();

        private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

        private string GenerateJwtToken(User user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Secret"] ?? ""));
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };
            var minutes = int.TryParse(_configuration["Jwt:ExpiryMinutes"], out var m) && m > 0 ? m : 60;

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(minutes),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
