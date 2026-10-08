using System.Text.RegularExpressions;
using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Ai;
using CvTailor.Api.Services.Privacy;

namespace CvTailor.Api.Services.Cv
{
    // CV metnini kasaya girecek yapılandırılmış profile çeviriyor. AI burada sadece "hangi satır hangi alana ait"
    // sorusunu çözüyor; madde metinleri kelimesi kelimesine kopyalanmalı. Uydurma koruması ileride bu maddeleri
    // kaynak olarak kullanacak, kaynak bozulursa koruma da bozulur.
    public class CvParser
    {
        private static readonly Regex Email = new(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.Compiled);
        private static readonly Regex Phone = new(@"(\+?90[\s-]?)?\(?0?5\d{2}\)?[\s-]?\d{3}[\s-]?\d{2}[\s-]?\d{2}", RegexOptions.Compiled);
        private static readonly Regex LinkedIn = new(@"(https?://)?([\w-]+\.)?linkedin\.com/in/[\w%-]+/?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GitHub = new(@"(https?://)?(www\.)?github\.com/[\w-]+/?(?![\w/-])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly AiService _ai;

        public CvParser(AiService ai) => _ai = ai;

        // E-posta, telefon, link, TC kimlik no ve IBAN AI'a yer tutucu olarak gidiyor ([EPOSTA_1]); AI onları doğru alana
        // kopyalıyor, biz geri açıyoruz. Ad soyadı desenle bulmak güvenilir değil, o yüzden ad AI'a açık gidiyor.
        public async Task<(ProfileDto Profile, AiCompletion Completion)> ParseAsync(string text, CancellationToken cancellationToken)
        {
            var mask = new PiiMask(Array.Empty<(string?, string)>());
            var safeText = mask.Mask(text).Replace("</cv>", "</cv_>", StringComparison.OrdinalIgnoreCase);
            var (profile, completion) = await _ai.CompleteJsonAsync<ProfileDto>(SystemPrompt, $"<cv>\n{safeText}\n</cv>", cancellationToken);
            return (ProfileNormalizer.Normalize(Unmask(profile, mask), "cv"), completion);
        }

        private static ProfileDto Unmask(ProfileDto p, PiiMask mask)
        {
            p.FullName = mask.Unmask(p.FullName);
            p.Headline = mask.Unmask(p.Headline);
            p.Email = mask.Unmask(p.Email);
            p.Phone = mask.Unmask(p.Phone);
            p.Location = mask.Unmask(p.Location);
            p.LinkedInUrl = mask.Unmask(p.LinkedInUrl);
            p.GitHubUrl = mask.Unmask(p.GitHubUrl);
            p.WebsiteUrl = mask.Unmask(p.WebsiteUrl);
            p.Summary = mask.Unmask(p.Summary);
            foreach (var a in (p.Experiences ?? new()).SelectMany(e => e.Achievements ?? new()).Concat((p.Projects ?? new()).SelectMany(x => x.Achievements ?? new())))
                a.Text = mask.Unmask(a.Text);
            foreach (var project in p.Projects ?? new())
            {
                project.Url = mask.Unmask(project.Url);
                project.Description = mask.Unmask(project.Description);
            }
            foreach (var c in p.Certificates ?? new())
                c.Url = mask.Unmask(c.Url);
            return p;
        }

        // AI yokken: sadece kesin tanınan iletişim bilgileri dolduruluyor, gerisini kullanıcı metne bakarak giriyor.
        // Bölümleri tahmin etmeye çalışmıyorum; yanlış bölünmüş bir CV'yi düzeltmek boş formu doldurmaktan zor.
        public static ProfileDto ParseContactsOnly(string text) => ProfileNormalizer.Normalize(new ProfileDto
        {
            Email = Email.Match(text) is { Success: true } e ? e.Value : null,
            Phone = Phone.Match(text) is { Success: true } p ? p.Value : null,
            LinkedInUrl = LinkedIn.Match(text) is { Success: true } l ? l.Value : null,
            GitHubUrl = GitHub.Match(text) is { Success: true } g ? g.Value : null
        }, "cv");

        private static readonly string SystemPrompt = $$"""
            You convert the text of a CV (inside <cv> tags) into structured JSON. You are a careful copy clerk, not a writer.

            Rules:
            - Copy every bullet, responsibility and achievement EXACTLY as written, in the original language. Do not rewrite, improve, translate, shorten, merge or summarize them. Do not add anything that is not in the text.
            - If a job lists responsibilities as one paragraph instead of bullets, split it into achievements only at sentence boundaries, keeping each sentence word for word.
            - The character "{{CvTextExtractor.Unreadable}}" marks letters the PDF reader could not read (usually ligatures such as "ti", "tt", "fi", "tı"). Restore the obvious word, e.g. "Geliş{{CvTextExtractor.Unreadable}}rici" → "Geliştirici". This is the only change you may make to the text.
            - Dates: use "YYYY-MM" when the month is known, "YYYY" when only the year is known. If the job is ongoing ("Günümüz", "Halen", "Present"), leave endDate null and set isCurrent true.
            - employmentType: one of "full-time", "part-time", "internship", "freelance", or null if the CV does not say. "Stajyer"/"Staj" means "internship". Do not infer "full-time" from an ordinary job title; use it only when the CV says so ("Tam zamanlı", "Full-time").
            - Skill category: "technical" (languages, frameworks, databases, methods), "tool" (software and tools such as Git, Jira, Excel, Logo, SAP), "language" (spoken languages; put the level such as "B2" or "İleri" in level), "soft" (soft skills).
            - Split comma separated skill lists into separate skills.
            - headline: the job title line under the name, if there is one. summary: the profile/about paragraph, copied exactly, if there is one.
            - Projects are things the person built outside a job (personal, school, open source). Put a project's bullets in its achievements.
            - Personal data is replaced by placeholders such as [EPOSTA_1], [TELEFON_1], [LINK_1]. Copy a placeholder exactly as written into the matching field (email, phone, linkedInUrl, gitHubUrl, websiteUrl, project url).
            - Leave a field null when the CV does not contain it. Never guess emails, phone numbers, links or dates.
            - Set every "id" to null and every achievement "source" to "cv".

            Return ONLY a JSON object with exactly this shape:
            {
              "fullName": "", "headline": "", "email": "", "phone": "", "location": "",
              "linkedInUrl": "", "gitHubUrl": "", "websiteUrl": "", "summary": "",
              "experiences": [ { "id": null, "company": "", "title": "", "location": "", "employmentType": null, "startDate": "", "endDate": "", "isCurrent": false,
                                 "achievements": [ { "id": null, "text": "", "source": "cv" } ] } ],
              "educations": [ { "id": null, "school": "", "degree": "", "field": "", "startDate": "", "endDate": "", "gpa": "" } ],
              "projects": [ { "id": null, "name": "", "url": "", "description": "", "startDate": "", "endDate": "",
                              "achievements": [ { "id": null, "text": "", "source": "cv" } ] } ],
              "skills": [ { "id": null, "name": "", "category": "technical", "level": null } ],
              "certificates": [ { "id": null, "name": "", "issuer": "", "date": "", "url": "" } ]
            }
            """;
    }
}
