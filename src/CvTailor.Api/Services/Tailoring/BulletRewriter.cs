using System.Text;
using System.Text.RegularExpressions;
using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Ai;
using CvTailor.Api.Services.Privacy;

namespace CvTailor.Api.Services.Tailoring
{
    // Seçilen maddeleri ilana göre yeniden yazdırıyor. AI'a her madde kısa bir etiketle (f1, f2...) gidiyor ve
    // her yeni madde hangi etiketlerden türediğini söylemek zorunda. Uzun Guid'ler yerine etiket kullanmak hem
    // token tasarrufu hem de AI'ın var olmayan bir kimlik uydurmasını zorlaştırıyor.
    public class BulletRewriter
    {
        private const int MaxBulletLength = 300;
        private static readonly string[] VaguePhrases = { "görev aldım", "yer aldım", "katılım sağladım", "sorumluydum", "çeşitli" };
        private readonly AiService _ai;

        public BulletRewriter(AiService ai) => _ai = ai;

        // AI'a kişisel verileri maskelenmiş bir kopya gidiyor; cevap geri açılıyor. Çağıran taraf maskesiz girdiyle çalışmaya devam ediyor.
        public async Task<(RewriteOutput Output, AiCompletion Completion)> RewriteAsync(RewriteInput input, PiiMask mask, CancellationToken cancellationToken)
        {
            var masked = Masked(input, mask);
            var (output, completion) = await _ai.CompleteJsonAsync<RewriteOutput>(SystemPrompt, BuildMessage(masked), cancellationToken);
            return (Unmasked(Clean(output, masked), mask), completion);
        }

        private static RewriteInput Masked(RewriteInput input, PiiMask mask) => new()
        {
            TargetTitle = input.TargetTitle,
            Seniority = input.Seniority,
            Summary = input.Summary == null ? null : mask.Mask(input.Summary),
            TotalExperience = input.TotalExperience,
            Requirements = input.Requirements,
            Keywords = input.Keywords,
            Entries = input.Entries.Select(e => new RewriteInputEntry
            {
                Token = e.Token,
                Kind = e.Kind,
                Title = mask.Mask(e.Title),
                Subtitle = e.Subtitle == null ? null : mask.Mask(e.Subtitle),
                Facts = e.Facts.Select(f => f with { Text = mask.Mask(f.Text) }).ToList()
            }).ToList()
        };

        private static RewriteOutput Unmasked(RewriteOutput output, PiiMask mask)
        {
            if (output.Summary != null)
            {
                output.Summary.Text = mask.Unmask(output.Summary.Text);
                output.Summary.Reason = mask.Unmask(output.Summary.Reason);
            }
            foreach (var bullet in output.Entries.SelectMany(e => e.Bullets))
            {
                bullet.Text = mask.Unmask(bullet.Text);
                bullet.Reason = mask.Unmask(bullet.Reason);
                bullet.Alternatives = bullet.Alternatives.Select(mask.Unmask).ToList();
            }
            return output;
        }

        // Cevaba körü körüne güvenmiyorum: bilinmeyen iş/madde etiketlerini at, bir maddeyi iki yerde kullanma,
        // AI'ın atladığı maddeleri olduğu gibi geri koy.
        public static RewriteOutput Clean(RewriteOutput output, RewriteInput input)
        {
            var result = new RewriteOutput { Summary = output.Summary };
            var used = new HashSet<string>();

            foreach (var entry in input.Entries)
            {
                var factIds = entry.Facts.Select(f => f.Token).ToHashSet();
                var bullets = new List<RewriteBullet>();
                foreach (var bullet in output.Entries.FirstOrDefault(e => e.Id == entry.Token)?.Bullets ?? new())
                {
                    var sources = (bullet.Sources ?? new()).Where(s => factIds.Contains(s) && !used.Contains(s)).Distinct().ToList();
                    if (sources.Count == 0 || string.IsNullOrWhiteSpace(bullet.Text)) continue;
                    used.UnionWith(sources);
                    bullets.Add(new RewriteBullet
                    {
                        Text = Clip(bullet.Text),
                        Sources = sources,
                        Requirements = (bullet.Requirements ?? new()).Where(r => input.Requirements.Any(x => x.Key == r)).Distinct().ToList(),
                        // Küçük modeller "sadece başka türlü yaz" deyince belirsiz kalıplara dönebiliyor; o alternatifleri göstermiyoruz.
                        Alternatives = (bullet.Alternatives ?? new())
                            .Where(a => !string.IsNullOrWhiteSpace(a) && !VaguePhrases.Any(v => a.Contains(v, StringComparison.OrdinalIgnoreCase)))
                            .Select(a => Clip(a)).Take(2).ToList(),
                        Reason = WithoutIds(bullet.Reason)
                    });
                }
                foreach (var fact in entry.Facts.Where(f => !used.Contains(f.Token)))
                    bullets.Add(new RewriteBullet { Text = fact.Text, Sources = { fact.Token }, Reason = "Değiştirilmedi." });
                result.Entries.Add(new RewriteEntry { Id = entry.Token, Bullets = bullets });
            }

            if (result.Summary != null)
            {
                var allFacts = input.Entries.SelectMany(e => e.Facts.Select(f => f.Token)).Append("s0").Append("y1").ToHashSet();
                result.Summary.Sources = (result.Summary.Sources ?? new()).Where(allFacts.Contains).Distinct().ToList();
                result.Summary.Text = string.IsNullOrWhiteSpace(result.Summary.Text) ? null : Clip(result.Summary.Text, 600);
                result.Summary.Reason = WithoutIds(result.Summary.Reason);
                if (result.Summary.Text == null || result.Summary.Sources.Count == 0) result.Summary = null;
            }
            return result;
        }

