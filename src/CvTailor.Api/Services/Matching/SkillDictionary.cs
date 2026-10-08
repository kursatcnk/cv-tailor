using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CvTailor.Api.Services.Matching
{
    // Knowledge/synonyms.json: eşdeğer terim grupları ("MSSQL" = "SQL Server") ve "kapsar" ilişkileri
    // ("GitHub Actions" kullanan CI/CD de biliyordur). Bir gereksinim terimi için CV'de hangi kelimelerin
    // kanıt sayılacağını buradan çıkarıyorum.
    public class SkillDictionary
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        private readonly Dictionary<string, HashSet<string>> _groupOf = new();
        private readonly Dictionary<string, List<string>> _impliedBy = new();
        private readonly Dictionary<string, Regex> _regexCache = new();
        private readonly object _cacheLock = new();

        public SkillDictionary(string json)
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var group in doc.RootElement.GetProperty("groups").EnumerateArray())
            {
                var members = group.EnumerateArray().Select(e => e.GetString()!).ToList();
                var set = new HashSet<string>(members);
                foreach (var member in members)
                    _groupOf[Normalize(member)] = set;
            }

            // "A": ["B"] → A'yı bilen B'yi de biliyor. Arama tersten yapılacağı için B → [A] olarak tutuyorum.
            foreach (var pair in doc.RootElement.GetProperty("implies").EnumerateObject())
                foreach (var implied in pair.Value.EnumerateArray())
                {
                    var key = Normalize(implied.GetString()!);
                    if (!_impliedBy.TryGetValue(key, out var list)) _impliedBy[key] = list = new();
                    list.Add(pair.Name);
                }
        }

        public static SkillDictionary Load(string path) => new(File.ReadAllText(path, Encoding.UTF8));

        // Sözlükteki bütün terimler; uydurma koruması yeni maddede bunlardan biri geçiyor mu diye bakıyor.
        public IEnumerable<string> AllTerms =>
            _groupOf.Values.SelectMany(g => g).Concat(_impliedBy.Values.SelectMany(v => v)).Distinct(StringComparer.OrdinalIgnoreCase);

        // Gereksinimdeki terim için CV'de aranacak bütün yazılışlar: kendisi, eşdeğerleri,
        // onu kapsayan terimler ve onların eşdeğerleri.
        public IReadOnlyCollection<string> EvidenceTerms(string term)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { term.Trim() };
            foreach (var t in Group(term)) result.Add(t);

            var queue = new Queue<string>(result);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!_impliedBy.TryGetValue(Normalize(current), out var wider)) continue;
                foreach (var w in wider.SelectMany(Group))
                    if (result.Add(w)) queue.Enqueue(w);
            }
            return result;
        }

        private IEnumerable<string> Group(string term) =>
            _groupOf.TryGetValue(Normalize(term), out var set) ? set : new[] { term };

        // Metinde terimi arıyor. Türkçede özel isimlere ek kesme işaretiyle geliyor ("Excel'de", "C#'ta"),
        // sıradan kelimelere bitişik ("muhasebede", "İngilizceyi").
        // strict: teknik terim ve araçlarda terimden sonra harf gelmemeli; yoksa "Git" "gitti"de, "Excel" "excellent"ta bulunur.
        // Diğerlerinde (bölüm, dil, alan) kelime başında başlaması yeterli. Kısa ya da harf dışı karakter içeren terim hep strict.
        public bool Contains(string text, string term, bool strict) => GetRegex(term, strict).IsMatch(Normalize(text));

        private Regex GetRegex(string term, bool strict)
        {
            var normalized = Normalize(term);
            strict = strict || normalized.Length <= 3 || normalized.Any(c => !char.IsLetter(c) && c != ' ');
            var cacheKey = (strict ? "s:" : "p:") + normalized;
            lock (_cacheLock)
            {
                if (_regexCache.TryGetValue(cacheKey, out var cached)) return cached;
                var pattern = Regex.Escape(normalized).Replace("\\ ", "\\s+");
                var regex = new Regex(strict
                    ? $@"(?<![\p{{L}}\p{{N}}]){pattern}(?![\p{{L}}\p{{N}}+#])"
                    : $@"(?<![\p{{L}}\p{{N}}]){pattern}", RegexOptions.CultureInvariant);
                _regexCache[cacheKey] = regex;
                return regex;
            }
        }

        // Karşılaştırma için: tr-TR küçük harf, sonra ı/i ve şapkalı harfler birleştiriliyor.
        // "INGILIZCE" (noktasız I ile yazılmış) da "İngilizce" ile eşleşsin.
        public static string Normalize(string value)
        {
            var lower = value.Trim().ToLower(Tr);
            var sb = new StringBuilder(lower.Length);
            foreach (var c in lower)
                sb.Append(c switch { 'ı' => 'i', 'â' => 'a', 'î' => 'i', 'û' => 'u', '’' => '\'', _ => c });
            return Regex.Replace(sb.ToString(), @"\s+", " ");
        }
    }
}
