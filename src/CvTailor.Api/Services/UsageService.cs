using Microsoft.EntityFrameworkCore;
using CvTailor.Api.Data;
using CvTailor.Api.Dtos;
using CvTailor.Api.Models;

namespace CvTailor.Api.Services
{
    // Plan ve aylık kota. Kota = bu ay UsageTracking'e yazılan satır sayısı.
    public class UsageService
    {
        // İlana göre bir CV yaklaşık 4 AI işlemi (ilan analizi, sorular, yeniden yazım, düzeltme);
        // ücretsiz planda ayda 10 civarı CV çıkıyor.
        public static readonly IReadOnlyList<PlanDefinition> Plans = new[]
        {
            new PlanDefinition("free", "Ücretsiz", 40),
            new PlanDefinition("pro", "Pro", 400)
        };

        private readonly CvTailorDbContext _context;

        public UsageService(CvTailorDbContext context) => _context = context;

        public static PlanDefinition GetPlan(string? plan) => Plans.FirstOrDefault(p => p.Key == plan) ?? Plans[0];

        private static DateTime MonthStartUtc()
        {
            var now = DateTime.UtcNow;
            return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        public async Task<UsageDto> GetUsageAsync(Guid userId, string plan)
        {
            var monthStart = MonthStartUtc();
            var used = await _context.UsageTracking.CountAsync(t => t.UserId == userId && t.Date >= monthStart);
            return new UsageDto
            {
                Plan = GetPlan(plan).Key,
                Used = used,
                Limit = GetPlan(plan).MonthlyLimit,
                ResetsAt = monthStart.AddMonths(1)
            };
        }

        // Son hakta aynı anda gelen iki istek ikisi de "yer var" görüp geçebiliyor.
        // sp_getapplock ile aynı kullanıcının istekleri sıraya giriyor: kilit → say → yer varsa ekle → commit.
        // Kilit kullanıcıya özel, farklı kullanıcılar birbirini beklemiyor.
        public async Task<Guid?> TryReserveAsync(Guid userId, string plan, string operation)
        {
            var limit = GetPlan(plan).MonthlyLimit;
            var monthStart = MonthStartUtc();

            // EnableRetryOnFailure açıkken elle transaction açmak için execution strategy şart.
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                var lockName = $"quota:{userId}";
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"EXEC sp_getapplock @Resource = {lockName}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000");

                var used = await _context.UsageTracking.CountAsync(t => t.UserId == userId && t.Date >= monthStart);
                if (used >= limit)
                {
                    await transaction.RollbackAsync();
                    return (Guid?)null;
                }

                var reservation = new UsageTracking
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Provider = "pending",
                    Operation = operation,
                    Date = DateTime.UtcNow
                };
                _context.UsageTracking.Add(reservation);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync(); // kilit transaction'la birlikte bırakılıyor
                return (Guid?)reservation.Id;
            });
        }

        public async Task CompleteAsync(Guid reservationId, string provider, int? tokens)
        {
            var row = await _context.UsageTracking.FindAsync(reservationId);
            if (row == null) return;
            row.Provider = provider;
            row.TokensUsed = tokens;
            await _context.SaveChangesAsync();
        }

        // İş olmadıysa (AI cevap vermedi vs.) hak geri veriliyor.
        public async Task ReleaseAsync(Guid reservationId) =>
            await _context.UsageTracking.Where(t => t.Id == reservationId).ExecuteDeleteAsync();
    }

    public record PlanDefinition(string Key, string Name, int MonthlyLimit);
}
