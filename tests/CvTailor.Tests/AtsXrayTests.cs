using CvTailor.Api.Services.Cv;
using CvTailor.Api.Services.Tailoring;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CvTailor.Tests
{
    public class AtsXrayTests
    {
        static AtsXrayTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        private readonly CvTextExtractor _extractor = new();

        private const string CleanCv = """
            Deniz Aksoy
            deniz@ornek.test · 0555 000 00 00

            İŞ DENEYİMİ
            Yazılım Uzmanı — Örnek Lojistik A.Ş.
            • Depo takip sisteminin API'sini geliştirdim.

            Eğitim
            Ege Örnek Üniversitesi, Bilgisayar Mühendisliği
            """;

        [Fact]
        public void Two_column_pdf_is_reported()
        {
            var read = _extractor.Extract(new MemoryStream(CvImportTests.TwoColumnPdf()), "cv.pdf");
            Assert.Contains(CvTextExtractor.LayoutColumns, read.Layout);
        }

        [Fact]
        public void Our_single_column_pdf_with_right_aligned_dates_is_not_columns()
        {
            var read = _extractor.Extract(new MemoryStream(CvPdfRenderer.Render(CvPdfRendererTests.Cv())), "cv.pdf");
            Assert.Empty(read.Layout);
        }

        [Fact]
        public void Docx_tables_text_boxes_and_headers_are_reported()
        {
            using var stream = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
            {
                var main = doc.AddMainDocumentPart();
                var header = main.AddNewPart<HeaderPart>();
                header.Header = new Header(new Paragraph(new Run(new Text("deniz@ornek.test"))));
                var body = new Body(
                    new Paragraph(new Run(new Text(CleanCv))),
                    new Table(new TableRow(new TableCell(new Paragraph(new Run(new Text("C#, SQL")))))),
                    new Paragraph(new Run(new Picture(new DocumentFormat.OpenXml.Vml.Shape(new DocumentFormat.OpenXml.Vml.TextBox(
                        new TextBoxContent(new Paragraph(new Run(new Text("Kutudaki metin"))))))))));
                main.Document = new Document(body);
            }
            stream.Position = 0;

            var read = _extractor.Extract(stream, "cv.docx");

            Assert.Contains(CvTextExtractor.LayoutTable, read.Layout);
            Assert.Contains(CvTextExtractor.LayoutTextBox, read.Layout);
            Assert.Contains(CvTextExtractor.LayoutHeader, read.Layout);
        }

        [Fact]
        public void Clean_cv_passes_every_check()
        {
            var checks = AtsXray.ForImport(_extractor.FromText(CleanCv));
            Assert.All(checks, c => Assert.True(c.Ok, $"{c.Label}: {c.Detail}"));
        }

        [Fact]
        public void Icons_missing_phone_and_creative_headings_are_explained()
        {
            var text = "Deniz Aksoy\n deniz@ornek.test\n\nNeler yaptım\nYazılım Uzmanı — Örnek Lojistik A.Ş.\n\nEğitim\nEge Örnek Üniversitesi " + new string('.', 40);

            var checks = AtsXray.ForImport(_extractor.FromText(text)).ToDictionary(c => c.Label);

            Assert.False(checks["Simgeler"].Ok);
            Assert.False(checks["İletişim bilgileri"].Ok);
            Assert.Contains("telefon", checks["İletişim bilgileri"].Detail);
            Assert.False(checks["Bölüm başlıkları"].Ok);
            Assert.Contains("\"Deneyim\"", checks["Bölüm başlıkları"].Detail);
            Assert.DoesNotContain("Eğitim", checks["Bölüm başlıkları"].Detail);
        }
    }
}
