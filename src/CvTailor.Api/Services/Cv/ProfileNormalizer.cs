using System.Globalization;
using System.Text.RegularExpressions;
using CvTailor.Api.Dtos;

namespace CvTailor.Api.Services.Cv
{
    // AI'dan gelen taslağa da, kontrol ekranından kaydedilen profile de aynı kurallar uygulanıyor:
    // boşlukları kırp, DB sınırlarına göre kes, tarihleri tek biçime getir, bilinmeyen değerleri at, boş kayıtları sil.
    // Hiçbir şey eklemiyor; sadece temizliyor.
    public static class ProfileNormalizer
    {
        public static readonly string[] EmploymentTypes = { "full-time", "part-time", "internship", "freelance" };
        public static readonly string[] SkillCategories = { "technical", "tool", "language", "soft" };
        public static readonly string[] Sources = { "cv", "interview", "manual" };

        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        // Ay adları: Türkçe tam/kısa, İngilizce tam/kısa. Anahtarlar tr-TR ile küçültülmüş hâlde.
        private static readonly Dictionary<string, int> Months = BuildMonths();

        private static readonly Regex YearMonth = new(@"^(?<y>(19|20)\d{2})[-./](?<m>\d{1,2})$", RegexOptions.Compiled);
        private static readonly Regex MonthYear = new(@"^(?<m>\d{1,2})\s*[-./]\s*(?<y>(19|20)\d{2})$", RegexOptions.Compiled);
        private static readonly Regex NamedMonthYear = new(@"^(?<m>\p{L}+)\.?\s+(?<y>(19|20)\d{2})$", RegexOptions.Compiled);
        private static readonly Regex YearOnly = new(@"^(?<y>(19|20)\d{2})$", RegexOptions.Compiled);

        private static readonly string[] CurrentWords = { "günümüz", "halen", "hâlen", "hala", "hâlâ", "devam", "devam ediyor", "şu an", "present", "current", "now", "ongoing" };

        public static ProfileDto Normalize(ProfileDto profile, string defaultSource)
        {
            profile.FullName = Clip(profile.FullName, 150);
            profile.Headline = Clip(profile.Headline, 150);
            profile.Email = Clip(profile.Email, 255);
            profile.Phone = Clip(profile.Phone, 40);
            profile.Location = Clip(profile.Location, 150);
            profile.LinkedInUrl = Url(profile.LinkedInUrl);
            profile.GitHubUrl = Url(profile.GitHubUrl);
            profile.WebsiteUrl = Url(profile.WebsiteUrl);
            profile.Summary = Clip(profile.Summary, 2000);

            profile.Experiences = (profile.Experiences ?? new())
                .Where(e => e != null)
                .Select(e =>
                {
                    e.Company = Clip(e.Company, 200);
                    e.Title = Clip(e.Title, 200);
                    e.Location = Clip(e.Location, 150);
                    e.EmploymentType = EmploymentTypes.Contains(e.EmploymentType) ? e.EmploymentType : null;
                    e.StartDate = Date(e.StartDate, out _);
                    e.EndDate = Date(e.EndDate, out var endIsCurrent);
                    e.IsCurrent = (e.IsCurrent || endIsCurrent) && e.EndDate == null;
                    e.Achievements = Achievements(e.Achievements, defaultSource);
                    return e;
                })
                // Şirketi de pozisyonu da olmayan deneyim anlamsız; ikisinden biri yeter, kontrol ekranında tamamlanır.
                .Where(e => e.Company != null || e.Title != null)
                .Take(30)
                .ToList();

            profile.Educations = (profile.Educations ?? new())
                .Where(e => e != null)
                .Select(e =>
                {
                    e.School = Clip(e.School, 200);
                    e.Degree = Clip(e.Degree, 100);
                    e.Field = Clip(e.Field, 200);
                    e.StartDate = Date(e.StartDate, out _);
                    e.EndDate = Date(e.EndDate, out _);
                    e.Gpa = Clip(e.Gpa, 20);
                    return e;
                })
                .Where(e => e.School != null)
                .Take(15)
                .ToList();

            profile.Projects = (profile.Projects ?? new())
                .Where(p => p != null)
                .Select(p =>
                {
                    p.Name = Clip(p.Name, 200);
                    p.Url = Url(p.Url);
                    p.Description = Clip(p.Description, 1000);
                    p.StartDate = Date(p.StartDate, out _);
                    p.EndDate = Date(p.EndDate, out _);
                    p.Achievements = Achievements(p.Achievements, defaultSource);
                    return p;
                })
                .Where(p => p.Name != null)
                .Take(30)
                .ToList();

            // Aynı beceri iki kez yazılmışsa (C# / c#) birini tut. Türkçe büyük-küçük harf için tr-TR.
            profile.Skills = (profile.Skills ?? new())
                .Where(s => s != null)
                .Select(s =>
                {
                    s.Name = Clip(s.Name, 100);
                    s.Category = SkillCategories.Contains(s.Category) ? s.Category : "technical";
                    s.Level = Clip(s.Level, 50);
                    return s;
                })
                .Where(s => s.Name != null)
                .DistinctBy(s => s.Name!.ToLower(Tr))
                .Take(80)
                .ToList();

            profile.Certificates = (profile.Certificates ?? new())
                .Where(c => c != null)
                .Select(c =>
                {
                    c.Name = Clip(c.Name, 200);
                    c.Issuer = Clip(c.Issuer, 200);
                    c.Date = Date(c.Date, out _);
                    c.Url = Url(c.Url);
                    return c;
                })
                .Where(c => c.Name != null)
                .Take(30)
                .ToList();

            return profile;
        }

