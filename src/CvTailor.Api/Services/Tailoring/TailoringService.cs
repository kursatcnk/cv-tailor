using System.Text.Json;
using CvTailor.Api.Data;
using CvTailor.Api.Dtos;
using CvTailor.Api.Models;
using CvTailor.Api.Services.Ai;
using CvTailor.Api.Services.Privacy;
using CvTailor.Api.Services.Cv;
using CvTailor.Api.Services.Matching;
using CvTailor.Api.Services.Targets;
using Microsoft.EntityFrameworkCore;

namespace CvTailor.Api.Services.Tailoring
{
    // İlana göre CV taslağı: seç (kod) → yeniden yaz (AI) → belgeyi kur → tek sayfaya sığdır (kod) → kaydet.
    // Taslak prova ekranına gidiyor; kullanıcı her değişikliği kabul ya da reddediyor.
    public class TailoringService
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private static readonly string[] Months = { "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
        private static readonly string[] DefaultOrder = { "experience", "projects", "education", "skills", "certificates" };

        private readonly CvTailorDbContext _context;
        private readonly TargetService _targets;
        private readonly VaultService _vault;
        private readonly RequirementMatcher _matcher;
        private readonly ProfessionCatalog _professions;
        private readonly BulletRewriter _rewriter;
        private readonly FabricationGuard _guard;
        private readonly AiService _ai;
        private readonly UsageService _usage;
        private readonly ILogger<TailoringService> _logger;

        public TailoringService(CvTailorDbContext context, TargetService targets, VaultService vault, RequirementMatcher matcher,
            ProfessionCatalog professions, BulletRewriter rewriter, FabricationGuard guard, AiService ai, UsageService usage, ILogger<TailoringService> logger)
        {
            _guard = guard;
            _context = context;
            _targets = targets;
            _vault = vault;
            _matcher = matcher;
            _professions = professions;
            _rewriter = rewriter;
            _ai = ai;
            _usage = usage;
            _logger = logger;
        }

        public async Task<TailoringOutcome> GenerateAsync(Guid userId, Guid targetId, CancellationToken ct)
        {
            var target = await _targets.GetAsync(userId, targetId, ct);
            if (target == null) return TailoringOutcome.Fail("Hedef bulunamadı.");
            var profile = (await _vault.GetAsync(userId, ct)).Profile;
            if (profile.Experiences.Count == 0 && profile.Projects.Count == 0)
                return TailoringOutcome.Fail("Kasanda deneyim ya da proje yok; önce kasanı doldur.");

            var today = DateTime.UtcNow;
            var match = _matcher.Match(target.Analysis, profile, today);
            var selection = CvLayoutPlanner.Select(profile, match, today);
            var input = BuildInput(target, profile, match, selection, today, out var tokens);

            RewriteOutput output;
            var usedAi = false;
            string? notice = null;
            var provider = _ai.ResolveProvider();
            if (provider == null)
            {
                output = Unchanged(input);
                notice = "AI anahtarı tanımlı değil. Maddeler yeniden yazılmadı; sadece ilana göre seçilip sıralandı.";
            }
            else
            {
                var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
                var reservation = await _usage.TryReserveAsync(userId, user.Plan, "rewrite");
                if (reservation == null)
                    return TailoringOutcome.QuotaExceeded(await _usage.GetUsageAsync(userId, user.Plan));
                try
                {
                    var (result, completion) = await _rewriter.RewriteAsync(input, PiiMask.ForProfile(profile), ct);
                    await _usage.CompleteAsync(reservation.Value, completion.Model, completion.InputTokens + completion.OutputTokens);
                    output = result;
                    usedAi = true;
                }
                catch (AiProviderException ex)
                {
                    await _usage.ReleaseAsync(reservation.Value);
                    _logger.LogWarning(ex, "CV yeniden yazılamadı ({Provider}).", provider.Key);
                    output = Unchanged(input);
                    notice = $"{ex.Message} Maddeler yeniden yazılmadı; sadece ilana göre seçilip sıralandı.";
                }
            }

            var (document, changes, scores) = Assemble(target, profile, match, selection, input, output, tokens);
            ApplyGuard(changes, target, profile, input);
            CvLayoutPlanner.Fit(document, changes, scores);
            document.Dropped.InsertRange(0, selection.Dropped);

            var cv = new TailoredCv
            {
                Id = Guid.NewGuid(),
                JobTargetId = targetId,
                Status = "draft",
                ContentJson = JsonSerializer.Serialize(document, Json),
                ChangesJson = JsonSerializer.Serialize(changes, Json),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.TailoredCvs.Add(cv);
            await _context.SaveChangesAsync(ct);

            var dto = ToDto(cv, target.Title, target.Analysis, document, changes);
            dto.Warnings = PiiScanner.Warnings(profile);
            dto.UsedAi = usedAi;
            dto.Notice = notice;
            return TailoringOutcome.Ok(dto);
        }

        public async Task<TailoredCvDto?> GetAsync(Guid userId, Guid id, CancellationToken ct)
        {
            var cv = await Load(userId, id).AsNoTracking().FirstOrDefaultAsync(ct);
            return cv == null ? null : ToDto(cv, Read<CvDocument>(cv.ContentJson), Read<List<CvChange>>(cv.ChangesJson));
        }

        // Prova ekranından: kabul, ret, elle düzenleme ya da alternatif seçimi. Elle yazılan ya da seçilen metin
        // uydurma korumasından tekrar geçiyor; kullanıcının kendi yazdığına da "kaynakta yok" uyarısı çıkabiliyor.
        // Uyarılı bir metni kabul etmek kullanıcının açık kararı; engellemiyoruz.
        public async Task<TailoringOutcome> UpdateChangeAsync(Guid userId, Guid id, string changeId, UpdateChangeRequest request, CancellationToken ct)
        {
            var cv = await Load(userId, id).FirstOrDefaultAsync(ct);
            if (cv == null) return TailoringOutcome.Fail("CV bulunamadı.");
            var changes = Read<List<CvChange>>(cv.ChangesJson);
            var change = changes.FirstOrDefault(c => c.Id == changeId);
            if (change == null) return TailoringOutcome.Fail("Değişiklik bulunamadı.");

            var text = request.Alternative is int i && i >= 0 && i < change.Alternatives.Count ? change.Alternatives[i] : request.Text?.Trim();
            if (text != null)
            {
                if (text.Length is < 10 or > 400) return TailoringOutcome.Fail("Madde 10-400 karakter arasında olmalı.");
                var profile = (await _vault.GetAsync(userId, ct)).Profile;
                var analysis = Read<JobAnalysis>(cv.JobTarget!.AnalysisJson ?? "{}");
                var targetTerms = analysis.Requirements.SelectMany(r => r.Terms).Concat(analysis.Keywords);
                change.EditedText = text == change.After ? null : text;
                change.Guard = _guard.Check(text, GuardSource(change, profile, InputForEdits(changes, profile)), targetTerms);
                change.Decision = "accepted";
            }
            if (request.Decision is "accepted" or "rejected") change.Decision = request.Decision;

            cv.ChangesJson = JsonSerializer.Serialize(changes, Json);
            cv.Status = "draft";
            cv.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            return TailoringOutcome.Ok(ToDto(cv, Read<CvDocument>(cv.ContentJson), changes));
        }

        // Onaylanan CV "ready" oluyor. Onay bekleyen (uyarılı) maddeler varsa onların orijinali basılıyor; kullanıcıya söylüyoruz.
        public async Task<TailoringOutcome> ApproveAsync(Guid userId, Guid id, CancellationToken ct)
        {
            var cv = await Load(userId, id).FirstOrDefaultAsync(ct);
            if (cv == null) return TailoringOutcome.Fail("CV bulunamadı.");
            var changes = Read<List<CvChange>>(cv.ChangesJson);
            cv.Status = "ready";
            cv.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            var dto = ToDto(cv, Read<CvDocument>(cv.ContentJson), changes);
            var pending = changes.Count(c => c.Decision == "pending");
            if (pending > 0) dto.Notice = $"Onay bekleyen {pending} maddenin orijinali kullanıldı.";
            return TailoringOutcome.Ok(dto);
        }

        public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct) =>
            await _context.TailoredCvs.Where(c => c.Id == id && c.JobTarget!.UserId == userId).ExecuteDeleteAsync(ct) > 0;

        private IQueryable<TailoredCv> Load(Guid userId, Guid id) =>
            _context.TailoredCvs.Include(c => c.JobTarget).Where(c => c.Id == id && c.JobTarget!.UserId == userId);

        // Düzenlemede yeniden yazım girdisi elimizde yok; özetin kaynağı için CV'deki maddelerin orijinallerinden kuruyoruz.
        private static RewriteInput InputForEdits(List<CvChange> changes, ProfileDto profile)
        {
            var months = ExperienceDuration.TotalMonths(profile.Experiences.Where(e => e.EmploymentType != "internship"), DateTime.UtcNow);
            return new RewriteInput
            {
                TotalExperience = months > 0 ? ExperienceDuration.Format(months) : null,
                Entries = changes.Where(c => c.Kind == "bullet").GroupBy(c => c.ParentId).Select(g => new RewriteInputEntry
                {
                    Title = profile.Experiences.FirstOrDefault(e => e.Id == g.Key)?.Title ?? profile.Projects.FirstOrDefault(p => p.Id == g.Key)?.Name ?? "",
                    Subtitle = profile.Experiences.FirstOrDefault(e => e.Id == g.Key)?.Company,
                    Facts = g.Select(c => new RewriteFact(c.Id, c.Before, false)).ToList()
                }).ToList()
            };
        }

        public async Task<List<TailoredCvSummaryDto>> ListAsync(Guid userId, Guid targetId, CancellationToken ct) =>
            await _context.TailoredCvs.AsNoTracking()
                .Where(c => c.JobTargetId == targetId && c.JobTarget!.UserId == userId)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new TailoredCvSummaryDto { Id = c.Id, Status = c.Status, CreatedAt = c.CreatedAt, UpdatedAt = c.UpdatedAt })
                .ToListAsync(ct);

