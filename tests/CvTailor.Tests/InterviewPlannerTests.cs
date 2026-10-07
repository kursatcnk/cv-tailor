using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Interview;
using CvTailor.Api.Services.Matching;
using static CvTailor.Tests.TestData;

namespace CvTailor.Tests
{
    public class InterviewPlannerTests
    {
        private readonly RequirementMatcher _matcher = new(Dictionary);

        private List<InterviewQuestion> Plan(JobAnalysis analysis, ProfileDto profile, InterviewState? state = null) =>
            InterviewPlanner.Plan(analysis, _matcher.Match(analysis, profile, Today), profile, state ?? new InterviewState());

        [Fact]
        public void Asks_about_missing_and_weak_requirements_but_not_strong_ones()
        {
            var analysis = Analysis(
                Requirement("ASP.NET Core bilgisi", "technical", "ASP.NET Core"),   // güçlü
                Requirement("Kubernetes deneyimi", "technical", "Kubernetes"),      // yok
                Requirement("Docker bilgisi", "tool", "Docker"));                   // zayıf (sadece beceri)

            var keys = Plan(analysis, Profile()).Where(q => q.Kind == "requirement").Select(q => q.RequirementKey).ToList();

            Assert.Equal(new[] { "r2", "r3" }, keys);
        }

        [Fact]
        public void Does_not_ask_about_education_or_languages()
        {
            var analysis = Analysis(
                Requirement("İktisat mezunu", "education", "İktisat"),
                Requirement("Almanca", "language", "Almanca"));

            Assert.DoesNotContain(Plan(analysis, Profile()), q => q.Kind == "requirement");
        }

        [Fact]
        public void Answered_and_skipped_questions_are_not_asked_again()
        {
            var analysis = Analysis(
                Requirement("Kubernetes deneyimi", "technical", "Kubernetes"),
                Requirement("Redis bilgisi", "technical", "Redis"));
            var state = new InterviewState { Answered = { "req:r1" }, Skipped = { "req:r2" } };

            Assert.DoesNotContain(Plan(analysis, Profile(), state), q => q.Kind == "requirement");
        }

        [Fact]
        public void Number_questions_target_bullets_without_numbers()
        {
            var profile = Profile();
            var questions = Plan(Analysis(Requirement("ASP.NET Core bilgisi", "technical", "ASP.NET Core")), profile)
                .Where(q => q.Kind == "number").ToList();

            var bulletTexts = profile.Experiences.SelectMany(e => e.Achievements).ToDictionary(a => a.Id!.Value, a => a.Text);
            Assert.NotEmpty(questions);
            Assert.All(questions, q => Assert.DoesNotMatch(@"\d", bulletTexts[q.AchievementId!.Value]));
            // İlanın zorunlu gereksinimine kanıt olan madde ilk soruluyor.
            Assert.Contains("ASP.NET Core", bulletTexts[questions[0].AchievementId!.Value]);
        }

        [Fact]
        public void Interview_answers_are_not_asked_for_numbers()
        {
            var profile = Profile();
            foreach (var a in profile.Experiences.SelectMany(e => e.Achievements)) a.Source = "interview";

            Assert.DoesNotContain(Plan(Analysis(Requirement("ASP.NET Core bilgisi", "technical", "ASP.NET Core")), profile), q => q.Kind == "number");
        }

        [Fact]
        public void Partial_match_asks_about_the_missing_part()
        {
            var question = Plan(Analysis(Requirement("ASP.NET Core ve Kubernetes deneyimi", "technical", "ASP.NET Core", "Kubernetes")), Profile())
                .Single(q => q.Kind == "requirement");

            Assert.Contains("kasanda Kubernetes görünmüyor", question.Prompt);
        }

        [Fact]
        public void Weak_evidence_suggests_the_job_it_came_from()
        {
            var profile = Profile();
            var current = profile.Experiences[0];
            current.Achievements.Add(new AchievementDto { Id = Guid.NewGuid(), Text = "Docker'ı denedim." });
            // Sadece Docker geçiyor, Kubernetes yok: zayıf, kanıt ilk işte.
            var question = Plan(Analysis(Requirement("Docker ve Kubernetes", "technical", "Docker", "Kubernetes")), profile)
                .Single(q => q.Kind == "requirement");

            Assert.Equal(current.Id, question.SuggestedParentId);
        }

        [Fact]
        public void At_most_five_requirement_questions()
        {
            var requirements = Enumerable.Range(1, 8).Select(i => Requirement($"Araç {i}", "tool", $"Arac{i}")).ToArray();
            Assert.Equal(InterviewPlanner.MaxRequirementQuestions, Plan(Analysis(requirements), Profile()).Count(q => q.Kind == "requirement"));
        }
    }
}
