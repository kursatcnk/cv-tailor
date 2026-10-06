using CvTailor.Api.Data;
using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace CvTailor.Api.Services
{
    // Giriş yapmış kullanıcının kendi hesabı: profil, şifre, e-posta doğrulama, hesap silme.
    public class AccountService
    {
        private readonly CvTailorDbContext _context;
        private readonly UsageService _usage;
        private readonly AiService _ai;
        private readonly OneTimeCodeService _codes;
        private readonly IUserService _users;

        public AccountService(CvTailorDbContext context, UsageService usage, AiService ai, OneTimeCodeService codes, IUserService users)
        {
            _context = context;
            _usage = usage;
            _ai = ai;
            _codes = codes;
            _users = users;
        }

        // Arayüz açılışta tek istekle her şeyi alsın diye hepsi bir arada.
        public async Task<MeResponse?> GetMeAsync(Guid userId)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return null;

            var provider = _ai.ResolveProvider();
            return new MeResponse
            {
                User = UserService.ToUserInfo(user),
                Usage = await _usage.GetUsageAsync(user.Id, user.Plan),
                Ai = new AiStatusDto { Enabled = provider != null, Provider = provider?.DisplayName, Model = provider?.Model }
            };
        }

        public async Task<(bool success, string? error, UserInfo? user)> UpdateProfileAsync(Guid userId, string? displayName)
        {
            var name = displayName?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 100)
                return (false, "Ad 1-100 karakter arasında olmalı.", null);

            var user = await _context.Users.FirstAsync(u => u.Id == userId);
            user.DisplayName = name;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return (true, null, UserService.ToUserInfo(user));
        }

        public async Task<(bool success, string? error)> ChangePasswordAsync(Guid userId, string? currentPassword, string? newPassword)
        {
            var user = await _context.Users.FirstAsync(u => u.Id == userId);
            if (string.IsNullOrEmpty(currentPassword) || !BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
                return (false, "Mevcut şifre yanlış.");
            if (UserService.ValidatePassword(newPassword) is { } error)
                return (false, error);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool success, string? error)> VerifyEmailAsync(Guid userId, string? code)
        {
            var user = await _context.Users.FirstAsync(u => u.Id == userId);
            if (user.EmailConfirmed) return (true, null);
            if (!await _codes.ConsumeAsync(userId, OneTimeCodeService.EmailVerify, code))
                return (false, "Kod hatalı veya süresi dolmuş. Yeni kod isteyebilirsin.");

            user.EmailConfirmed = true;
            await _context.SaveChangesAsync();
            return (true, null);
        }

        // Zaten doğrulanmışsa mail atmadan true dönüyor.
        public async Task<bool> ResendVerificationAsync(Guid userId)
        {
            var user = await _context.Users.FirstAsync(u => u.Id == userId);
            return user.EmailConfirmed || await _users.SendEmailVerificationAsync(user);
        }

        // CV baştan sona kişisel veri. Kullanıcı silinince kasası, ilanları, ürettiği CV'ler,
        // kodları ve kullanım kayıtları cascade ile birlikte gidiyor.
        public async Task<(bool success, string? error)> DeleteAccountAsync(Guid userId, string? password)
        {
            var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
            if (string.IsNullOrEmpty(password) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                return (false, "Şifre yanlış.");

            await _context.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
            return (true, null);
        }
    }
}
