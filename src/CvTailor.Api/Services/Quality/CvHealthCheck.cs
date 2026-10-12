using System.Globalization;
using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Cv;

namespace CvTailor.Api.Services.Quality
{
    // Kasadaki CV'nin ilandan bağımsız sorunları: tarih hataları, boşluklar, klişeler, zayıf fiiller, rakamsız maddeler.
    // İşe alımcının ilk 10 saniyede takıldığı şeyler bunlar. Kaydedilmemiş profil üzerinde de çalışıyor (saf fonksiyon).
    public static class CvHealthCheck
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
        private const int GapMonths = 6;

        // E-posta adresinde iş başvurusuna uymayan kelimeler. Çok basit bir liste; amaç "prenses_1995" tipini yakalamak.
        // "angel", "king", "tatli" gibi isim ve soyadların içinde geçebilenleri bilerek koymadım (Angela, Tatlıcı).
        private static readonly string[] CasualEmailWords =
        {
            "prens", "kralice", "askim", "kanka", "delikanli", "canavar", "manyak", "cilgin", "yakisikli",
            "baby", "bebek", "cool", "sexy", "princess", "queen", "crazy", "killer"
        };

        public static HealthReport Check(ProfileDto profile, DateTime today)
        {
            // Formdan gelen profil; boş listeler null gelebiliyor.
            profile.Experiences ??= new();
            profile.Projects ??= new();
            foreach (var e in profile.Experiences) e.Achievements ??= new();
            foreach (var p in profile.Projects) p.Achievements ??= new();

            var issues = new List<HealthIssue>();
            var nowIndex = today.Year * 12 + today.Month - 1;

            CheckContact(profile, issues);
            CheckDates(profile, nowIndex, issues);

            if (!string.IsNullOrWhiteSpace(profile.Summary))
                foreach (var issue in BulletLinter.Lint(profile.Summary).Where(i => i.Code is "cliche" or "pronoun"))
                    issues.Add(new HealthIssue(issue, "Özet", "summary"));

            var bullets = 0;
            var withNumbers = 0;
            var seen = new Dictionary<string, string>();
            void Bullets(List<AchievementDto> achievements, string where, string path)
            {
                var moods = new HashSet<string>();
                for (var i = 0; i < achievements.Count; i++)
                {
                    var text = achievements[i].Text?.Trim();
                    // Rakam sorusunun cevabı ayrı madde değil, bağlı olduğu maddeye ek bilgi.
                    if (string.IsNullOrEmpty(text) || achievements[i].RelatedId != null) continue;
                    bullets++;
                    var lint = BulletLinter.Lint(text);
                    if (lint.All(x => x.Code != "no-number")) withNumbers++;
                    // Rakamsızlığı madde madde değil toplu söylüyoruz; her maddenin altında aynı uyarı gürültü oluyor.
                    foreach (var issue in lint.Where(x => x.Code != "no-number"))
                        issues.Add(new HealthIssue(issue, where, $"{path}.achievements.{i}"));

                    var key = text.ToLower(Tr);
                    if (seen.TryGetValue(key, out var first))
                        issues.Add(new HealthIssue("duplicate", "info", "Aynı madde iki yerde", $"Bu madde {first} altında da var. Tekrar eden madde yer kaplar; birini sil ya da farklılaştır.", where, $"{path}.achievements.{i}"));
                    else seen[key] = where;

                    if (BulletLinter.Mood(text) is { } mood) moods.Add(mood);
                }
                if (moods.Count > 1)
                    issues.Add(new HealthIssue("mood", "info", "Maddeler farklı kiplerde", "Bir kısmı \"geliştirdim\", bir kısmı \"geliştirildi\" ya da \"geliştirme\" diye bitiyor. Hepsini aynı kipte yaz; en güçlüsü birinci tekil: \"geliştirdim\".", where, path));
            }

            for (var i = 0; i < profile.Experiences.Count; i++)
            {
                var e = profile.Experiences[i];
                var where = Label(e.Company, e.Title);
                if (e.Achievements.All(a => string.IsNullOrWhiteSpace(a.Text)))
                    issues.Add(new HealthIssue("empty-entry", "warn", "Hiç madde yok", "Sadece şirket ve unvan yazmak ne yaptığını anlatmıyor. En az iki madde ekle: ne yaptın, sonucu ne oldu?", where, $"experiences.{i}"));
                Bullets(e.Achievements, where, $"experiences.{i}");
            }
            for (var i = 0; i < profile.Projects.Count; i++)
                Bullets(profile.Projects[i].Achievements, Label(profile.Projects[i].Name, null), $"projects.{i}");

            // Üç maddeden azsa oran bir şey söylemiyor.
            if (bullets >= 3 && withNumbers * 10 < bullets * 3)
                issues.Add(new HealthIssue("few-numbers", "info", $"{bullets} maddenin {withNumbers} tanesinde rakam var",
                    "Rakam, işe alımcının ilk aradığı şey: kaç kullanıcı, kaç kişilik ekip, yüzde kaç hızlanma. Bilmiyorsan hedef ekranındaki sorular rakamları bulmana yardım ediyor.", "Genel", null));

            return new HealthReport
            {
                Issues = issues.OrderBy(i => i.Level switch { "error" => 0, "warn" => 1, _ => 2 }).ToList(),
                Bullets = bullets,
                BulletsWithNumbers = withNumbers
            };
        }