        private static List<AchievementDto> Achievements(List<AchievementDto>? items, string defaultSource) =>
            (items ?? new())
                .Where(a => a != null)
                .Select(a =>
                {
                    // CV'lerde maddenin başındaki işaret metne karışabiliyor: "• ", "- ", "* "
                    a.Text = Clip(a.Text?.TrimStart('•', '-', '*', '–', '·', ' '), 1000);
                    a.Source = Sources.Contains(a.Source) ? a.Source : defaultSource;
                    return a;
                })
                .Where(a => a.Text != null)
                .Take(20)
                .ToList();

        // "2023-04", "04/2023", "Nisan 2023", "Apr 2023", "2023" → "2023-04" / "2023".
        // "Günümüz", "Halen", "Present" → null ve isCurrent. Tanınmayan biçim null; kullanıcı ekranda düzeltiyor.
        public static string? Date(string? value, out bool isCurrent)
        {
            isCurrent = false;
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text)) return null;

            var lower = text.ToLower(Tr);
            if (CurrentWords.Contains(lower)) { isCurrent = true; return null; }

            Match m;
            if ((m = YearMonth.Match(text)).Success || (m = MonthYear.Match(text)).Success)
                return Format(m.Groups["y"].Value, int.Parse(m.Groups["m"].Value));
            // tr-TR "APRIL"ı "aprıl" yapıyor; İngilizce ay adları için kültürsüz küçültmeye de bak.
            if ((m = NamedMonthYear.Match(text)).Success
                && (Months.TryGetValue(m.Groups["m"].Value.ToLower(Tr), out var month) || Months.TryGetValue(m.Groups["m"].Value.ToLowerInvariant(), out month)))
                return Format(m.Groups["y"].Value, month);
            if ((m = YearOnly.Match(text)).Success)
                return m.Groups["y"].Value;
            return null;
        }

        private static string? Format(string year, int month) =>
            month is >= 1 and <= 12 ? $"{year}-{month:D2}" : year;

        // "github.com/ali" gibi şemasız linklere https ekle; link olmayan metni at.
        private static string? Url(string? value)
        {
            var text = Clip(value, 500);
            if (text == null) return null;
            if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                text = "https://" + text;
            return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Host.Contains('.') ? text : null;
        }

        private static string? Clip(string? value, int max)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            return text.Length <= max ? text : text[..max].TrimEnd();
        }

        private static Dictionary<string, int> BuildMonths()
        {
            var map = new Dictionary<string, int>();
            string[][] names =
            {
                new[] { "ocak", "oca", "january", "jan" }, new[] { "şubat", "şub", "february", "feb" },
                new[] { "mart", "mar", "march" }, new[] { "nisan", "nis", "april", "apr" },
                new[] { "mayıs", "may" }, new[] { "haziran", "haz", "june", "jun" },
                new[] { "temmuz", "tem", "july", "jul" }, new[] { "ağustos", "ağu", "august", "aug" },
                new[] { "eylül", "eyl", "september", "sep", "sept" }, new[] { "ekim", "eki", "october", "oct" },
                new[] { "kasım", "kas", "november", "nov" }, new[] { "aralık", "ara", "december", "dec" }
            };
            for (var i = 0; i < names.Length; i++)
                foreach (var name in names[i]) map[name] = i + 1;
            return map;
        }
    }
}