        // Prompt istemese de AI bazen "f1 ve f2 maddelerini birleştirdim" yazıyor; kullanıcı bu etiketleri görmüyor.
        private static readonly Regex Ids = new(@"\b[efy]\d+\b(\s*(ve|,)\s*\b[efy]\d+\b)*\s*(maddeler(in)?i|maddesini|maddesi|bilgilerini|bilgisini)?\s*", RegexOptions.Compiled);

        public static string WithoutIds(string? reason)
        {
            var text = Ids.Replace(reason?.Trim() ?? "", "").Trim();
            return text.Length > 0 ? char.ToUpper(text[0], new System.Globalization.CultureInfo("tr-TR")) + text[1..] : text;
        }

        private static string Clip(string text) => Clip(text, MaxBulletLength);
        private static string Clip(string text, int max)
        {
            var t = text.Trim();
            return t.Length <= max ? t : t[..max].TrimEnd();
        }

        private static string BuildMessage(RewriteInput input)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<target title=\"{Escape(input.TargetTitle)}\" seniority=\"{input.Seniority ?? "unknown"}\">");
            foreach (var r in input.Requirements)
                sb.AppendLine($"  <requirement key=\"{r.Key}\" importance=\"{r.Importance}\" evidence=\"{r.Strength}\">{Escape(r.Text)}</requirement>");
            if (input.Keywords.Count > 0)
                sb.AppendLine($"  <keywords>{Escape(string.Join(", ", input.Keywords))}</keywords>");
            sb.AppendLine("</target>");
            sb.AppendLine();
            sb.AppendLine("<candidate>");
            if (!string.IsNullOrEmpty(input.TotalExperience))
                sb.AppendLine($"  <fact id=\"y1\">Toplam iş deneyimi: {Escape(input.TotalExperience)}</fact>");
            if (!string.IsNullOrEmpty(input.Summary))
                sb.AppendLine($"  <fact id=\"s0\">{Escape(input.Summary)}</fact>");
            foreach (var entry in input.Entries)
            {
                sb.AppendLine($"  <{entry.Kind} id=\"{entry.Token}\" title=\"{Escape(entry.Title)}\" at=\"{Escape(entry.Subtitle ?? "")}\">");
                foreach (var fact in entry.Facts)
                    sb.AppendLine($"    <fact id=\"{fact.Token}\"{(fact.FromInterview ? " source=\"answer\"" : "")}{(fact.AddsTo != null ? $" adds-to=\"{fact.AddsTo}\"" : "")}>{Escape(fact.Text)}</fact>");
                sb.AppendLine($"  </{entry.Kind}>");
            }
            sb.AppendLine("</candidate>");
            return sb.ToString();
        }

        private static string Escape(string text) => text.Replace("<", "‹").Replace(">", "›").Replace("\"", "'");

        private const string SystemPrompt = """
            You rewrite a candidate's CV bullets for a specific job (inside <target>), using ONLY the facts inside <candidate>.
            Facts marked source="answer" are the candidate's own answers to our follow-up questions; treat them as true facts. In reasons call them "sorulara verdiğin cevap", never "mülakat".

            Hard rules (a program checks them; a bullet that breaks them is shown to the candidate in red):
            - Every bullet lists the fact ids it is based on in "sources". Only use facts from the same job/project the bullet belongs to.
            - Never add a number, percentage, duration, technology, tool, company, product, title or achievement that is not in the source facts. If a fact has no number, the bullet has no number.
            - Do not upgrade the candidate's role ("helped" must not become "led", "took part in" must not become "managed").
            - Use every fact exactly once. You may merge at most two facts of the same job into one bullet when they describe the same work.
            - A fact with adds-to="fX" is the candidate's answer that adds details (usually numbers) to fact fX. Always merge it into the bullet for fX; that bullet's sources must list both ids.
            - Write in the same language as the facts (normally Turkish), first person past tense without the pronoun ("geliştirdim", "azalttım"), consistent across all bullets.

            How to make a bullet strong:
            - Structure: what you did + how/with what + result. Lead with a concrete verb. Put the result (numbers from the facts) at the end.
            - Use the posting's own words for skills and tools when the fact supports them (e.g. the fact says "SQL Server" and the posting says "MSSQL": write "MSSQL (SQL Server)" or just the posting's term). Weave keywords in naturally; never list them.
            - Remove vague phrases ("görev aldım", "sorumluydum", "çeşitli") by saying what was actually done, as far as the facts allow.
            - One or two lines (max ~220 characters).

            When a fact is too vague to improve without inventing details (e.g. "Web sitesi geliştirmelerinde görev aldım"):
            - Keep the bullet close to the original. Do not fill the gap with guesses.
            - Use "reason" to teach what is missing, e.g. "Bu madde ne yaptığını söylemiyor. Hangi sayfaları/özellikleri geliştirdiğini ve sonucunu kasana eklersen güçlü bir maddeye dönüşür."

            For each bullet also give:
            - "requirements": keys of the target requirements this bullet proves.
            - "alternatives": two other phrasings that follow exactly the same rules (no new facts, no vague words such as "çeşitli", "görev aldım", "yer aldım", "sorumluydum").
            - "reason": one Turkish sentence for the candidate, addressed to them as "sen", explaining what you changed and why, e.g. "İlan ASP.NET Core istiyor; teknolojiyi başa aldım ve 'görev aldım' yerine ne yaptığını yazdım." Never mention fact or entry ids (f1, e2) in reasons; the candidate does not see them.

            Summary: two or three sentences in Turkish for the top of the CV, aimed at this job, built only from facts (use y1 for total experience and s0 for the candidate's own summary if present).
            - Write it in the usual Turkish CV style without "ben" and without first-person verbs: "ASP.NET Core ve SQL Server ile backend geliştiren, 4 yılı aşkın deneyime sahip yazılım uzmanı. ..."
            - Round experience down to whole years ("4 yılı aşkın"); under one year, say "1 yıldan az". Never claim more than y1.
            - No generic claims that the facts do not show ("süreçleri optimize ettim", "dinamik", "çözüm odaklı").
            - sources: the fact ids used. If there is not enough to say, return "summary": null.

            Return ONLY a JSON object with exactly this shape:
            {
              "summary": { "text": "", "sources": ["y1"], "reason": "" },
              "entries": [
                { "id": "e1", "bullets": [ { "text": "", "sources": ["f1"], "requirements": ["r1"], "alternatives": ["", ""], "reason": "" } ] }
              ]
            }
            """;
    }

    public class RewriteInput
    {
        public string TargetTitle { get; set; } = string.Empty;
        public string? Seniority { get; set; }
        public string? Summary { get; set; }
        public string? TotalExperience { get; set; }
        public List<RewriteRequirement> Requirements { get; set; } = new();
        public List<string> Keywords { get; set; } = new();
        public List<RewriteInputEntry> Entries { get; set; } = new();
    }

    public record RewriteRequirement(string Key, string Text, string Importance, string Strength);

    public class RewriteInputEntry
    {
        public string Token { get; set; } = string.Empty;
        // experience | project (prompttaki etiket adı)
        public string Kind { get; set; } = "experience";
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public List<RewriteFact> Facts { get; set; } = new();
    }

    // AddsTo: rakam sorusunun cevabıysa detay eklediği maddenin etiketi; AI ikisini tek madde yapıyor.
    public record RewriteFact(string Token, string Text, bool FromInterview, string? AddsTo = null);

    public class RewriteOutput
    {
        public RewriteSummary? Summary { get; set; }
        public List<RewriteEntry> Entries { get; set; } = new();
    }

    public class RewriteSummary
    {
        public string? Text { get; set; }
        public List<string>? Sources { get; set; }
        public string? Reason { get; set; }
    }

    public class RewriteEntry
    {
        public string Id { get; set; } = string.Empty;
        public List<RewriteBullet> Bullets { get; set; } = new();
    }

    public class RewriteBullet
    {
        public string Text { get; set; } = string.Empty;
        public List<string> Sources { get; set; } = new();
        public List<string> Requirements { get; set; } = new();
        public List<string> Alternatives { get; set; } = new();
        public string Reason { get; set; } = string.Empty;
    }
}
