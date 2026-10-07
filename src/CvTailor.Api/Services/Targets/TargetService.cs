using System.Text.Json;
using CvTailor.Api.Data;
using CvTailor.Api.Dtos;
using CvTailor.Api.Models;
using CvTailor.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace CvTailor.Api.Services.Targets
{
    // CV'nin hedefi olan ilanlar. İlan bir kez çözümlenip saklanıyor; aynı ilana tekrar CV hazırlarken
    // kota yeniden harcanmıyor ve ilan yayından kalksa bile neyin istendiği kayıtlı kalıyor.
    public class TargetService
    {
        public const int MinPostingLength = 150;
        public const int MaxPostingLength = 20_000;

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly CvTailorDbContext _context;
        private readonly JobAnalyzer _analyzer;
        private readonly AiService _ai;
        private readonly UsageService _usage;
        private readonly ILogger<TargetService> _logger;

        public TargetService(CvTailorDbContext context, JobAnalyzer analyzer, AiService ai, UsageService usage, ILogger<TargetService> logger)
        {
            _context = context;
            _analyzer = analyzer;
            _ai = ai;
            _usage = usage;
            _logger = logger;
        }

        public async Task<TargetOutcome> CreateAsync(Guid userId, CreateTargetRequest request, CancellationToken ct)
        {
            var posting = request.PostingText?.Trim() ?? "";
            if (posting.Length < MinPostingLength)
                return TargetOutcome.Fail("İlan metni çok kısa. İlanın tamamını, özellikle aranan nitelikler kısmını yapıştır.");
            if (posting.Length > MaxPostingLength)
                return TargetOutcome.Fail("İlan metni çok uzun. Sadece ilanın kendisini yapıştır.");

            JobAnalysis analysis;
            var usedAi = false;
            string? notice = null;

            var provider = _ai.ResolveProvider();
            if (provider == null)
            {
                analysis = JobAnalyzer.AnalyzeWithoutAi(posting);
                notice = "AI anahtarı tanımlı değil. Gereksinimler ilandaki madde işaretli satırlardan çıkarıldı; eşleştirme bu modda daha zayıf olur.";
            }
            else
            {
                var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
                var reservation = await _usage.TryReserveAsync(userId, user.Plan, "analyze");
                if (reservation == null)
                    return TargetOutcome.QuotaExceeded(await _usage.GetUsageAsync(userId, user.Plan));

                try
                {
                    var (result, completion) = await _analyzer.AnalyzeAsync(posting, ct);
                    await _usage.CompleteAsync(reservation.Value, completion.Model, completion.InputTokens + completion.OutputTokens);
                    analysis = result;
                    usedAi = true;
                }
                catch (AiProviderException ex)
                {
                    await _usage.ReleaseAsync(reservation.Value);
                    _logger.LogWarning(ex, "İlan çözümlenemedi ({Provider}).", provider.Key);
                    analysis = JobAnalyzer.AnalyzeWithoutAi(posting);
                    notice = $"{ex.Message} Gereksinimler ilandaki madde işaretli satırlardan çıkarıldı.";
                }
            }

            if (analysis.Requirements.Count == 0)
                return TargetOutcome.Fail("İlanda gereksinim bulunamadı. \"Aranan nitelikler\" kısmının metinde olduğundan emin ol.");

            var target = new JobTarget
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                // Kullanıcının yazdığı başlık önce gelir; boşsa ilandan okunan.
                Title = Clip(request.Title) ?? analysis.Title ?? "Başlıksız ilan",
                Company = Clip(request.Company) ?? analysis.Company,
                PostingText = posting,
                Seniority = analysis.Seniority,
                AnalysisJson = JsonSerializer.Serialize(analysis, Json),
                CreatedAt = DateTime.UtcNow
            };
            _context.JobTargets.Add(target);
            await _context.SaveChangesAsync(ct);

            var dto = ToDto(target, analysis);
            dto.UsedAi = usedAi;
            dto.Notice = notice;
            return TargetOutcome.Ok(dto);
        }

        public async Task<List<TargetSummaryDto>> ListAsync(Guid userId, CancellationToken ct)
        {
            var targets = await _context.JobTargets
                .AsNoTracking()
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(ct);
            return targets.Select(t => (TargetSummaryDto)ToDto(t, Read(t))).ToList();
        }

        public async Task<TargetDto?> GetAsync(Guid userId, Guid id, CancellationToken ct)
        {
            var target = await _context.JobTargets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
            return target == null ? null : ToDto(target, Read(target));
        }

        // Hedefe bağlı CV'ler de cascade ile gidiyor.
        public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct) =>
            await _context.JobTargets.Where(t => t.Id == id && t.UserId == userId).ExecuteDeleteAsync(ct) > 0;

        private static JobAnalysis Read(JobTarget target) =>
            string.IsNullOrEmpty(target.AnalysisJson) ? new JobAnalysis() : JsonSerializer.Deserialize<JobAnalysis>(target.AnalysisJson, Json) ?? new JobAnalysis();

        private static TargetDto ToDto(JobTarget target, JobAnalysis analysis) => new()
        {
            Id = target.Id,
            Title = target.Title,
            Company = target.Company,
            Seniority = target.Seniority,
            MustCount = analysis.Requirements.Count(r => r.Importance == "must"),
            NiceCount = analysis.Requirements.Count(r => r.Importance == "nice"),
            CreatedAt = DateTime.SpecifyKind(target.CreatedAt, DateTimeKind.Utc),
            PostingText = target.PostingText,
            Analysis = analysis
        };

        private static string? Clip(string? value)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            return text.Length <= 200 ? text : text[..200];
        }
    }

    public record TargetOutcome(TargetDto? Target, string? Error, UsageDto? Usage)
    {
        public static TargetOutcome Ok(TargetDto target) => new(target, null, null);
        public static TargetOutcome Fail(string error) => new(null, error, null);
        public static TargetOutcome QuotaExceeded(UsageDto usage) => new(null, "Bu ayki AI hakkın doldu.", usage);
    }
}
