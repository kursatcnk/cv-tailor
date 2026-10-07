using CvTailor.Api.Data;
using CvTailor.Api.Dtos;
using CvTailor.Api.Models;
using CvTailor.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace CvTailor.Api.Services.Cv
{
    // Yüklenen CV'yi kontrol ekranına gidecek taslağa çeviriyor. Kasaya hiçbir şey yazmıyor;
    // kullanıcı taslağı kontrol edip "Kasaya kaydet" deyince VaultService yazıyor.
    public class CvImportService
    {
        // Ayrıştırmayı tekrar denemek için metin tutuluyor; fazlası gereksiz kişisel veri.
        private const int KeepImports = 3;

        private readonly CvTailorDbContext _context;
        private readonly CvTextExtractor _extractor;
        private readonly CvParser _parser;
        private readonly AiService _ai;
        private readonly UsageService _usage;
        private readonly ILogger<CvImportService> _logger;

        public CvImportService(CvTailorDbContext context, CvTextExtractor extractor, CvParser parser, AiService ai,
            UsageService usage, ILogger<CvImportService> logger)
        {
            _context = context;
            _extractor = extractor;
            _parser = parser;
            _ai = ai;
            _usage = usage;
            _logger = logger;
        }

        public Task<CvImportOutcome> ImportFileAsync(Guid userId, Stream stream, string fileName, CancellationToken ct) =>
            ImportAsync(userId, fileName, () => _extractor.Extract(stream, fileName), ct);

        public Task<CvImportOutcome> ImportTextAsync(Guid userId, string text, CancellationToken ct) =>
            ImportAsync(userId, "yapıştırılan metin", () => _extractor.FromText(text), ct);

        private async Task<CvImportOutcome> ImportAsync(Guid userId, string fileName, Func<CvTextResult> extract, CancellationToken ct)
        {
            CvTextResult extracted;
            try { extracted = extract(); }
            catch (CvImportException ex) { return CvImportOutcome.Fail(ex.Message); }

            var response = new CvImportResponse { ExtractedText = extracted.Text };
            if (extracted.HasUnreadableLetters)
                response.Warnings.Add("PDF'teki bazı harf birleşimleri (ör. \"ti\", \"fi\") okunamadı. Bunlar tamamlanmaya çalışıldı; kelimeleri kontrol et.");

            var provider = _ai.ResolveProvider();
            if (provider == null)
            {
                response.Profile = CvParser.ParseContactsOnly(extracted.Text);
                response.Notice = "AI anahtarı tanımlı değil. İletişim bilgileri dolduruldu; deneyim ve eğitimleri soldaki metne bakarak ekleyebilirsin.";
            }
            else
            {
                var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
                var reservation = await _usage.TryReserveAsync(userId, user.Plan, "parse");
                if (reservation == null)
                    return CvImportOutcome.QuotaExceeded(await _usage.GetUsageAsync(userId, user.Plan));

                try
                {
                    var (profile, completion) = await _parser.ParseAsync(extracted.Text, ct);
                    await _usage.CompleteAsync(reservation.Value, completion.Model, completion.InputTokens + completion.OutputTokens);
                    response.Profile = profile;
                    response.UsedAi = true;
                }
                catch (AiProviderException ex)
                {
                    // Hak yanmasın; kullanıcı yine de iletişim bilgileri dolu bir formla devam edebilsin.
                    await _usage.ReleaseAsync(reservation.Value);
                    _logger.LogWarning(ex, "CV ayrıştırılamadı ({Provider}).", provider.Key);
                    response.Profile = CvParser.ParseContactsOnly(extracted.Text);
                    response.Notice = $"{ex.Message} İletişim bilgileri dolduruldu; geri kalanını soldaki metne bakarak ekleyebilirsin.";
                }
            }

            if (response.UsedAi && response.Profile.Experiences.Count == 0 && response.Profile.Educations.Count == 0)
                response.Warnings.Add("Metinde deneyim ya da eğitim bulunamadı. Doğru dosyayı yüklediğinden emin ol.");

            var import = new CvImport
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FileName = Path.GetFileName(fileName),
                ExtractedText = extracted.Text,
                ImportedAt = DateTime.UtcNow
            };
            _context.CvImports.Add(import);
            await _context.SaveChangesAsync(ct);
            await _context.CvImports
                .Where(i => i.UserId == userId)
                .OrderByDescending(i => i.ImportedAt)
                .Skip(KeepImports)
                .ExecuteDeleteAsync(ct);

            response.ImportId = import.Id;
            return CvImportOutcome.Ok(response);
        }
    }

    public record CvImportOutcome(CvImportResponse? Response, string? Error, UsageDto? Usage)
    {
        public static CvImportOutcome Ok(CvImportResponse response) => new(response, null, null);
        public static CvImportOutcome Fail(string error) => new(null, error, null);
        public static CvImportOutcome QuotaExceeded(UsageDto usage) => new(null, "Bu ayki AI hakkın doldu.", usage);
    }
}