        // ---- Yeniden yazım girdisi ----

        private RewriteInput BuildInput(TargetDto target, ProfileDto profile, MatchResult match, CvSelection selection, DateTime today, out Dictionary<string, Guid> tokens)
        {
            tokens = new Dictionary<string, Guid>();
            var input = new RewriteInput
            {
                TargetTitle = target.Title,
                Seniority = target.Seniority,
                Summary = profile.Summary,
                Keywords = target.Analysis.Keywords,
                Requirements = match.Items.Select(i => new RewriteRequirement(i.Key, i.Text, i.Importance, i.Strength)).ToList()
            };
            var months = ExperienceDuration.TotalMonths(profile.Experiences.Where(e => e.EmploymentType != "internship"), today);
            if (months > 0) input.TotalExperience = ExperienceDuration.Format(months);

            var factIndex = 1;
            for (var i = 0; i < selection.Entries.Count; i++)
            {
                var entry = selection.Entries[i];
                var token = $"e{i + 1}";
                tokens[token] = entry.SourceId;
                var (title, subtitle) = Titles(entry, profile);
                var inputEntry = new RewriteInputEntry { Token = token, Kind = entry.Kind, Title = title, Subtitle = subtitle };
                foreach (var fact in entry.Facts)
                {
                    var factToken = $"f{factIndex++}";
                    tokens[factToken] = fact.Id!.Value;
                    inputEntry.Facts.Add(new RewriteFact(factToken, fact.Text ?? "", fact.Source == "interview"));
                }
                input.Entries.Add(inputEntry);
            }
            return input;
        }

