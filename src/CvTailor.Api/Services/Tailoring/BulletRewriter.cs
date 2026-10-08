using System.Text;
using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Ai;

namespace CvTailor.Api.Services.Tailoring
{
    // Seçilen maddeleri ilana göre yeniden yazdırıyor. AI'a her madde kısa bir etiketle (f1, f2...) gidiyor ve
    // her yeni madde hangi etiketlerden türediğini söylemek zorunda. Uzun Guid'ler yerine etiket kullanmak hem
    // token tasarrufu hem de AI'ın var olmayan bir kimlik uydurmasını zorlaştırıyor.
    public class BulletRewriter
    {
        private const int MaxBulletLength = 300;
        private readonly AiService _ai;

        public BulletRewriter(AiService ai) => _ai = ai;

        public async Task<(RewriteOutput Output, AiCompletion Completion)> RewriteAsync(RewriteInput input, CancellationToken cancellationToken)
        {
            var (output, completion) = await _ai.CompleteJsonAsync<RewriteOutput>(SystemPrompt, BuildMessage(input), cancellationToken);
            return (Clean(output, input), completion);
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
                        Alternatives = (bullet.Alternatives ?? new()).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => Clip(a)).Take(2).ToList(),
                        Reason = bullet.Reason?.Trim() ?? ""
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
                if (result.Summary.Text == null || result.Summary.Sources.Count == 0) result.Summary = null;
            }
            return result;
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
                    sb.AppendLine($"    <fact id=\"{fact.Token}\"{(fact.FromInterview ? " source=\"interview\"" : "")}>{Escape(fact.Text)}</fact>");
                sb.AppendLine($"  </{entry.Kind}>");
            }
            sb.AppendLine("</candidate>");
            return sb.ToString();
        }

        private static string Escape(string text) => text.Replace("<", "‹").Replace(">", "›").Replace("\"", "'");

        private const string SystemPrompt = """
            You rewrite a candidate's CV bullets for a specific job (inside <target>), using ONLY the facts inside <candidate>.
            Facts marked source="interview" are the candidate's own answers to follow-up questions; treat them as true facts.

            Hard rules (a program checks them; a bullet that breaks them is shown to the candidate in red):
            - Every bullet lists the fact ids it is based on in "sources". Only use facts from the same job/project the bullet belongs to.
            - Never add a number, percentage, duration, technology, tool, company, product, title or achievement that is not in the source facts. If a fact has no number, the bullet has no number.
            - Do not upgrade the candidate's role ("helped" must not become "led", "took part in" must not become "managed").
            - Use every fact exactly once. You may merge at most two facts of the same job into one bullet when they describe the same work (for example an original bullet and the interview answer that adds its numbers).
            - Write in the same language as the facts (normally Turkish), first person past tense without the pronoun ("geliştirdim", "azalttım"), consistent across all bullets.

            How to make a bullet strong:
            - Structure: what you did + how/with what + result. Lead with a concrete verb. Put the result (numbers from the facts) at the end.
            - Use the posting's own words for skills and tools when the fact supports them (e.g. the fact says "SQL Server" and the posting says "MSSQL": write "MSSQL (SQL Server)" or just the posting's term). Weave keywords in naturally; never list them.
            - Remove vague phrases ("görev aldım", "sorumluydum", "çeşitli") by saying what was actually done, as far as the facts allow.
            - One or two lines (max ~220 characters).

            For each bullet also give:
            - "requirements": keys of the target requirements this bullet proves.
            - "alternatives": two other phrasings that follow the same rules.
            - "reason": one Turkish sentence for the candidate explaining what you changed and why, e.g. "İlan ASP.NET Core istiyor; teknolojiyi başa aldım ve 'görev aldım' yerine ne yaptığını yazdım."

            Summary: two or three sentences in Turkish for the top of the CV, aimed at this job, built only from facts (use y1 for total experience and s0 for the candidate's own summary if present). Never claim more years than y1. sources: the fact ids used. If there is not enough to say, return "summary": null.

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

    public record RewriteFact(string Token, string Text, bool FromInterview);

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
