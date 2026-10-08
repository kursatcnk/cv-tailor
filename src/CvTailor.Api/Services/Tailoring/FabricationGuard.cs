using System.Text.RegularExpressions;
using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Matching;

namespace CvTailor.Api.Services.Tailoring
{
    // Uydurma koruması. AI'ın yazdığı her madde, dayandığı kasa maddeleriyle karşılaştırılıyor.
    // Kaynakta olmayan rakam, teknoloji/ilan terimi, özel ad ya da liderlik iddiası varsa madde işaretleniyor
    // ve kullanıcı onaylamadan CV'ye girmiyor. Prompt "uydurma" diyor ama buna güvenmiyorum; son söz kodda.
    public class FabricationGuard
    {
        private static readonly Regex Number = new(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);
        // Büyük harfle başlayan kelime ya da kısaltma: "Trendyol", "SAP", "Örnek"
        private static readonly Regex ProperWord = new(@"(?<![\p{L}\p{N}])\p{Lu}[\p{L}\p{N}.#+]*", RegexOptions.Compiled);
        private static readonly string[] Leadership = { "yönet", "liderlik", "lider ", "liderliğ", "koordine", "ekibin başında", "ekip lideri", "sorumluluğunu üstlen" };
        // Cümle ortasında büyük harfle yazılsa da özel ad sayılmayacak kelimeler
        private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase) { "Türkiye", "TL", "PDF", "Excel'de" };

        private readonly SkillDictionary _dictionary;

        public FabricationGuard(SkillDictionary dictionary) => _dictionary = dictionary;

        // sourceText: maddenin dayandığı kasa maddelerinin metni (özet için daha geniş: unvanlar, beceriler, toplam süre).
        // targetTerms: ilandaki terimler; AI'ın en çok "eklemek isteyeceği" kelimeler bunlar.
        public GuardResult Check(string text, string sourceText, IEnumerable<string> targetTerms)
        {
            var issues = new List<string>();
            var source = SkillDictionary.Normalize(sourceText);

            var sourceNumbers = Number.Matches(sourceText).Select(m => NormalizeNumber(m.Value)).ToHashSet();
            foreach (var number in Number.Matches(text).Select(m => m.Value).Distinct())
                if (!sourceNumbers.Contains(NormalizeNumber(number)))
                    issues.Add($"Kaynakta olmayan rakam: {number}");

            // Teknoloji ve ilan terimleri: maddede geçiyorsa kaynakta da (ya da eşdeğeri/kapsayanı) geçmeli.
            // Uzun terim önce: "SQL Server" bulunduysa içindeki "SQL" ayrıca raporlanmasın.
            var terms = _dictionary.AllTerms.Concat(targetTerms).Where(t => t.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(t => t.Length);
            var added = new List<string>();
            foreach (var term in terms)
            {
                if (!_dictionary.Contains(text, term, strict: true)) continue;
                if (_dictionary.EvidenceTerms(term).Any(t => _dictionary.Contains(sourceText, t, strict: false))) continue;
                // "SQL Server" geçen maddede "SQL" ayrıca bulunuyor; daha uzun bir terimin parçasıysa tekrar sayma.
                if (added.Any(a => SkillDictionary.Normalize(a).Contains(SkillDictionary.Normalize(term)))) continue;
                added.Add(term);
            }
            issues.AddRange(added.Select(t => $"Kaynakta olmayan teknoloji ya da terim: {t}"));

            // Özel adlar: cümle başı dışında büyük harfle yazılmış kelime kaynakta yoksa (şirket, ürün, kurum adı uydurması).
            foreach (Match m in ProperWord.Matches(text))
            {
                var word = m.Value.TrimEnd('.');
                if (m.Index == 0 || Regex.IsMatch(text[..m.Index], @"[.!?:]\s*$")) continue;
                if (word.Length < 3 || Common.Contains(word) || added.Any(a => a.Equals(word, StringComparison.OrdinalIgnoreCase))) continue;
                var stem = SkillDictionary.Normalize(word.Split('\'')[0]);
                if (stem.Length >= 3 && !source.Contains(stem[..Math.Max(3, stem.Length - 2)]))
                    issues.Add($"Kaynakta geçmeyen ad: {word}");
            }

            var normalized = SkillDictionary.Normalize(text);
            if (Leadership.Any(l => normalized.Contains(l)) && !Leadership.Any(l => source.Contains(l)))
                issues.Add("Kaynakta olmayan yönetim ya da liderlik iddiası.");

            return new GuardResult { Status = issues.Count == 0 ? "verified" : "flagged", Issues = issues.Distinct().ToList() };
        }

        // "1.500" ve "1500", "2,5" ve "2.5" aynı sayı.
        private static string NormalizeNumber(string value) => value.Replace(".", "").Replace(",", "");
    }
}
