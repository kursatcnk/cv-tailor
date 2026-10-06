using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using CvTailor.Api.Data;
using CvTailor.Api.Models;

namespace CvTailor.Api.Services
{
    // Mail doğrulama kodu ve şifre sıfırlama linki için tek kullanımlık kodlar.
    // Kodun kendisini değil SHA-256'sını saklıyorum.
    public class OneTimeCodeService
    {
        public const string EmailVerify = "email-verify";
        public const string PasswordReset = "password-reset";

        // 6 haneli kodda 1 milyon ihtimal var; 5 denemeden sonra kod ölüyor, tahminle bulunamıyor.
        private const int MaxFailedAttempts = 5;
        private readonly CvTailorDbContext _context;

        public OneTimeCodeService(CvTailorDbContext context) => _context = context;

        public Task<string> CreateNumericCodeAsync(Guid userId, string purpose, TimeSpan lifetime) =>
            CreateAsync(userId, purpose, lifetime, RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6"));

        // Linklerde kullanılan uzun, tahmin edilemez anahtar.
        public Task<string> CreateSecretTokenAsync(Guid userId, string purpose, TimeSpan lifetime) =>
            CreateAsync(userId, purpose, lifetime, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());

        private async Task<string> CreateAsync(Guid userId, string purpose, TimeSpan lifetime, string code)
        {
            // Aynı amaçla yeni kod istenince eskiler iptal; elde her zaman sadece son kod geçerli.
            await _context.UserTokens.Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null).ExecuteDeleteAsync();

            _context.UserTokens.Add(new UserToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Purpose = purpose,
                TokenHash = Hash(code),
                ExpiresAt = DateTime.UtcNow.Add(lifetime),
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return code;
        }

        // Doğruysa kodu "kullanıldı" diye işaretliyor, ikinci kez geçmiyor. Yanlışsa deneme sayacı artıyor.
        public async Task<bool> ConsumeAsync(Guid userId, string purpose, string? code)
        {
            var token = await _context.UserTokens
                .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (token == null || token.ExpiresAt < DateTime.UtcNow || token.FailedAttempts >= MaxFailedAttempts || string.IsNullOrWhiteSpace(code))
                return false;

            // FixedTimeEquals: karşılaştırma süresi kodun ne kadarının tuttuğunu ele vermesin.
            var matches = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(token.TokenHash), Encoding.UTF8.GetBytes(Hash(code.Trim())));

            if (matches) token.UsedAt = DateTime.UtcNow;
            else token.FailedAttempts++;

            await _context.SaveChangesAsync();
            return matches;
        }

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