        // AI yokken ya da hata verdiğinde: maddeler olduğu gibi, sadece seçilmiş ve sıralanmış.
        private static RewriteOutput Unchanged(RewriteInput input) => BulletRewriter.Clean(new RewriteOutput(), input);

        // ---- Uydurma koruması ----

        // Her yeni madde dayandığı kasa maddeleriyle karşılaştırılıyor. İşaretlenen madde "pending" başlıyor:
        // kullanıcı prova ekranında onaylamadan CV'ye orijinali basılıyor.
        private void ApplyGuard(List<CvChange> changes, TargetDto target, ProfileDto profile, RewriteInput input)
        {
            var targetTerms = target.Analysis.Requirements.SelectMany(r => r.Terms).Concat(target.Analysis.Keywords).ToList();
            foreach (var change in changes)
            {
                if (change.After == change.Before) continue;
                change.Guard = _guard.Check(change.After, GuardSource(change, profile, input), targetTerms);
                if (change.Guard.Status == "flagged") change.Decision = "pending";
            }
        }

        // Maddenin kaynağı: dayandığı maddeler + bağlı olduğu iş/proje adı (madde "Örnek Lojistik'te" diyebilir).
        // Özetin kaynağı daha geniş: bütün seçili maddeler, unvanlar, beceriler, eğitim ve kodun hesapladığı toplam süre.
        public static string GuardSource(CvChange change, ProfileDto profile, RewriteInput input)
        {
            if (change.Kind == "summary")
            {
                // Prompt bir yıldan kısa deneyim için "1 yıldan az" yazdırıyor; o 1 rakamı uydurma sayılmasın.
                var underAYear = input.TotalExperience == null || !input.TotalExperience.Contains("yıl");
                var parts = input.Entries.SelectMany(e => e.Facts.Select(f => f.Text).Append(e.Title).Append(e.Subtitle ?? ""))
                    .Append(profile.Summary ?? "").Append(profile.Headline ?? "")
                    .Concat(profile.Skills.Select(s => s.Name ?? ""))
                    .Concat(profile.Educations.Select(e => $"{e.Field} {e.Degree}"))
                    .Append(input.TotalExperience == null ? "" : $"Toplam iş deneyimi: {input.TotalExperience}")
                    .Append(underAYear ? "1 yıldan az" : "");
                return string.Join(" ", parts);
            }
            var parent = profile.Experiences.FirstOrDefault(e => e.Id == change.ParentId) is { } exp
                ? $"{exp.Title} {exp.Company}"
                : profile.Projects.FirstOrDefault(p => p.Id == change.ParentId) is { } project ? $"{project.Name} {project.Description}" : "";
            return $"{change.Before} {parent}";
        }

