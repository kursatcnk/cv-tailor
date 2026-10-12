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
    }
}
