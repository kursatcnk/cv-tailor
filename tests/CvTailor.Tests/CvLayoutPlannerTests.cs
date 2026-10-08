using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Matching;
using CvTailor.Api.Services.Tailoring;
using static CvTailor.Tests.TestData;

namespace CvTailor.Tests
{
    public class CvLayoutPlannerTests
    {
        private readonly RequirementMatcher _matcher = new(Dictionary);

        private CvSelection Select(ProfileDto profile, params JobRequirement[] requirements) =>
            CvLayoutPlanner.Select(profile, _matcher.Match(Analysis(requirements), profile, Today), Today);

        [Fact]
        public void Bullets_that_prove_a_requirement_score_higher()
        {
            var profile = Profile();
            var selection = Select(profile, Requirement("ASP.NET Core bilgisi", "technical", "ASP.NET Core"));

            var aspnet = profile.Experiences[0].Achievements[0].Id!.Value;
            var other = profile.Experiences[0].Achievements[1].Id!.Value;
            Assert.True(selection.Scores.GetValueOrDefault(aspnet) > selection.Scores.GetValueOrDefault(other));
            Assert.Equal(new[] { "r1" }, selection.Requirements[aspnet]);
        }

        [Fact]
        public void Internship_is_dropped_when_there_are_two_real_jobs_and_it_is_unrelated()
        {
            var profile = Profile();
            profile.Experiences.Add(new ExperienceDto { Id = Guid.NewGuid(), Title = "Destek Uzmanı", Company = "Deneme A.Ş.", StartDate = "2023-10", EndDate = "2024-02" });

            var selection = Select(profile, Requirement("ASP.NET Core bilgisi", "technical", "ASP.NET Core"));

            Assert.Contains(selection.Dropped, d => d.Kind == "experience" && d.Text.StartsWith("Stajyer"));
            Assert.Equal(2, selection.Entries.Count(e => e.Kind == "experience"));
        }

        [Fact]
        public void Keeps_the_internship_for_someone_with_little_experience()
        {
            var selection = Select(Profile(), Requirement("ASP.NET Core bilgisi", "technical", "ASP.NET Core"));
            Assert.Equal(2, selection.Entries.Count(e => e.Kind == "experience"));
        }

        [Fact]
        public void Latest_job_gets_at_most_five_bullets_and_the_rest_are_explained()
        {
            var profile = Profile();
            for (var i = 0; i < 5; i++)
                profile.Experiences[0].Achievements.Add(new AchievementDto { Id = Guid.NewGuid(), Text = $"Ek madde {i}" });

            var selection = Select(profile, Requirement("ASP.NET Core bilgisi", "technical", "ASP.NET Core"));

            Assert.Equal(5, selection.Entries[0].Facts.Count);
            Assert.Equal(2, selection.Dropped.Count(d => d.Kind == "bullet"));
            Assert.Contains(selection.Entries[0].Facts, f => f.Text!.Contains("ASP.NET Core"));
        }

        [Fact]
        public void Fit_removes_lowest_scored_bullets_until_the_page_fits_but_keeps_one_per_job()
        {
            var document = new CvDocument();
            var changes = new List<CvChange>();
            var scores = new Dictionary<string, double>();
            for (var job = 0; job < 4; job++)
            {
                var entry = new CvEntry { SourceId = Guid.NewGuid(), Title = $"İş {job}" };
                for (var b = 0; b < 5; b++)
                {
                    var id = $"c{job}-{b}";
                    // 3 satırlık madde: 4 iş × 5 madde sayfayı rahatça taşırıyor.
                    changes.Add(new CvChange { Id = id, After = new string('x', 250), SourceIds = { Guid.NewGuid() } });
                    entry.BulletChangeIds.Add(id);
                    scores[id] = b == 0 ? 10 : b;
                }
                document.Experiences.Add(entry);
            }

            CvLayoutPlanner.Fit(document, changes, scores);

            Assert.True(CvLayoutPlanner.EstimateLines(document, changes.ToDictionary(c => c.Id)) <= CvLayoutPlanner.LineBudget);
            Assert.All(document.Experiences, e => Assert.Contains($"c{e.Title[^1]}-0", e.BulletChangeIds));
            Assert.NotEmpty(document.Dropped);
        }
    }
}
