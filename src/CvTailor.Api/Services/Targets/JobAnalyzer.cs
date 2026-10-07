using System.Globalization;
using System.Text.RegularExpressions;
using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Ai;

namespace CvTailor.Api.Services.Targets
{
    // İlan metninden pozisyonu, seviyeyi ve gereksinimleri çıkarıyor. Gereksinimler sonraki adımların temeli:
    // eşleşme her gereksinim için kasada kanıt arıyor, sorular kanıtı olmayanlar için soruluyor.
    public class JobAnalyzer
    {
        public static readonly string[] Categories = { "technical", "tool", "experience", "education", "language", "certification", "soft", "other" };
        public static readonly string[] Seniorities = { "junior", "mid", "senior" };

        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        private readonly AiService _ai;

        public JobAnalyzer(AiService ai) => _ai = ai;

        public async Task<(JobAnalysis Analysis, AiCompletion Completion)> AnalyzeAsync(string posting, CancellationToken cancellationToken)
        {
            var safe = posting.Replace("</posting>", "</posting_>", StringComparison.OrdinalIgnoreCase);
            var (analysis, completion) = await _ai.CompleteJsonAsync<JobAnalysis>(SystemPrompt, $"<posting>\n{safe}\n</posting>", cancellationToken);
            return (Normalize(analysis), completion);
        }

        // AI'a güvenmiyorum: sayıları sınırla, bilinmeyen değerleri at, anahtarları baştan ver (r1, r2...).
        public static JobAnalysis Normalize(JobAnalysis analysis)
        {
            analysis.Title = Clip(analysis.Title, 200);
            analysis.Company = Clip(analysis.Company, 200);
            analysis.Seniority = Seniorities.Contains(analysis.Seniority) ? analysis.Seniority : null;
            analysis.YearsOfExperience = analysis.YearsOfExperience is >= 0 and <= 40 ? analysis.YearsOfExperience : null;
            analysis.Summary = Clip(analysis.Summary, 500);

            var seen = new HashSet<string>();
            analysis.Requirements = (analysis.Requirements ?? new())
                .Where(r => r != null)
                .Select(r => new JobRequirement
                {
                    Text = Clip(r.Text, 300),
                    Importance = r.Importance == "nice" ? "nice" : "must",
                    Category = Categories.Contains(r.Category) ? r.Category : "other",
                    Terms = Clean(r.Terms, 8, 60)
                })
                .Where(r => r.Text != null && seen.Add(r.Text.ToLower(Tr)))
                // Önce zorunlular; sıralama kararlı, ilandaki sıra korunuyor.
                .OrderBy(r => r.Importance == "must" ? 0 : 1)
                .Take(25)
                .ToList();
            for (var i = 0; i < analysis.Requirements.Count; i++)
                analysis.Requirements[i].Key = $"r{i + 1}";

            analysis.Responsibilities = Clean(analysis.Responsibilities, 15, 300);
            analysis.Keywords = Clean(analysis.Keywords, 25, 60);
            return analysis;
        }

        private static List<string> Clean(List<string>? items, int max, int length) =>
            (items ?? new())
                .Select(s => Clip(s, length))
                .Where(s => s != null)
                .DistinctBy(s => s!.ToLower(Tr))
                .Take(max)
                .ToList()!;

        private static string? Clip(string? value, int max)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            return text.Length <= max ? text : text[..max].TrimEnd();
        }

        // ---- AI yokken ----

        private static readonly Regex Bullet = new(@"^\s*([•\-\*·▪◦●–]|\d{1,2}[.)])\s+(?<text>.+)$", RegexOptions.Compiled);
        private static readonly string[] MustHeadings = { "aranan", "nitelik", "gereksinim", "beklenti", "requirement", "qualification", "must have", "aradığımız" };
        private static readonly string[] NiceHeadings = { "tercih", "artı", "avantaj", "plus", "nice to have", "preferred", "bonus" };
        private static readonly string[] DutyHeadings = { "sorumluluk", "görev", "iş tanımı", "yapacakların", "responsibilit", "what you will do" };
        // Yan haklar ve şirket tanıtımı gereksinim değil; altındaki maddeler atlanıyor.
        private static readonly string[] SkipHeadings = { "yan hak", "sunduklarımız", "neler sunuyoruz", "olanak", "imkan", "benefit", "perks", "hakkımızda", "biz kimiz", "about us" };

