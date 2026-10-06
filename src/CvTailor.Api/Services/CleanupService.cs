using Microsoft.EntityFrameworkCore;
using CvTailor.Api.Data;

namespace CvTailor.Api.Services
{
    // Arka planda birkaç saatte bir çöp topluyor:
    // - süresi geçmiş / kullanılmış tek kullanımlık kodlar (UserTokens tablosu sonsuza kadar büyümesin)
    // - yarıda kalmış kota rezervasyonları (istek ortasında uygulama kapanırsa "pending" satır kalıyor ve hak yanıyor)
    public class CleanupService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<CleanupService> _logger;

        public CleanupService(IServiceScopeFactory scopes, ILogger<CleanupService> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Temizlik başarısız olursa uygulama çökmesin, bir sonraki turda tekrar dener.
                    _logger.LogWarning(ex, "Temizlik işi çalışamadı.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CvTailorDbContext>();
            var now = DateTime.UtcNow;

            var tokens = await db.UserTokens
                .Where(t => t.ExpiresAt < now.AddDays(-1) || (t.UsedAt != null && t.UsedAt < now.AddDays(-1)))
                .ExecuteDeleteAsync(ct);

            // Normal bir istek en fazla ~90 sn sürüyor; 30 dakikadır pending kalan satır kesin yetim.
            var reservations = await db.UsageTracking
                .Where(u => u.Provider == "pending" && u.Date < now.AddMinutes(-30))
                .ExecuteDeleteAsync(ct);

            if (tokens + reservations > 0)
                _logger.LogInformation("Temizlik: {Tokens} eski kod, {Reservations} yarım kalan rezervasyon silindi.", tokens, reservations);
        }
    }
}
