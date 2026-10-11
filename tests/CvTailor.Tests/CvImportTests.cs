using CvTailor.Api.Services.Cv;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace CvTailor.Tests
{
    // Kurgusal CV'ler test sırasında üretiliyor; repoda ikili dosya ve gerçek kişisel veri yok.
    public class CvImportTests
    {
        static CvImportTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        private readonly CvTextExtractor _extractor = new();

        // Canva/Word şablonlarına benzer iki sütunlu CV: solda iletişim ve beceriler, sağda deneyim.
        // Bitişik harfler açık; gerçek hayattaki PDF'lerin çoğu böyle geliyor.
        private static byte[] TwoColumnPdf() => QuestPDF.Fluent.Document.Create(c => c.Page(p =>
        {
            p.Size(PageSizes.A4);
            p.Margin(30);
            p.Content().Row(row =>
            {
                row.ConstantItem(160).Column(col =>
                {
                    col.Item().Text("Deniz Aksoy").FontSize(18).Bold();
                    col.Item().Text("Backend Geliştirici");
                    col.Item().Text("deniz@ornek.test");
                    col.Item().PaddingTop(12).Text("BECERİLER").Bold();
                    col.Item().Text("C#, ASP.NET Core, MSSQL");
                });
                row.RelativeItem().PaddingLeft(20).Column(col =>
                {
                    col.Item().Text("DENEYİM").Bold();
                    col.Item().Text("Yazılım Uzmanı — Örnek Lojistik A.Ş.");
                    col.Item().Text("• Depo takip sisteminin API'sini geliştirdim.");
                    col.Item().Text("• Raporlama ekranının açılma süresini kısalttım.");
                });
            });
        })).GeneratePdf();

        [Fact]
        public void Two_column_pdf_is_read_column_by_column()
        {
            var text = _extractor.Extract(new MemoryStream(TwoColumnPdf()), "cv.pdf").Text;

            // Sol sütun bitmeden sağ sütuna geçilmiyor: beceriler deneyimden önce, satırlar birbirine karışmadan.
            Assert.True(text.IndexOf("BECERİLER", StringComparison.Ordinal) < text.IndexOf("DENEYİM", StringComparison.Ordinal));
            Assert.Contains("C#, ASP.NET Core, MSSQL", text);
            Assert.Contains("Depo takip sisteminin API'sini", text);
        }

        [Fact]
        public void Lost_ligature_letters_are_marked_instead_of_silently_dropped()
        {
            // Bazı PDF'ler "ti" gibi bitişik harfleri karakter karşılığı olmadan basıyor; PdfPig bunları \0 olarak veriyor.
            // (Kullandığımız QuestPDF sürümü karşılığı doğru yazdığı için böyle bir PDF'i burada üretemiyoruz, metinle deniyoruz.)
            var result = _extractor.FromText("Backend Geliş\0rici\nRaporlama ekranının açılma süresini kısal\0m. " + new string('.', 80));

            Assert.True(result.HasUnreadableLetters);
            Assert.Contains($"Geliş{CvTextExtractor.Unreadable}rici", result.Text);
        }

        [Fact]
        public void Unicode_ligatures_become_plain_letters()
        {
            var result = _extractor.FromText("ﬁyat listesi ve oﬃce yazılımları için Excel şablonu hazırladım. " + new string('.', 80));

            Assert.False(result.HasUnreadableLetters);
            Assert.StartsWith("fiyat listesi ve office yazılımları", result.Text);
        }

        [Fact]
        public void Docx_bullets_keep_their_marker()
        {
            using var stream = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
            {
                var body = new Body();
                body.Append(new Paragraph(new Run(new Text("Deniz Aksoy"))));
                body.Append(new Paragraph(new Run(new Text("Yazılım Uzmanı — Örnek Lojistik A.Ş. (Mart 2022 – Günümüz)"))));
                body.Append(new Paragraph(new ParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 })),
                    new Run(new Text("Depo takip sisteminin API'sini geliştirdim."))));
                doc.AddMainDocumentPart().Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body);
            }
            stream.Position = 0;

            var text = _extractor.Extract(stream, "cv.docx").Text;

            Assert.Contains("• Depo takip sisteminin API'sini geliştirdim.", text);
            Assert.DoesNotContain("• Deniz Aksoy", text);
        }

        [Theory]
        [InlineData("cv.doc", "Eski .doc")]
        [InlineData("cv.png", "Sadece PDF, DOCX ya da TXT")]
        public void Unsupported_files_get_a_clear_message(string fileName, string message)
        {
            var ex = Assert.Throws<CvImportException>(() => _extractor.Extract(new MemoryStream(new byte[10]), fileName));
            Assert.StartsWith(message, ex.Message);
        }

        [Fact]
        public void Without_ai_only_contact_details_are_filled()
        {
            var profile = CvParser.ParseContactsOnly("Deniz Aksoy\ndeniz@ornek.test · 0555 000 00 00 · github.com/deniz-ornek\nDENEYİM ...");

            Assert.Equal("deniz@ornek.test", profile.Email);
            Assert.Equal("0555 000 00 00", profile.Phone);
            Assert.Equal("https://github.com/deniz-ornek", profile.GitHubUrl);
            Assert.Empty(profile.Experiences);
        }
    }
}
