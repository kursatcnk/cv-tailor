using System.Globalization;
using System.Text.RegularExpressions;

namespace CvTailor.Api.Services.Quality
{
    // Tek bir CV maddesine bakıp öğretici geri bildirim veriyor. AI yok; kurallar sabit ve her uyarının nedeni belli.
    // Hem kasadaki sağlık kontrolü hem de madde yazarken anlık koç bunu kullanıyor.
    public static class BulletLinter
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
        private const int MaxLength = 220;

        // Her CV'de olan, hiçbir şey kanıtlamayan ifadeler.
        private static readonly string[] Cliches =
        {
            "takım çalışmasına yatkın", "ekip çalışmasına yatkın", "stres altında çalışabilen", "stres altında çalışabilirim",
            "öğrenmeye açık", "yeniliklere açık", "dinamik", "sonuç odaklı", "çözüm odaklı", "detaycı",
            "problem çözme yeteneği", "iletişim becerisi yüksek", "iletişimi güçlü", "analitik düşünme",
            "hızlı öğrenen", "motivasyonu yüksek", "disiplinli", "sorumluluk sahibi"
        };

        // Ne yapıldığını söylemeyen fiiller.
        private static readonly (string Phrase, string Hint)[] WeakVerbs =
        {
            ("görev aldım", "\"Görev aldım\" ne yaptığını söylemiyor. Somut işi yaz: \"... sayfasını geliştirdim\"."),
            ("yer aldım", "\"Yer aldım\" senin payını göstermiyor. Projede tam olarak neyi yaptığını yaz."),
            ("sorumluydum", "\"Sorumluydum\" bir görev tanımı, başarı değil. O sorumlulukla ne yaptığını ve sonucunu yaz."),
            ("sorumlu oldum", "\"Sorumlu oldum\" bir görev tanımı. O işte ne yaptığını ve sonucunu yaz."),
            ("yardımcı oldum", "\"Yardımcı oldum\" katkını küçültüyor. Neyi yaptığını doğrudan yaz."),
            ("katkıda bulundum", "\"Katkıda bulundum\" belirsiz. Katkının ne olduğunu yaz."),
            ("ilgilendim", "\"İlgilendim\" ne yaptığını anlatmıyor. Yaptığın işi fiiliyle yaz."),
            ("çeşitli", "\"Çeşitli\" yerine hangileri olduğunu yaz.")
        };

        private static readonly Regex Number = new(@"\d", RegexOptions.Compiled);

        public static List<LintIssue> Lint(string? text)
        {
            var issues = new List<LintIssue>();
            var t = text?.Trim() ?? "";
            if (t.Length == 0) return issues;
            // Kasada özet 2000, madde 1000 karakterde kesiliyor; kaydedilmemiş formdan gelen metin için de sınır koy.
            if (t.Length > 2000) t = t[..2000];
            var lower = t.ToLower(Tr);

            foreach (var cliche in Cliches.Where(c => lower.Contains(c)))
                issues.Add(new LintIssue("cliche", "warn", $"Klişe: \"{cliche}\"", "Bunu herkes yazıyor ve bir şey kanıtlamıyor. Bu özelliği gösteren gerçek bir durumu yaz."));
            foreach (var (phrase, hint) in WeakVerbs.Where(w => lower.Contains(w.Phrase)))
                issues.Add(new LintIssue("weak-verb", "warn", $"Zayıf ifade: \"{phrase}\"", hint));
            if (lower.StartsWith("ben "))
                issues.Add(new LintIssue("pronoun", "warn", "\"Ben\" ile başlıyor", "CV'de \"ben\" kullanılmaz; doğrudan fiille başla: \"Geliştirdim\", \"Kurdum\"."));
            if (t.Length > MaxLength)
                issues.Add(new LintIssue("length", "info", "İki satırı geçiyor", "Bir madde bir iki satır olmalı. En önemli sonucu bırak, gerisini kısalt."));
            if (!Number.IsMatch(t))
                issues.Add(new LintIssue("no-number", "info", "Rakam yok", "Kaç kişi, ne kadar süre, yüzde kaç? Bir rakam maddeyi iki kat inandırıcı yapar."));
            switch (Mood(t))
            {
                case "edilgen":
                    issues.Add(new LintIssue("passive", "info", "Edilgen cümle", "\"Geliştirildi\" işi kimin yaptığını saklıyor. Senin işinse \"geliştirdim\" yaz."));
                    break;
                case "isim":
                    issues.Add(new LintIssue("noun-ending", "info", "Fiille bitmiyor", "\"API geliştirme\" bir görev başlığı gibi duruyor. \"API'yi geliştirdim\" dersen ne yaptığın netleşir."));
                    break;
            }
            return issues;
        }

        // Madde hangi kiple bitiyor? Aynı iş altında karışık kip ("geliştirdim" / "geliştirildi" / "geliştirme") dağınık görünüyor.
        public static string? Mood(string? text)
        {
            // Sadece son kelime lazım; uzun metinde regex'i baştan çalıştırmamak için sondan kısa bir parça yeter.
            var tail = text?.Trim().TrimEnd('.', ';', '!') ?? "";
            tail = tail[^Math.Min(60, tail.Length)..];
            var word = Regex.Match(tail, @"(\p{L}+)$").Value.ToLower(Tr);
            if (word.Length < 4) return null;
            if (Regex.IsMatch(word, "(ıldı|ildi|uldu|üldü|ndı|ndi|ndu|ndü)$")) return "edilgen";
            if (Regex.IsMatch(word, "(dım|dim|dum|düm|tım|tim|tum|tüm)$")) return "birinci";
            if (Regex.IsMatch(word, "(ması|mesi|mak|mek)$") || Regex.IsMatch(word, "(ma|me)$")) return "isim";
            return null;
        }
    }

    // level: warn (düzeltilmeli) | info (iyileştirilebilir) | error (yanlış)
    public record LintIssue(string Code, string Level, string Message, string Hint);
}
