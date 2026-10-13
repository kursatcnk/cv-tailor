using System.Text.RegularExpressions;
using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Matching;

namespace CvTailor.Api.Services.Tailoring
{
    // Önce/sonra karnesi: kullanıcının elindeki CV ile ilana göre yazılan CV aynı ölçülerle karşılaştırılıyor.
    // Puan uydurmuyoruz; her satır sayılabilen bir şey (kaç kelime, kaç gereksinim, kaç madde).
    public static class CvScorecard
    {
        private static readonly Regex Digit = new(@"\d", RegexOptions.Compiled);

        public static Scorecard Compute(JobAnalysis analysis, SkillDictionary dictionary,
            string beforeText, IReadOnlyCollection<string> beforeBullets, string afterText, IReadOnlyCollection<string> afterBullets)
        {
            var card = new Scorecard();

            // ATS ilandaki yazılışı arıyor; eşdeğer saymıyoruz ("MSSQL" isteyen ilan için "SQL Server" yazmak bu satırda sayılmaz).
            // Anahtar kelime listesi yoksa gereksinim terimleri; ama "C#, Java, Python..." gibi seçenek listeleri
            // hepsi istenmiş gibi saymasın diye önce çözümleyicinin seçtiği anahtar kelimeler.
            var keywords = (analysis.Keywords.Count > 0 ? analysis.Keywords : analysis.Requirements.SelectMany(r => r.Terms))
                .Select(k => k.Trim()).Where(k => k.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (keywords.Count > 0)
            {
                bool In(string text, string k) => dictionary.Contains(text, k, strict: false);
                card.Rows.Add(new ScoreRow("İlandaki anahtar kelimeler", keywords.Count(k => In(beforeText, k)), keywords.Count(k => In(afterText, k)), keywords.Count,
                    "ATS'nin arayacağı kelimeler, ilandaki yazılışıyla."));
            }

            // İnsan okuyucu eşdeğeri de anlar: zorunlu gereksinimin herhangi bir yazılışı CV'de görünüyor mu?
            var must = analysis.Requirements.Where(r => r.Importance == "must" && r.Terms.Count > 0).ToList();
            if (must.Count > 0)
            {
                bool Shown(string text, JobRequirement r) => r.Terms.Any(t => dictionary.EvidenceTerms(t).Any(e => dictionary.Contains(text, e, strict: false)));
                card.Rows.Add(new ScoreRow("Zorunlu gereksinimler", must.Count(r => Shown(beforeText, r)), must.Count(r => Shown(afterText, r)), must.Count,
                    "CV'de kanıtı görünen zorunlu gereksinimler."));
                card.MissingRequirements = must.Where(r => !Shown(afterText, r)).Select(r => r.Text ?? "").Where(t => t.Length > 0).Take(5).ToList();
            }

            card.Rows.Add(new ScoreRow("Rakam içeren maddeler", beforeBullets.Count(b => Digit.IsMatch(b)), afterBullets.Count(b => Digit.IsMatch(b)), null,
                $"Önce {beforeBullets.Count}, sonra {afterBullets.Count} madde."));
            return card;
        }

        // Basılan CV'nin düz metni: indirilen dosyada ne varsa o.
        public static string PrintedText(TailoredCvDto cv)
        {
            var d = cv.Document;
            var changes = cv.Changes.ToDictionary(c => c.Id);
            string? Final(string id) => changes.TryGetValue(id, out var c) ? c.FinalText : null;
            var parts = new List<string?> { d.Header.Headline, d.SummaryChangeId == null ? null : Final(d.SummaryChangeId) };
            foreach (var e in d.Experiences.Concat(d.Projects))
            {
                parts.Add(e.Title);
                parts.Add(e.Subtitle);
                parts.AddRange(e.BulletChangeIds.Select(Final));
            }
            parts.AddRange(d.Educations.Select(e => $"{e.School} {e.Detail}"));
            parts.AddRange(d.Skills.SelectMany(s => s.Items));
            parts.AddRange(d.Certificates);
            return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        // Yüklenen CV metnindeki madde satırları: "•", "-", "*" gibi işaretle başlayan, cümle uzunluğundaki satırlar.
        // Alt satıra taşan devamı alınmıyor; PDF'te devam satırını sonraki girişin tarih satırından ayırmak güvenilir değil.
        public static List<string> BulletLines(string text) => text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && "•-*▪●◦–·".Contains(l[0]))
            .Select(l => l.TrimStart('•', '-', '*', '▪', '●', '◦', '–', '·', ' '))
            .Where(l => l.Length >= 15)
            .ToList();

        public static List<string> PrintedBullets(TailoredCvDto cv)
        {
            var changes = cv.Changes.ToDictionary(c => c.Id);
            return cv.Document.Experiences.Concat(cv.Document.Projects)
                .SelectMany(e => e.BulletChangeIds)
                .Select(id => changes.TryGetValue(id, out var c) ? c.FinalText : null)
                .OfType<string>()
                .Where(t => t.Length > 0)
                .ToList();
        }
    }

    public class Scorecard
    {
        // "Yüklediğin CV" ya da (CV yüklenmediyse) "Kasandaki bilgiler"
        public string BeforeLabel { get; set; } = string.Empty;
        public List<ScoreRow> Rows { get; set; } = new();
        // Uyarlamadan sonra da CV'de görünmeyen zorunlu gereksinimler; kasada kanıtı yoksa uydurmuyoruz, kullanıcı bilsin.
        public List<string> MissingRequirements { get; set; } = new();
    }

    // Total null ise oran değil sayı (madde sayıları farklı olabiliyor).
    public record ScoreRow(string Label, int Before, int After, int? Total, string Hint);
}