        // ---- Belge ----

        private (CvDocument, List<CvChange>, Dictionary<string, double>) Assemble(TargetDto target, ProfileDto profile, MatchResult match,
            CvSelection selection, RewriteInput input, RewriteOutput output, Dictionary<string, Guid> tokens)
        {
            var factTexts = input.Entries.SelectMany(e => e.Facts).ToDictionary(f => f.Token, f => f.Text);
            var changes = new List<CvChange>();
            var scores = new Dictionary<string, double>();
            var document = new CvDocument
            {
                Header = new CvHeader
                {
                    FullName = profile.FullName,
                    Headline = profile.Headline,
                    Email = profile.Email,
                    Phone = profile.Phone,
                    Location = profile.Location,
                    Links = new[] { profile.LinkedInUrl, profile.GitHubUrl, profile.WebsiteUrl }
                        .Where(l => !string.IsNullOrEmpty(l))
                        .Select(l => l!.Replace("https://", "").Replace("http://", "").TrimEnd('/'))
                        .ToList()
                },
                SectionOrder = SectionOrder(target, profile)
            };

            if (output.Summary?.Text != null)
            {
                var summary = new CvChange
                {
                    Id = "summary",
                    Kind = "summary",
                    SourceIds = output.Summary.Sources!.Where(tokens.ContainsKey).Select(t => tokens[t]).ToList(),
                    Before = profile.Summary ?? "",
                    After = output.Summary.Text,
                    Reason = output.Summary.Reason?.Trim() ?? "İlana göre kısa bir özet eklendi.",
                    // Özetin kaynakları da metin olarak lazım; uydurma koruması özet için bunlara bakıyor.
                    Alternatives = new()
                };
                changes.Add(summary);
                document.SummaryChangeId = summary.Id;
            }
            else if (!string.IsNullOrEmpty(profile.Summary))
            {
                changes.Add(new CvChange { Id = "summary", Kind = "summary", Before = profile.Summary, After = profile.Summary, Reason = "Değiştirilmedi." });
                document.SummaryChangeId = "summary";
            }

            var changeIndex = 1;
            foreach (var entry in input.Entries)
            {
                var sourceId = tokens[entry.Token];
                var selected = selection.Entries.First(e => e.SourceId == sourceId);
                var cvEntry = BuildEntry(selected, profile);
                foreach (var bullet in output.Entries.First(e => e.Id == entry.Token).Bullets)
                {
                    var change = new CvChange
                    {
                        Id = $"c{changeIndex++}",
                        ParentId = sourceId,
                        SourceIds = bullet.Sources.Select(s => tokens[s]).ToList(),
                        Before = string.Join(" ", bullet.Sources.Select(s => factTexts[s])),
                        After = bullet.Text,
                        Alternatives = bullet.Alternatives,
                        Reason = string.IsNullOrEmpty(bullet.Reason) ? "Değiştirilmedi." : bullet.Reason,
                        Requirements = bullet.Requirements
                    };
                    changes.Add(change);
                    cvEntry.BulletChangeIds.Add(change.Id);
                    scores[change.Id] = change.SourceIds.Sum(id => selection.Scores.GetValueOrDefault(id));
                }
                (selected.Kind == "project" ? document.Projects : document.Experiences).Add(cvEntry);
            }

            document.Educations = profile.Educations.Select(e => new CvEducation
            {
                School = e.School ?? "",
                Detail = string.Join(", ", new[] { e.Field, e.Degree, e.Gpa != null ? $"Not ortalaması {e.Gpa}" : null }.Where(x => !string.IsNullOrEmpty(x))),
                Period = Period(e.StartDate, e.EndDate, false)
            }).ToList();
            document.Skills = SkillGroups(profile, match);
            document.Certificates = profile.Certificates
                .Select(c => string.Join(" · ", new[] { c.Name, c.Issuer, c.Date?[..4] }.Where(x => !string.IsNullOrEmpty(x))))
                .ToList();
            return (document, changes, scores);
        }

