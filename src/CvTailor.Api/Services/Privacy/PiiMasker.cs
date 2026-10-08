using System.Text.RegularExpressions;
using CvTailor.Api.Dtos;

namespace CvTailor.Api.Services.Privacy
{
    // AI'a giden metindeki kişisel verileri yer tutucuyla değiştiriyor, cevap gelince geri koyuyor.
    // AI'ın maddeyi yeniden yazmak için adına, telefonuna ya da e-postana ihtiyacı yok; gönderilmeyen veri sızamaz.
    public class PiiMask
    {
        private static readonly Regex Email = new(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.Compiled);
        private static readonly Regex Url = new(@"(https?://)?(www\.)?(linkedin\.com|github\.com|gitlab\.com|behance\.net)/[\w%./-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Phone = new(@"(?<!\d)(\+90[\s-]?)?\(?0?[2-5]\d{2}\)?[\s-]?\d{3}[\s-]?\d{2}[\s-]?\d{2}(?!\d)", RegexOptions.Compiled);
        private static readonly Regex Iban = new(@"\bTR\d{2}(\s?\d{4}){5}\s?\d{2}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Eleven = new(@"(?<!\d)[1-9]\d{10}(?!\d)", RegexOptions.Compiled);

        private readonly List<(string Value, string Placeholder)> _known = new();
        private readonly Dictionary<string, string> _restore = new();
        private readonly Dictionary<string, int> _counters = new();

        // Kasadaki bilinen değerler (ad, e-posta, telefon, linkler). Regex'le bulunamayan ad soyad bunlarla maskeleniyor.
        public PiiMask(IEnumerable<(string? Value, string Kind)> known)
        {
            foreach (var (value, kind) in known)
                if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 3)
                    _known.Add((value.Trim(), Next(kind, value.Trim())));
            // Uzun değer önce: "Deniz Aksoy" maskelenmeden "Deniz" maskelenmesin.
            _known.Sort((a, b) => b.Value.Length.CompareTo(a.Value.Length));
        }

        public static PiiMask ForProfile(ProfileDto profile)
        {
            var name = profile.FullName?.Trim();
            var parts = name?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length >= 3) ?? Enumerable.Empty<string>();
            return new PiiMask(new[] { (name, "KISI"), (profile.Email, "EPOSTA"), (profile.Phone, "TELEFON"),
                    (profile.LinkedInUrl, "LINK"), (profile.GitHubUrl, "LINK"), (profile.WebsiteUrl, "LINK") }
                .Concat(parts.Select(p => ((string?)p, "KISI"))));
        }

        public string Mask(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var result = text;
            foreach (var (value, placeholder) in _known)
                result = Regex.Replace(result, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(value)}(?![\p{{L}}\p{{N}}])", placeholder, RegexOptions.IgnoreCase);
            result = Email.Replace(result, m => Next("EPOSTA", m.Value));
            result = Url.Replace(result, m => Next("LINK", m.Value));
            result = Iban.Replace(result, m => Next("IBAN", m.Value));
            result = Eleven.Replace(result, m => PiiScanner.IsTckn(m.Value) ? Next("TCKN", m.Value) : m.Value);
            result = Phone.Replace(result, m => Next("TELEFON", m.Value));
            return result;
        }

        public string Unmask(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var result = text;
            foreach (var (placeholder, value) in _restore)
                result = result.Replace(placeholder, value);
            return result;
        }

        // Aynı değer hep aynı yer tutucuyu alıyor; AI metinde iki yerde geçen e-postanın aynı olduğunu bilsin.
        private string Next(string kind, string value)
        {
            var existing = _restore.FirstOrDefault(p => p.Value == value && p.Key.StartsWith($"[{kind}_"));
            if (existing.Key != null) return existing.Key;
            _counters[kind] = _counters.GetValueOrDefault(kind) + 1;
            var placeholder = $"[{kind}_{_counters[kind]}]";
            _restore[placeholder] = value;
            return placeholder;
        }
    }

    // CV'de bulunmaması gereken kişisel verileri buluyor ve kullanıcıyı uyarıyor.
    public static class PiiScanner
    {
        private static readonly Regex Eleven = new(@"(?<!\d)[1-9]\d{10}(?!\d)", RegexOptions.Compiled);
        // Tek başına "Cad." adres sayılmaz (şirket adresi, okul adı olabilir); en az iki adres işareti arıyorum.
        private static readonly Regex AddressMarker = new(@"(?<![\p{L}])(mah\.|mahallesi|sok\.|sokak|sokağı|sk\.|cad\.|caddesi|bulvarı|blv\.|apt\.|apartmanı|daire\s*:?\s*\d+|d\s*:\s*\d+|kat\s*:?\s*\d+|no\s*:\s*\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<string> Warnings(string? text)
        {
            var warnings = new List<string>();
            if (string.IsNullOrEmpty(text)) return warnings;
            if (Eleven.Matches(text).Any(m => IsTckn(m.Value)))
                warnings.Add("CV'nde TC kimlik numarası var. CV'ye koyma; işverenin bu bilgiye başvuru aşamasında ihtiyacı yok ve kötüye kullanılabilir.");
            if (AddressMarker.Matches(text).Count >= 2)
                warnings.Add("CV'nde tam ev adresi var gibi görünüyor. Şehir (ve istersen ilçe) yazman yeterli.");
            return warnings;
        }

        public static List<string> Warnings(ProfileDto profile) => Warnings(string.Join("\n",
            new[] { profile.Location, profile.Summary, profile.Headline }
                .Concat(profile.Experiences.SelectMany(e => e.Achievements.Select(a => a.Text)))
                .Concat(profile.Projects.SelectMany(p => p.Achievements.Select(a => a.Text)))));

        // T.C. kimlik no kuralı: 11 hane, ilk hane 0 değil; 10. hane ve 11. hane önceki hanelerden hesaplanıyor.
        public static bool IsTckn(string value)
        {
            if (value.Length != 11 || value[0] == '0' || !value.All(char.IsDigit)) return false;
            var d = value.Select(c => c - '0').ToArray();
            var tenth = ((d[0] + d[2] + d[4] + d[6] + d[8]) * 7 - (d[1] + d[3] + d[5] + d[7])) % 10;
            if (tenth < 0) tenth += 10;
            return d[9] == tenth && d[10] == d.Take(10).Sum() % 10;
        }
    }
}
