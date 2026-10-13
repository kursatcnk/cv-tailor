using System.Text;
using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Ai;
using CvTailor.Api.Services.Privacy;

namespace CvTailor.Api.Services.Tailoring
{
    // Ön yazı. Kaynağı kasa değil, kullanıcının provada onayladığı CV: mektup CV'de olmayan bir şey söylemesin.
    // Hitap ve imza kodda; AI sadece gövde paragraflarını yazıyor ve ad soyad AI'a hiç gitmiyor.
    public class CoverLetterWriter
    {
        private const int MaxParagraphs = 4;
        private const int MaxParagraphLength = 900;
        private readonly AiService _ai;

        public CoverLetterWriter(AiService ai) => _ai = ai;

        public async Task<(List<string> Paragraphs, AiCompletion Completion)> WriteAsync(CoverLetterInput input, PiiMask mask, CancellationToken cancellationToken)
        {
            var (output, completion) = await _ai.CompleteJsonAsync<CoverLetterOutput>(SystemPrompt, BuildMessage(input, mask), cancellationToken);
            return (Clean(output).Select(mask.Unmask).ToList(), completion);
        }

        // Prompt'a rağmen hitap ya da imza yazarsa at; boşları ve fazlasını kes.
        public static List<string> Clean(CoverLetterOutput output) => output.Paragraphs
            .Select(p => p?.Trim() ?? "")
            .Where(p => p.Length > 0
                && !p.StartsWith("Sayın", StringComparison.OrdinalIgnoreCase)
                && !p.StartsWith("Saygılarımla", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Length <= MaxParagraphLength ? p : p[..MaxParagraphLength].TrimEnd() + "…")
            .Take(MaxParagraphs)
            .ToList();

        private static string BuildMessage(CoverLetterInput input, PiiMask mask)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<position title=\"{Escape(input.TargetTitle)}\" company=\"{Escape(input.Company ?? "")}\">");
            foreach (var r in input.Requirements)
                sb.AppendLine($"  <requirement importance=\"{r.Importance}\">{Escape(r.Text)}</requirement>");
            sb.AppendLine("</position>");
            sb.AppendLine();
            sb.AppendLine("<cv>");
            if (!string.IsNullOrEmpty(input.TotalExperience)) sb.AppendLine($"  <fact>Toplam iş deneyimi: {Escape(input.TotalExperience)}</fact>");
            if (!string.IsNullOrEmpty(input.Summary)) sb.AppendLine($"  <summary>{Escape(mask.Mask(input.Summary))}</summary>");
            foreach (var entry in input.Entries)
            {
                sb.AppendLine($"  <entry title=\"{Escape(mask.Mask(entry.Title))}\" at=\"{Escape(mask.Mask(entry.Subtitle ?? ""))}\">");
                foreach (var bullet in entry.Bullets)
                    sb.AppendLine($"    <fact>{Escape(mask.Mask(bullet))}</fact>");
                sb.AppendLine("  </entry>");
            }
            if (input.Skills.Count > 0) sb.AppendLine($"  <skills>{Escape(string.Join(", ", input.Skills))}</skills>");
            sb.AppendLine("</cv>");
            return sb.ToString();
        }

        private static string Escape(string text) => text.Replace("<", "‹").Replace(">", "›").Replace("\"", "'");

        private const string SystemPrompt = """
            You write the body of a short Turkish cover letter (ön yazı) for the position in <position>, using ONLY what is in <cv>.

            Hard rules (a program checks every paragraph; anything that breaks them is shown to the candidate in red):
            - Never add a number, duration, technology, tool, company, product, title or achievement that is not in <cv>.
            - Do not upgrade the candidate's role ("helped" must not become "led").
            - Mention only requirements the CV actually supports. Never mention, excuse or apologize for missing ones.
            - Do not invent reasons for wanting this company (its mission, products, culture). You know nothing about it except its name.
            - No greeting ("Sayın ...") and no closing or signature ("Saygılarımla"); the program adds them.

            Style:
            - Formal Turkish, first person, addressing the company politely with "siz" forms ("ilanınız", "ekibinize").
            - Three paragraphs, 120-200 words in total:
              1. The position you are applying for and one sentence on why you fit, based on the CV (total experience, main area).
              2. Two or three of the strongest CV facts that match the most important requirements, with their numbers if the facts have numbers.
              3. One or two sentences: you would be glad to talk about this in an interview. Nothing else.
            - No clichés: "dinamik", "çözüm odaklı", "takım çalışmasına yatkın", "kendimi geliştirmeye açık", "stres altında çalışabilen", "sonuç odaklı". Show it with facts instead.
            - Placeholders such as [EPOSTA_1] are hidden personal data; do not use them.

            Return ONLY a JSON object: { "paragraphs": ["", "", ""] }
            """;
    }

    public class CoverLetterInput
    {
        public string TargetTitle { get; set; } = string.Empty;
        public string? Company { get; set; }
        public List<(string Text, string Importance)> Requirements { get; set; } = new();
        public string? TotalExperience { get; set; }
        public string? Summary { get; set; }
        public List<(string Title, string? Subtitle, List<string> Bullets)> Entries { get; set; } = new();
        public List<string> Skills { get; set; } = new();
    }

    public class CoverLetterOutput
    {
        public List<string?> Paragraphs { get; set; } = new();
    }
}