        private static void CheckContact(ProfileDto profile, List<HealthIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(profile.Email) && string.IsNullOrWhiteSpace(profile.Phone))
                issues.Add(new HealthIssue("contact", "error", "İletişim bilgisi yok", "E-posta ya da telefon olmadan sana ulaşamazlar.", "Kişisel bilgiler", "contact"));

            var local = profile.Email?.Split('@')[0].ToLowerInvariant() ?? "";
            var casual = CasualEmailWords.FirstOrDefault(w => local.Contains(w));
            if (casual != null)
                issues.Add(new HealthIssue("email", "warn", "E-posta adresi resmi durmuyor",
                    $"\"{casual}\" geçen bir adres ilk izlenimi bozabilir. Ad ve soyadından oluşan bir adres aç; ücretsiz ve beş dakika sürüyor.", "Kişisel bilgiler", "contact"));
        }

        private static void CheckDates(ProfileDto profile, int nowIndex, List<HealthIssue> issues)
        {
            var ranges = new List<(int Start, int End, string Where, bool Main)>();
            for (var i = 0; i < profile.Experiences.Count; i++)
            {
                var e = profile.Experiences[i];
                var where = Label(e.Company, e.Title);
                var path = $"experiences.{i}";
                var start = Month(e.StartDate, false);
                var end = e.IsCurrent ? nowIndex : Month(e.EndDate, true);

                if (start == null)
                {
                    issues.Add(new HealthIssue("no-date", "warn", "Başlangıç tarihi yok", "Tarihsiz deneyim süre hesabına girmiyor ve \"en az 2 yıl\" gibi şartlarda sayılmıyor.", where, path));
                    continue;
                }
                if (start > nowIndex)
                    issues.Add(new HealthIssue("future", "error", "Başlangıç tarihi gelecekte", "Tarihi kontrol et; büyük ihtimalle yıl yanlış yazılmış.", where, path));
                if (end != null && end < start)
                {
                    issues.Add(new HealthIssue("end-before-start", "error", "Bitiş başlangıçtan önce", "Tarihler ters girilmiş olabilir.", where, path));
                    continue;
                }
                if (!e.IsCurrent && end == null)
                    issues.Add(new HealthIssue("no-end", "info", "Bitiş tarihi yok", "Hâlâ çalışıyorsan \"Devam ediyor\"u işaretle; değilse bitiş tarihini yaz.", where, path));

                // Yarı zamanlı, staj ve serbest işler bir tam zamanlı işle aynı anda olabilir; çakışma onlar için sorun değil.
                var main = e.EmploymentType is null or "full-time";
                ranges.Add((start.Value, end ?? start.Value, where, main));
            }

            var mains = ranges.Where(r => r.Main).OrderBy(r => r.Start).ToList();
            for (var i = 0; i < mains.Count; i++)
                for (var j = i + 1; j < mains.Count; j++)
                {
                    var overlap = Math.Min(mains[i].End, mains[j].End) - mains[j].Start + 1;
                    // Bir ay çakışma iş değişikliğinde normal (biri ayın başında bitip diğeri sonunda başlıyor).
                    if (overlap > 1)
                        issues.Add(new HealthIssue("overlap", "info", $"{mains[i].Where} ile {overlap} ay çakışıyor",
                            "İki tam zamanlı iş aynı dönemde görünüyor. Biri yarı zamanlı ya da serbestse türünü belirt; değilse tarihleri kontrol et.", mains[j].Where, null));
                }

            // Boşluk: tüm işler (staj, serbest dahil) birleştirilip aralarına bakılıyor.
            var sorted = ranges.OrderBy(r => r.Start).ToList();
            var coveredUntil = int.MinValue;
            string? previous = null;
            foreach (var r in sorted)
            {
                if (previous != null && r.Start - coveredUntil - 1 >= GapMonths)
                    issues.Add(new HealthIssue("gap", "info", $"{r.Start - coveredUntil - 1} aylık boşluk",
                        $"{previous} ile {r.Where} arasında. Mülakatta sorulur; bu dönemde askerlik, kurs, proje ya da serbest iş varsa CV'ye ekle.", r.Where, null));
                if (r.End > coveredUntil) (coveredUntil, previous) = (r.End, r.Where);
            }
        }

        // "2023-04" → ay sırası. Sadece yıl varsa başlangıçta Ocak, bitişte Aralık (boşlukları büyütmemek için).
        // Kontrol kaydedilmemiş formda da çalıştığı için "04/2023" gibi elle yazılmış biçimler önce düzeltiliyor.
        private static int? Month(string? value, bool isEnd)
        {
            var date = ProfileNormalizer.Date(value, out _);
            if (string.IsNullOrEmpty(date) || date.Length < 4 || !int.TryParse(date[..4], out var year)) return null;
            var month = date.Length >= 7 && int.TryParse(date[5..7], out var m) ? m : isEnd ? 12 : 1;
            return year * 12 + month - 1;
        }

        private static string Label(string? a, string? b)
        {
            var parts = new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            return parts.Count == 0 ? "Adsız kayıt" : string.Join(" · ", parts);
        }
    }

    public class HealthReport
    {
        public List<HealthIssue> Issues { get; set; } = new();
        public int Bullets { get; set; }
        public int BulletsWithNumbers { get; set; }
    }

    // Path: sorunun ekrandaki yeri ("experiences.0.achievements.2"); null ise genel.
    public record HealthIssue(string Code, string Level, string Message, string Hint, string Where, string? Path)
    {
        public HealthIssue(LintIssue issue, string where, string? path) : this(issue.Code, issue.Level, issue.Message, issue.Hint, where, path) { }
    }
}