        private static CvEntry BuildEntry(CvSelectedEntry selected, ProfileDto profile)
        {
            if (selected.Kind == "project")
            {
                var p = profile.Projects.First(x => x.Id == selected.SourceId);
                return new CvEntry { SourceId = selected.SourceId, Title = p.Name ?? "", Subtitle = p.Url?.Replace("https://", "").TrimEnd('/'), Period = Period(p.StartDate, p.EndDate, false) };
            }
            var e = profile.Experiences.First(x => x.Id == selected.SourceId);
            return new CvEntry
            {
                SourceId = selected.SourceId,
                Title = e.EmploymentType == "internship" && e.Title?.Contains("Staj", StringComparison.OrdinalIgnoreCase) != true ? $"{e.Title} (Staj)" : e.Title ?? "",
                Subtitle = e.Company,
                Location = e.Location,
                Period = Period(e.StartDate, e.EndDate, e.IsCurrent)
            };
        }

        private static (string Title, string? Subtitle) Titles(CvSelectedEntry entry, ProfileDto profile)
        {
            if (entry.Kind == "project")
            {
                var p = profile.Projects.First(x => x.Id == entry.SourceId);
                return (p.Name ?? "", p.Description);
            }
            var e = profile.Experiences.First(x => x.Id == entry.SourceId);
            return (e.Title ?? "", e.Company);
        }

