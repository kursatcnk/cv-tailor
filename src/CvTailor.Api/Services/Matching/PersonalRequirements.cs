using System.Globalization;
using System.Text.RegularExpressions;
using CvTailor.Api.Dtos;

namespace CvTailor.Api.Services.Matching
{
    // Türkiye'deki ilanların sık sorduğu, beceri olmayan şartlar: askerlik, ehliyet, seyahat.
    // Bunlar anahtar kelimeyle değil kasadaki kişisel bilgilerle karşılanıyor. CV'ye de buradaki etiketlerle basılıyor.
    public static class PersonalRequirements
    {
        public const string Military = "military";
        public const string License = "license";
        public const string Travel = "travel";

        // Metin önce tr-TR ile küçültülüyor ("EHLİYET" → "ehliyet"); regex'ler küçük harfle yazılı.
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
        private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
        private static readonly Regex MilitaryText = new(@"askerl|askeri\s+hizmet|tecil|military service", Options);
        private static readonly Regex LicenseText = new(@"ehliyet|sürücü\s+belge|driv(er'?s|ing)\s+licen[cs]e", Options);
        private static readonly Regex TravelText = new(@"seyahat|travel", Options);
        // "B sınıfı", "b sinifi", "class B"
        private static readonly Regex LicenseClass = new(@"\b(?<c>[a-g][1e]?|[a-d]e)\s*(sınıfı|sinifi|sınıf|class)\b|\bclass\s+(?<c>[a-g][1e]?)\b", Options);

        // Bu gereksinim hangi kişisel bilgiyle ilgili? Değilse null.
        public static string? KindOf(string text)
        {
            var lower = text.ToLower(Tr);
            return MilitaryText.IsMatch(lower) ? Military
                : LicenseText.IsMatch(lower) ? License
                : TravelText.IsMatch(lower) ? Travel
                : null;
        }

        // CV'ye basılan hâli; bilgi kasada yoksa null.
        public static string? Label(ProfileDto p, string kind) => kind switch
        {
            Military => p.MilitaryStatus switch
            {
                "done" => "Askerlik: Tamamlandı",
                "exempt" => "Askerlik: Muaf",
                "deferred" => p.MilitaryDeferredUntil is { } year ? $"Askerlik: {year} yılına kadar tecilli" : "Askerlik: Tecilli",
                _ => null
            },
            License => string.IsNullOrWhiteSpace(p.DriverLicense) ? null : $"Ehliyet: {p.DriverLicense} sınıfı",
            Travel => p.CanTravel == true ? "Seyahat engeli yok" : null,
            _ => null
        };

        public static (string Strength, string Note) Match(string kind, string requirementText, ProfileDto p)
        {
            var requirement = requirementText.ToLower(Tr);
            switch (kind)
            {
                case Military:
                    return p.MilitaryStatus switch
                    {
                        "done" or "exempt" => ("strong", "Kişisel bilgilerinde var; CV'ye de yazılacak."),
                        // "Askerliğini tamamlamış ya da tecilli" gibi ilanlarda tecil yeterli.
                        "deferred" when requirement.Contains("tecil") => ("strong", "İlan tecili kabul ediyor; tecil bilgin CV'ye yazılacak."),
                        "deferred" => ("weak", "Tecillisin; ilan askerliğini tamamlamış ya da muaf birini istiyor olabilir. Tecil bitiş yılını CV'de yazmak işverenin sorusunu baştan cevaplar."),
                        _ => ("missing", "Askerlik durumun kasanda yok. Erkek adaylara Türkiye'de çok sorulur; kişisel bilgilere ekle.")
                    };
                case License:
                {
                    if (string.IsNullOrWhiteSpace(p.DriverLicense)) return ("missing", "Ehliyet bilgin kasada yok. Varsa sınıfıyla kişisel bilgilere ekle.");
                    var wanted = LicenseClass.Match(requirement) is { Success: true } m ? m.Groups["c"].Value.ToUpperInvariant() : null;
                    var have = p.DriverLicense.ToUpperInvariant().Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (wanted == null || have.Contains(wanted)) return ("strong", "Kişisel bilgilerinde var; CV'ye de yazılacak.");
                    return ("missing", $"İlan {wanted} sınıfı istiyor; kasanda {p.DriverLicense} sınıfı var.");
                }
                case Travel:
                    return p.CanTravel switch
                    {
                        true => ("strong", "Kişisel bilgilerinde var; CV'ye de yazılacak."),
                        false => ("missing", "Kasanda seyahat edemeyeceğini belirtmişsin; ilan seyahat istiyor."),
                        _ => ("missing", "Seyahat engelin yoksa kişisel bilgilere ekle; ilan bunu soruyor.")
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}
