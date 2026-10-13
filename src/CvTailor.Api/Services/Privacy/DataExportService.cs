using System.Text.Json;
using System.Text.Json.Nodes;
using CvTailor.Api.Data;
using CvTailor.Api.Services.Cv;
using Microsoft.EntityFrameworkCore;

namespace CvTailor.Api.Services.Privacy
{
    // KVKK'daki "verilerimi görmek ve almak istiyorum" hakkı: kullanıcının bizde tuttuğumuz her şeyi tek JSON dosyası.
    // Hesap silme zaten her şeyi siliyor; bu, silmeden önce (ya da sadece merak ettiği için) alabilmesi için.
    // Şifre özeti, doğrulama kodları ve oturum anahtarları bilerek yok: bunlar kişisel veri değil, güvenlik bilgisi.
    public class DataExportService
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        private readonly CvTailorDbContext _context;
        private readonly VaultService _vault;

        public DataExportService(CvTailorDbContext context, VaultService vault)
        {
            _context = context;
            _vault = vault;
        }

        public async Task<byte[]> ExportAsync(Guid userId, CancellationToken ct)
        {
            var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
            var vault = await _vault.GetAsync(userId, ct);
            var targets = await _context.JobTargets.AsNoTracking()
                .Where(t => t.UserId == userId)
                .Include(t => t.TailoredCvs)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync(ct);
            var imports = await _context.CvImports.AsNoTracking()
                .Where(i => i.UserId == userId)
                .OrderBy(i => i.ImportedAt)
                .ToListAsync(ct);
            var usage = await _context.UsageTracking.AsNoTracking()
                .Where(u => u.UserId == userId)
                .OrderBy(u => u.Date)
                .Select(u => new { u.Operation, u.Provider, u.TokensUsed, u.Date })
                .ToListAsync(ct);

            var export = new
            {
                ExportedAt = DateTime.UtcNow,
                Account = new { user.Email, user.DisplayName, user.Plan, user.EmailConfirmed, user.CreatedAt },
                Vault = vault.Exists ? vault.Profile : null,
                Targets = targets.Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.Company,
                    t.Seniority,
                    t.ProfessionKey,
                    t.PostingText,
                    // JSON sütunları metin olarak değil, okunabilir yapı olarak.
                    Analysis = Parse(t.AnalysisJson),
                    Interview = Parse(t.InterviewJson),
                    t.CreatedAt,
                    Cvs = t.TailoredCvs.OrderBy(c => c.CreatedAt).Select(c => new
                    {
                        c.Id,
                        c.Status,
                        Document = Parse(c.ContentJson),
                        Changes = Parse(c.ChangesJson),
                        CoverLetter = Parse(c.CoverLetterJson),
                        c.CreatedAt,
                        c.UpdatedAt
                    })
                }),
                // Yüklenen dosyaların kendisi saklanmıyor, sadece okunan metin.
                Imports = imports.Select(i => new { i.FileName, i.ImportedAt, i.ExtractedText }),
                AiUsage = usage
            };
            return JsonSerializer.SerializeToUtf8Bytes(export, Json);
        }

        private static JsonNode? Parse(string? json) => string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
    }
}
