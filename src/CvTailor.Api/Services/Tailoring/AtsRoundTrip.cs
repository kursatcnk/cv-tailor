using System.Text.RegularExpressions;
using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Cv;

namespace CvTailor.Api.Services.Tailoring
{
    // Üretilen dosyayı bir işe alım sistemi (ATS) gibi geri okuyup basılan her şeyin metin olarak çıktığını doğruluyor.
    // Okuyucu, CV yüklerken kullandığımız okuyucu; iki sütunlu bir PDF'i bile sıraya sokabiliyor, yani buradan geçemeyen
    // bir dosya gerçek ATS'lerden hiç geçemez. "ATS uyumlu" demek yerine her seferinde ölçüyoruz.
    public static class AtsRoundTrip
    {
        private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

        public static AtsReport Check(TailoredCvDto cv)
        {
            var extractor = new CvTextExtractor();
            return new AtsReport
            {
                Formats =
                {
                    Read(cv, "PDF", () => extractor.Extract(new MemoryStream(CvPdfRenderer.Render(cv)), "cv.pdf")),
                    Read(cv, "Word", () => extractor.Extract(new MemoryStream(CvDocxRenderer.Render(cv)), "cv.docx"))
                }
            };
        }

        private static AtsFormatResult Read(TailoredCvDto cv, string format, Func<CvTextResult> extract)
        {
            var result = new AtsFormatResult { Format = format };
            CvTextResult read;
            try { read = extract(); }
            catch (CvImportException ex)
            {
                result.Checks.Add(new AtsCheck("Metin okunuyor", false, ex.Message));
                return result;
            }

            // Satır sonları ve madde işaretleri ATS'ye göre değişiyor; karşılaştırmada önemli olan kelimelerin sırası.
            var text = Flat(read.Text.Replace("•", " "));
            bool Found(string? value) => !string.IsNullOrWhiteSpace(value) && text.Contains(Flat(value), StringComparison.Ordinal);

            var h = cv.Document.Header;
            result.Checks.Add(new AtsCheck("Ad soyad", Found(h.FullName), h.FullName));
            var contacts = new[] { h.Email, h.Phone }.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            var lost = contacts.Where(c => !Found(c)).ToList();
            result.Checks.Add(new AtsCheck("İletişim bilgileri", contacts.Count > 0 && lost.Count == 0,
                contacts.Count == 0 ? "CV'de e-posta ya da telefon yok." : lost.Count > 0 ? $"Okunamadı: {string.Join(", ", lost)}" : string.Join(", ", contacts)));

            // Başlıklar basıldıkları sırayla bulunmalı; ATS bölümleri bu başlıklarla ayırıyor.
            var titles = PrintedSections(cv).Select(s => CvPdfRenderer.SectionTitles[s]).ToList();
            var positions = titles.Select(t => text.IndexOf(t, StringComparison.Ordinal)).ToList();
            var titlesOk = positions.All(p => p >= 0) && positions.SequenceEqual(positions.OrderBy(p => p));
            result.Checks.Add(new AtsCheck("Bölüm başlıkları", titlesOk, string.Join(" · ", titles)));

            var changes = cv.Changes.ToDictionary(c => c.Id);
            var printed = cv.Document.Experiences.Concat(cv.Document.Projects)
                .SelectMany(e => e.BulletChangeIds)
                .Select(id => changes.TryGetValue(id, out var c) ? c.FinalText : null)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
            var missing = printed.Where(t => !Found(t)).ToList();
            result.Checks.Add(new AtsCheck("Maddeler", missing.Count == 0,
                missing.Count == 0 ? $"{printed.Count} maddenin hepsi eksiksiz okunuyor." : $"{printed.Count} maddenin {missing.Count} tanesi bozuk okunuyor: \"{Short(missing[0]!)}\""));

            result.Checks.Add(new AtsCheck("Harfler", !read.HasUnreadableLetters,
                read.HasUnreadableLetters ? "Bazı harfler okunamıyor." : "Türkçe karakterler dahil hepsi okunuyor."));
            return result;
        }

        // Renderer'lar boş bölümün başlığını basmıyor; kontrol de aynı kuralla.
        private static IEnumerable<string> PrintedSections(TailoredCvDto cv)
        {
            var d = cv.Document;
            return d.SectionOrder.Where(s => s switch
            {
                "experience" => d.Experiences.Count > 0,
                "projects" => d.Projects.Count > 0,
                "education" => d.Educations.Count > 0,
                "skills" => d.Skills.Count > 0,
                "certificates" => d.Certificates.Count > 0,
                _ => false
            });
        }

        private static string Flat(string value) => Spaces.Replace(value, " ").Trim();

        private static string Short(string value) => value.Length <= 60 ? value : value[..60].TrimEnd() + "…";
    }

    public class AtsReport
    {
        public List<AtsFormatResult> Formats { get; set; } = new();
        public bool Passed => Formats.All(f => f.Passed);
    }

    public class AtsFormatResult
    {
        public string Format { get; set; } = string.Empty;
        public List<AtsCheck> Checks { get; set; } = new();
        public bool Passed => Checks.All(c => c.Ok);
    }
}
