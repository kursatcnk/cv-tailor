using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Cv;
using CvTailor.Api.Services.Matching;
using CvTailor.Api.Services.Tailoring;
using static CvTailor.Tests.TestData;

namespace CvTailor.Tests
{
    public class PersonalRequirementsTests
    {
        private readonly RequirementMatcher _matcher = new(Dictionary);

        private MatchItem Match(string text, Action<ProfileDto> fill)
        {
            var profile = Profile();
            fill(profile);
            return _matcher.Match(Analysis(Requirement(text, "other")), profile, Today).Items.Single();
        }

        [Theory]
        [InlineData("Askerlik hizmetini tamamlamış", PersonalRequirements.Military)]
        [InlineData("EHLİYET SAHİBİ", PersonalRequirements.License)]
        [InlineData("B sınıfı sürücü belgesi", PersonalRequirements.License)]
        [InlineData("Seyahat engeli olmayan", PersonalRequirements.Travel)]
        [InlineData("Takım çalışmasına yatkın", null)]
        public void Personal_requirements_are_recognized(string text, string? kind) =>
            Assert.Equal(kind, PersonalRequirements.KindOf(text));

        [Fact]
        public void Completed_military_service_is_strong_and_shown_as_evidence()
        {
            var item = Match("Askerlik hizmetini tamamlamış", p => p.MilitaryStatus = "done");

            Assert.Equal("strong", item.Strength);
            Assert.Equal("personal", item.Evidence[0].Kind);
            Assert.Equal("Askerlik: Tamamlandı", item.Evidence[0].Text);
        }

        [Fact]
        public void Deferral_counts_only_when_the_posting_accepts_it()
        {
            Assert.Equal("weak", Match("Askerliğini tamamlamış", p => { p.MilitaryStatus = "deferred"; p.MilitaryDeferredUntil = 2028; }).Strength);
            var accepted = Match("Askerlik hizmetini tamamlamış ya da tecilli", p => { p.MilitaryStatus = "deferred"; p.MilitaryDeferredUntil = 2028; });
            Assert.Equal("strong", accepted.Strength);
            Assert.Equal("Askerlik: 2028 yılına kadar tecilli", accepted.Evidence[0].Text);
        }

        [Fact]
        public void License_class_must_match()
        {
            Assert.Equal("strong", Match("B sınıfı ehliyet", p => p.DriverLicense = "B").Strength);
            Assert.Equal("strong", Match("Aktif araç kullanabilen, ehliyetli", p => p.DriverLicense = "A2").Strength);
            var wrong = Match("B sınıfı ehliyet", p => p.DriverLicense = "A2");
            Assert.Equal("missing", wrong.Strength);
            Assert.Contains("B sınıfı istiyor", wrong.Note);
        }

        [Fact]
        public void Missing_details_say_where_to_add_them()
        {
            var item = Match("Seyahat engeli olmayan", _ => { });
            Assert.Equal("missing", item.Strength);
            Assert.Empty(item.Evidence);
            Assert.Contains("kişisel bilgilere ekle", item.Note);
        }

        [Fact]
        public void Travel_as_a_skill_is_not_a_personal_detail()
        {
            // "other" dışındaki kategorilerde kişisel bilgi kuralı devreye girmiyor.
            var item = _matcher.Match(Analysis(Requirement("Seyahat acentesinde deneyim", "experience", "Seyahat")), Profile(), Today).Items.Single();
            Assert.NotEqual("personal", item.Evidence.FirstOrDefault()?.Kind);
        }

        [Fact]
        public void Normalizer_cleans_personal_details()
        {
            var profile = ProfileNormalizer.Normalize(new ProfileDto { MilitaryStatus = "done", MilitaryDeferredUntil = 2028, DriverLicense = "b sınıfı" }, "manual");
            Assert.Null(profile.MilitaryDeferredUntil);
            Assert.Equal("B", profile.DriverLicense);
            Assert.Null(ProfileNormalizer.Normalize(new ProfileDto { MilitaryStatus = "yapıldı" }, "manual").MilitaryStatus);
        }

        private static ProfileDto Filled()
        {
            var profile = Profile();
            profile.MilitaryStatus = "exempt";
            profile.DriverLicense = "B";
            profile.CanTravel = true;
            return profile;
        }

        [Fact]
        public void Cv_prints_only_what_the_posting_asks()
        {
            var target = new TargetDto { Analysis = Analysis(Requirement("B sınıfı ehliyet", "other"), Requirement("ASP.NET Core", "technical", "ASP.NET Core")) };
            Assert.Equal(new[] { "Ehliyet: B sınıfı" }, TailoringService.PersonalLines(target, Filled()));
        }

        [Fact]
        public void Profession_target_prints_every_filled_detail()
        {
            var target = new TargetDto { ProfessionKey = "backend-developer" };
            Assert.Equal(new[] { "Askerlik: Muaf", "Ehliyet: B sınıfı", "Seyahat engeli yok" }, TailoringService.PersonalLines(target, Filled()));
        }

        [Fact]
        public void Personal_line_reaches_both_downloads()
        {
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            var cv = CvPdfRendererTests.Cv();
            cv.Document.Header.Personal = new() { "Askerlik: Muaf", "Ehliyet: B sınıfı" };

            var pdf = new CvTextExtractor().Extract(new MemoryStream(CvPdfRenderer.Render(cv)), "cv.pdf").Text;
            var docx = new CvTextExtractor().Extract(new MemoryStream(CvDocxRenderer.Render(cv)), "cv.docx").Text;
            Assert.Contains("Askerlik: Muaf · Ehliyet: B sınıfı", pdf.Replace("  ", " "));
            Assert.Contains("Askerlik: Muaf  ·  Ehliyet: B sınıfı", docx);
        }
    }
}
