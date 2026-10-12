using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Tailoring;

namespace CvTailor.Tests
{
    public class AtsRoundTripTests
    {
        static AtsRoundTripTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        [Fact]
        public void Both_downloads_pass_with_every_bullet_readable()
        {
            var report = AtsRoundTrip.Check(CvPdfRendererTests.Cv());

            Assert.Equal(new[] { "PDF", "Word" }, report.Formats.Select(f => f.Format));
            foreach (var format in report.Formats)
                Assert.True(format.Passed, $"{format.Format}: {string.Join("; ", format.Checks.Where(c => !c.Ok).Select(c => $"{c.Label} {c.Detail}"))}");
            Assert.Contains(report.Formats[0].Checks, c => c.Label == "Maddeler" && c.Detail!.StartsWith("3 maddenin hepsi"));
        }

        [Fact]
        public void Long_bullets_that_wrap_still_match()
        {
            var cv = CvPdfRendererTests.Cv();
            cv.Changes.Single(c => c.Id == "c1").After =
                "Fiyatlandırma servisini ASP.NET Core ile baştan yazdım; önbellek katmanı, kuyruk tabanlı yeniden deneme ve ayrıntılı loglama ekleyerek yoğun kampanya günlerinde bile servisin ayakta kalmasını sağladım.";

            Assert.True(AtsRoundTrip.Check(cv).Passed);
        }

        [Fact]
        public void Missing_contact_info_fails_the_check()
        {
            var cv = CvPdfRendererTests.Cv();
            cv.Document.Header.Email = null;

            var contact = AtsRoundTrip.Check(cv).Formats[0].Checks.Single(c => c.Label == "İletişim bilgileri");
            Assert.False(contact.Ok);
        }

        [Fact]
        public void Empty_sections_are_not_expected()
        {
            var cv = CvPdfRendererTests.Cv();
            cv.Document.SectionOrder.Add("certificates");

            var titles = AtsRoundTrip.Check(cv).Formats[0].Checks.Single(c => c.Label == "Bölüm başlıkları");
            Assert.True(titles.Ok);
            Assert.DoesNotContain("SERTİFİKALAR", titles.Detail);
        }
    }
}
