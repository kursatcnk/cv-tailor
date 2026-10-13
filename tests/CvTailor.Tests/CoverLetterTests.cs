using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Ai;
using CvTailor.Api.Services.Privacy;
using CvTailor.Api.Services.Tailoring;
using Microsoft.Extensions.Configuration;
using static CvTailor.Tests.TestData;

namespace CvTailor.Tests
{
    public class CoverLetterTests
    {
        private static (CoverLetterWriter Writer, RewritePrivacyTests.RecordingProvider Provider) Create(string reply)
        {
            var provider = new RewritePrivacyTests.RecordingProvider { Reply = reply };
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AI:Preferred"] = "fake" }).Build();
            return (new CoverLetterWriter(new AiService(new[] { provider }, config)), provider);
        }

        private static CoverLetterInput Input() => new()
        {
            TargetTitle = "Backend Developer",
            Company = "Örnek Teknoloji",
            Requirements = { ("ASP.NET Core ile API geliştirme", "must") },
            TotalExperience = "3 yıl 2 ay",
            Entries = { ("Yazılım Uzmanı", "Örnek Lojistik", new List<string> { "Sipariş servisini ASP.NET Core ile yazdım; destek için deniz.aksoy@ornek.test adresini kurdum." }) }
        };

        private static readonly PiiMask Mask = PiiMask.ForProfile(new ProfileDto { FullName = "Deniz Aksoy", Email = "deniz.aksoy@ornek.test" });

        [Fact]
        public async Task Name_and_contact_details_stay_out_of_the_prompt()
        {
            var (writer, provider) = Create("""{ "paragraphs": ["Backend Developer pozisyonu için başvuruyorum."] }""");
            await writer.WriteAsync(Input(), Mask, CancellationToken.None);

            Assert.DoesNotContain("Deniz", provider.LastMessage);
            Assert.DoesNotContain("ornek.test", provider.LastMessage);
            Assert.Contains("Örnek Teknoloji", provider.LastMessage);
        }

        [Fact]
        public async Task Greeting_and_signature_written_by_the_ai_are_dropped()
        {
            var (writer, _) = Create("""{ "paragraphs": ["Sayın İK Ekibi,", "İlanınızdaki pozisyona başvuruyorum.", "", "Saygılarımla, Deniz"] }""");
            var (paragraphs, _) = await writer.WriteAsync(Input(), Mask, CancellationToken.None);

            Assert.Equal(new[] { "İlanınızdaki pozisyona başvuruyorum." }, paragraphs);
        }

        [Fact]
        public void Guard_flags_a_claim_that_is_not_in_the_cv()
        {
            var guard = new FabricationGuard(Dictionary);
            var source = "Sipariş servisini ASP.NET Core ile yazdım.\nToplam iş deneyimi: 3 yıl 2 ay\nBackend Developer\nÖrnek Teknoloji";

            Assert.Equal("verified", guard.Check("3 yılı aşkın süredir ASP.NET Core ile backend geliştiriyorum; Örnek Teknoloji ekibine katkı sunmak isterim.", source, new[] { "Docker" }).Status);
            var flagged = guard.Check("Docker ile 5 servisi yönettim.", source, new[] { "Docker" });
            Assert.Equal("flagged", flagged.Status);
            Assert.Contains(flagged.Issues, i => i.Contains("Docker"));
        }
    }
}