        // İlanla eşleşen beceriler önde; sonra kasadaki sırası. Teknik 12, araç 10 ile sınırlı; liste okunur kalsın.
        private static List<CvSkillGroup> SkillGroups(ProfileDto profile, MatchResult match)
        {
            var matched = match.Items.SelectMany(i => i.Evidence).Where(e => e.Kind == "skill" && e.SourceId != null).Select(e => e.SourceId!.Value).ToHashSet();
            List<string> Names(string category, int max) => profile.Skills
                .Where(s => s.Category == category)
                .Select((s, i) => (Skill: s, Order: i))
                .OrderBy(x => matched.Contains(x.Skill.Id ?? Guid.Empty) ? 0 : 1).ThenBy(x => x.Order)
                .Take(max)
                .Select(x => x.Skill.Level != null ? $"{x.Skill.Name} ({x.Skill.Level})" : x.Skill.Name!)
                .ToList();

            return new[]
            {
                new CvSkillGroup { Label = "Teknik", Items = Names("technical", 12) },
                new CvSkillGroup { Label = "Araçlar", Items = Names("tool", 10) },
                new CvSkillGroup { Label = "Diller", Items = Names("language", 5) },
                new CvSkillGroup { Label = "Yetkinlikler", Items = Names("soft", 6) }
            }.Where(g => g.Items.Count > 0).ToList();
        }

        // Meslek profili bir sıra öneriyorsa o; iş deneyimi olmayan için projeler önde.
        private List<string> SectionOrder(TargetDto target, ProfileDto profile)
        {
            var profession = _professions.Find(target.ProfessionKey);
            if (profession?.Sections.Count > 0) return profession.Sections;
            return profile.Experiences.Any(e => e.EmploymentType != "internship")
                ? DefaultOrder.ToList()
                : new List<string> { "projects", "experience", "education", "skills", "certificates" };
        }

        private static string? Period(string? start, string? end, bool current)
        {
            var from = Format(start);
            var to = current ? "Devam ediyor" : Format(end);
            return from == null ? to : to == null ? from : $"{from} – {to}";
        }

        private static string? Format(string? date) =>
            string.IsNullOrEmpty(date) ? null
            : date.Length >= 7 && int.TryParse(date[5..7], out var m) && m is >= 1 and <= 12 ? $"{Months[m - 1]} {date[..4]}"
            : date[..Math.Min(4, date.Length)];

        private static T Read<T>(string json) where T : new() => JsonSerializer.Deserialize<T>(json, Json) ?? new T();

        private static TailoredCvDto ToDto(TailoredCv cv, CvDocument document, List<CvChange> changes) =>
            ToDto(cv, cv.JobTarget!.Title, Read<JobAnalysis>(cv.JobTarget.AnalysisJson ?? "{}"), document, changes);

        private static TailoredCvDto ToDto(TailoredCv cv, string targetTitle, JobAnalysis analysis, CvDocument document, List<CvChange> changes) => new()
        {
            Id = cv.Id,
            TargetId = cv.JobTargetId,
            TargetTitle = targetTitle,
            RequirementLabels = analysis.Requirements.Where(r => r.Key != null && r.Text != null).ToDictionary(r => r.Key!, r => r.Text!),
            Status = cv.Status,
            Document = document,
            Changes = changes,
            CreatedAt = DateTime.SpecifyKind(cv.CreatedAt, DateTimeKind.Utc),
            UpdatedAt = DateTime.SpecifyKind(cv.UpdatedAt, DateTimeKind.Utc)
        };
    }

    public class TailoredCvSummaryDto
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "draft";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public record TailoringOutcome(TailoredCvDto? Cv, string? Error, UsageDto? Usage)
    {
        public static TailoringOutcome Ok(TailoredCvDto cv) => new(cv, null, null);
        public static TailoringOutcome Fail(string error) => new(null, error, null);
        public static TailoringOutcome QuotaExceeded(UsageDto usage) => new(null, "Bu ayki AI hakkın doldu.", usage);
    }
}
