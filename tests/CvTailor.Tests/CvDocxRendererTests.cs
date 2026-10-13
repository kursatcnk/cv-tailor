using CvTailor.Api.Services.Cv;
using CvTailor.Api.Services.Tailoring;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace CvTailor.Tests
{
    public class CvDocxRendererTests
    {
        [Fact]
        public void Word_file_is_valid_open_xml()
        {
            using var stream = new MemoryStream(CvDocxRenderer.Render(CvPdfRendererTests.Cv()));
            using var doc = WordprocessingDocument.Open(stream, false);

            // Word "dosya bozuk" demesin.
            var errors = new OpenXmlValidator().Validate(doc).Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
            Assert.Empty(errors);
        }

        [Fact]
        public void Bullets_are_real_list_items_and_follow_decisions()
        {
            var text = new CvTextExtractor().Extract(new MemoryStream(CvDocxRenderer.Render(CvPdfRendererTests.Cv())), "cv.docx").Text;

            // Okuyucumuz Word listesindeki paragrafların başına "•" koyuyor; ATS'ler de onları liste olarak görüyor.
            Assert.Contains("• Fiyatlandırma servisini ASP.NET Core ile geliştirdim; yanıt süresini kısalttım.", text);
            Assert.Contains("• Orijinal madde.", text);
            Assert.DoesNotContain("%90", text);
            Assert.True(text.IndexOf("DENEYİM", StringComparison.Ordinal) < text.IndexOf("EĞİTİM", StringComparison.Ordinal));
        }

        [Fact]
        public void Contact_links_and_title_are_real_word_properties()
        {
            var cv = CvPdfRendererTests.Cv();
            cv.Document.Header.Links.Add("github.com/deniz-ornek");

            using var stream = new MemoryStream(CvDocxRenderer.Render(cv));
            using var doc = WordprocessingDocument.Open(stream, false);
            var links = doc.MainDocumentPart!.HyperlinkRelationships.Select(r => r.Uri.ToString()).ToList();

            Assert.Contains("mailto:deniz@ornek.test", links);
            Assert.Contains("https://github.com/deniz-ornek", links);
            Assert.Equal("Deniz Aksoy - Backend Developer CV", doc.PackageProperties.Title);
            Assert.Empty(new OpenXmlValidator().Validate(doc));
        }
    }
}