        // Başlıklara göre bölümleri ayırıp madde işaretli satırları gereksinim sayıyor. Terimleri çıkaramıyor,
        // eşleşme o yüzden AI'sız modda daha zayıf; kullanıcıya bu söyleniyor.
        public static JobAnalysis AnalyzeWithoutAi(string posting)
        {
            var analysis = new JobAnalysis();
            var lines = posting.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count > 0 && lines[0].Length <= 80 && !Bullet.IsMatch(lines[0]))
                analysis.Title = lines[0];

            var section = "must";
            var sawHeading = false;
            foreach (var line in lines.Skip(1))
            {
                var match = Bullet.Match(line);
                if (!match.Success)
                {
                    // Kısa ve madde olmayan satır büyük ihtimalle bölüm başlığı.
                    if (line.Length > 60) continue;
                    var lower = line.ToLower(Tr);
                    if (SkipHeadings.Any(lower.Contains)) { section = "skip"; sawHeading = true; }
                    else if (NiceHeadings.Any(lower.Contains)) { section = "nice"; sawHeading = true; }
                    else if (MustHeadings.Any(lower.Contains)) { section = "must"; sawHeading = true; }
                    else if (DutyHeadings.Any(lower.Contains)) { section = "duty"; sawHeading = true; }
                    continue;
                }

                var text = match.Groups["text"].Value;
                if (section == "skip") continue;
                if (section == "duty") analysis.Responsibilities.Add(text);
                else analysis.Requirements.Add(new JobRequirement { Text = text, Importance = section, Category = "other" });
            }

            // Hiç başlık yoksa sorumluluk/gereksinim ayrımı yapılamıyor; hepsi gereksinim kalıyor.
            if (!sawHeading && analysis.Requirements.Count == 0)
                analysis.Requirements.AddRange(analysis.Responsibilities.Select(t => new JobRequirement { Text = t, Importance = "must", Category = "other" }));
            return Normalize(analysis);
        }

        private const string SystemPrompt = """
            You analyze a job posting (inside <posting> tags) so that a candidate's CV can be matched against it. Extract only what the posting actually says.

            Rules:
            - requirements: every distinct qualification the candidate is expected to have. Split combined lines ("C# ve SQL bilgisi") into separate requirements when they are separate skills. Do not include company benefits or the company description.
            - importance: "must" for required qualifications, "nice" for ones marked as preferred / a plus / tercih sebebi / artı / avantaj. If the posting does not distinguish, use "must".
            - category: "technical" (languages, frameworks, databases, methods), "tool" (software and tools: Git, Jira, Excel, SAP, Logo...), "experience" (years or type of experience), "education" (degree, field), "language" (spoken languages), "certification", "soft" (communication, teamwork...), "other" (driving licence, military service status, travel...).
            - text: a short phrase close to the posting's own wording, in the posting's language. Never add requirements the posting does not mention.
            - terms: the concrete words a CV would need to contain to prove this requirement, as written in the posting (e.g. ["ASP.NET Core", ".NET"], ["İngilizce"], ["Bilgisayar Mühendisliği"]). Empty for vague soft requirements.
            - seniority: "junior", "mid" or "senior" only if the title or required years make it clear (0-2 years junior, 3-5 mid, 6+ senior); otherwise null. yearsOfExperience: the minimum years required, or null.
            - responsibilities: what the person will do in the job, short phrases, in the posting's language.
            - keywords: up to 20 terms an applicant tracking system would search for (technologies, tools, domain words, the job title), as written in the posting.
            - summary: one or two sentences in Turkish describing the role.
            - title and company: from the posting; null if not stated.

            Return ONLY a JSON object with exactly this shape:
            {
              "title": "", "company": "", "seniority": null, "yearsOfExperience": null, "summary": "",
              "requirements": [ { "text": "", "importance": "must", "category": "technical", "terms": [""] } ],
              "responsibilities": [""],
              "keywords": [""]
            }
            """;
    }
}
