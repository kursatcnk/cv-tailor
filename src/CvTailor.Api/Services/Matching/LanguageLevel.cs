using System.Globalization;
using System.Text.RegularExpressions;

namespace CvTailor.Api.Services.Matching
{
    // Dil seviyesi: ilan "ileri düzey" istiyorsa kasada A2 yazması tam kanıt değil.
    // Hem ilan cümlesini ("İyi derecede İngilizce") hem kasadaki seviyeyi ("B2", "Orta") aynı ölçeğe çeviriyor.
    public static class LanguageLevel
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
        private static readonly Regex Cefr = new(@"(?<![\p{L}\p{N}])(?<l>[abc])\s?(?<n>[12])(?![\p{L}\p{N}])", RegexOptions.Compiled);

        // 1 = A1 ... 6 = C2 / anadil; seviye yazmıyorsa null.
        public static int? Rank(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text.ToLower(Tr);
            if (Cefr.Match(t) is { Success: true } m) return (m.Groups["l"].Value[0] - 'a') * 2 + int.Parse(m.Groups["n"].Value);
            // Sıra önemli: "upper-intermediate" orta değil, "orta-ileri" ileri sayılıyor.
            if (Regex.IsMatch(t, "anadil|native")) return 6;
            if (Regex.IsMatch(t, "ileri|akıcı|fluent|advanced|çok iyi")) return 5;
            if (Regex.IsMatch(t, @"upper|\biyi\b")) return 4;
            if (Regex.IsMatch(t, "orta|intermediate")) return 3;
            if (Regex.IsMatch(t, "başlangıç|temel|beginner|elementary")) return 2;
            return null;
        }

        public static string Name(int rank) => rank switch { 1 => "A1", 2 => "A2", 3 => "B1", 4 => "B2", 5 => "C1", _ => "C2" };
    }
}
