using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Cv;

namespace CvTailor.Tests
{
    public class ProfileNormalizerTests
    {
        [Theory]
        [InlineData("2023-04", "2023-04")]
        [InlineData("04/2023", "2023-04")]
        [InlineData("4.2023", "2023-04")]
        [InlineData("Nisan 2023", "2023-04")]
        [InlineData("Ağu 2021", "2021-08")]
        [InlineData("APRIL 2022", "2022-04")]
        [InlineData("2019", "2019")]
        [InlineData("13/2023", "2023")]
        [InlineData("geçen yıl", null)]
        public void Dates_end_up_in_one_format(string input, string? expected) =>
            Assert.Equal(expected, ProfileNormalizer.Date(input, out _));

        [Theory]
        [InlineData("Günümüz")]
        [InlineData("Halen")]
        [InlineData("Present")]
        public void Ongoing_words_mean_current(string input)
        {
            Assert.Null(ProfileNormalizer.Date(input, out var current));
            Assert.True(current);
        }

        [Fact]
        public void Cleans_without_adding_anything()
        {
            var profile = ProfileNormalizer.Normalize(new ProfileDto
            {
                GitHubUrl = "github.com/deniz-ornek",
                WebsiteUrl = "benim sitem",
                Experiences =
                {
                    new ExperienceDto { Company = "Örnek A.Ş.", Title = "Uzman", StartDate = "Mart 2022", EndDate = "Halen", EmploymentType = "tam zamanlı",
                        Achievements = { new AchievementDto { Text = "• API geliştirdim." }, new AchievementDto { Text = "   " } } },
                    new ExperienceDto { Company = " ", Title = null }
                },
                Skills = { new SkillDto { Name = "C#" }, new SkillDto { Name = "c#" }, new SkillDto { Name = "İngilizce", Category = "language", Level = "B2" } }
            }, "cv");

            var e = profile.Experiences.Single();
            Assert.Equal("https://github.com/deniz-ornek", profile.GitHubUrl);
            Assert.Null(profile.WebsiteUrl);
            Assert.Equal("2022-03", e.StartDate);
            Assert.True(e.IsCurrent);
            Assert.Null(e.EmploymentType);
            Assert.Equal("API geliştirdim.", e.Achievements.Single().Text);
            Assert.Equal("cv", e.Achievements.Single().Source);
            Assert.Equal(new[] { "C#", "İngilizce" }, profile.Skills.Select(s => s.Name));
        }
    }
}
