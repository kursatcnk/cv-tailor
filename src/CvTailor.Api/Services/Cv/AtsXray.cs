using System.Globalization;
using System.Text.RegularExpressions;
using CvTailor.Api.Dtos;

namespace CvTailor.Api.Services.Cv
{
    // Yüklenen CV dosyasının ATS röntgeni: bir işe alım sistemi bu dosyadan ne okur, nerede takılır?
    // Kullanıcı kontrol ekranında okunan metni zaten görüyor; bu liste neyin neden bozuk çıktığını açıklıyor.
    public static class AtsXray
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        // Satırın tamamı başlık olmalı: "İŞ DENEYİMİ", "Deneyim:", "Professional Experience".
        private static readonly Regex ExperienceHeading = new(@"^(iş |profesyonel |work |professional )?(deneyim(ler)?i?|tecrübe(ler)?|experience|work history|employment)\s*:?$", RegexOptions.Compiled);
        private static readonly Regex EducationHeading = new(@"^(eğitim( bilgileri)?|öğrenim( durumu)?|education)\s*:?$", RegexOptions.Compiled);

        public static List<AtsCheck> ForImport(CvTextResult read)
        {
            var checks = new List<AtsCheck>
            {
                new("Harfler", !read.HasUnreadableLetters, read.HasUnreadableLetters
                    ? "Bazı harf birleşimleri (\"ti\", \"fi\") metin olarak çıkmıyor; \"Geliştirici\" kelimesi aramada bulunmaz. Kasaya alırken düzelttik ama dosyanın kendisi bu hâliyle ATS'lerde bozuk okunur."
                    : "Türkçe karakterler dahil hepsi okunuyor.")
            };

            // Font ikonları (telefon, e-posta, konum simgeleri) Unicode'un özel kullanım alanından geliyor; ATS'de anlamsız karakter.
            var icons = read.Text.Count(c => c is >= '' and <= '');
            checks.Add(new("Simgeler", icons == 0, icons == 0
                ? "Metne karışan ikon yok."
                : $"{icons} ikon metinde anlamsız karakter olarak çıkıyor. Telefon ve e-posta ikonlarının yerine \"Tel:\", \"E-posta:\" yaz."));

            var contacts = CvParser.ParseContactsOnly(read.Text);
            var missing = new[] { contacts.Email == null ? "e-posta" : null, contacts.Phone == null ? "telefon" : null }.Where(x => x != null).ToList();
            checks.Add(new("İletişim bilgileri", missing.Count == 0, missing.Count == 0
                ? "E-posta ve telefon metinde."
                : $"Metinde {string.Join(" ve ", missing)} bulunamadı. Üst bilgide ya da bir resmin içindeyse ATS'ler okuyamaz; sayfanın gövdesine düz yazı olarak koy."));

            var lines = read.Text.Split('\n').Select(l => l.Trim().ToLower(Tr)).ToList();
            var headings = new[] { (Regex: ExperienceHeading, Name: "Deneyim"), (Regex: EducationHeading, Name: "Eğitim") }
                .Where(h => !lines.Any(l => h.Regex.IsMatch(l)))
                .Select(h => h.Name)
                .ToList();
            checks.Add(new("Bölüm başlıkları", headings.Count == 0, headings.Count == 0
                ? "Deneyim ve eğitim bölümleri standart başlıklarla ayrılmış."
                : $"\"{string.Join("\", \"", headings)}\" başlığı bulunamadı. Bu bölüm CV'nde varsa standart bir başlık kullan; ATS'ler bölümleri bu başlıklardan tanıyor, \"Neler yaptım\" gibi yaratıcı başlıklarda bölüm kayboluyor."));

            var layout = read.Layout.Select(Explain).ToList();
            checks.Add(new("Düzen", layout.Count == 0, layout.Count == 0 ? "Tek sütun, tablo ve metin kutusu yok." : string.Join(" ", layout)));
            return checks;
        }

        private static string Explain(string code) => code switch
        {
            CvTextExtractor.LayoutColumns => "İki sütunlu düzen: biz sütunları sırayla okuyabildik ama birçok ATS satırları yan yana birleştirip karıştırıyor.",
            CvTextExtractor.LayoutTextBox => "Metin kutusu var: bazı ATS'ler metin kutularının içini hiç okumuyor.",
            CvTextExtractor.LayoutTable => "Tablo var: tablo hücreleri bazı ATS'lerde yanlış sırada ya da hiç okunmuyor.",
            CvTextExtractor.LayoutHeader => "Üst/alt bilgide metin var: birçok ATS (ve biz) orayı okumuyor; önemli bir bilgi varsa gövdeye taşı.",
            _ => code
        };
    }
}
