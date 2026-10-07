using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Matching;
using static CvTailor.Tests.TestData;

namespace CvTailor.Tests
{
    public class RequirementMatcherTests
    {
        private readonly RequirementMatcher _matcher = new(Dictionary);

        private MatchItem MatchSingle(JobRequirement requirement, ProfileDto? profile = null) =>
            _matcher.Match(Analysis(requirement), profile ?? Profile(), Today).Items.Single();

        [Fact]
        public void Term_used_in_a_job_bullet_is_strong_evidence()
        {
            var item = MatchSingle(Requirement("ASP.NET Core bilgisi", "technical", "ASP.NET Core"));

            Assert.Equal("strong", item.Strength);
            Assert.Equal("achievement", item.Evidence[0].Kind);
            Assert.Equal("Yazılım Uzmanı · Örnek Lojistik", item.Evidence[0].Where);
        }

        [Fact]
        public void Synonym_in_a_bullet_counts_and_shows_which_word_matched()
        {
            var item = MatchSingle(Requirement("MSSQL bilgisi", "technical", "MSSQL"));

            Assert.Equal("strong", item.Strength);
            Assert.Equal("SQL Server → MSSQL", item.Evidence[0].Term);
        }

        [Fact]
        public void Company_name_counts_for_sector_experience()
        {
            var item = MatchSingle(Requirement("Lojistik sektöründe deneyim", "experience", "Lojistik"));

            Assert.Equal("strong", item.Strength);
            Assert.Equal("experience", item.Evidence[0].Kind);
        }

        [Fact]
        public void Project_bullet_is_reported_as_a_project()
        {
            var profile = Profile();
            profile.Projects.Add(new ProjectDto
            {
                Id = Guid.NewGuid(), Name = "Kütüphane Takip",
                Achievements = { new AchievementDto { Id = Guid.NewGuid(), Text = "Entity Framework ile ödünç takibi yaptım." } }
            });

            var item = MatchSingle(Requirement("Entity Framework bilgisi", "technical", "Entity Framework"), profile);

            Assert.Equal("projectAchievement", item.Evidence[0].Kind);
            Assert.Equal("Kütüphane Takip projesinde geçiyor.", item.Note);
        }

        [Fact]
        public void Skill_only_listed_is_weak_evidence()
        {
            var item = MatchSingle(Requirement("Docker bilgisi", "tool", "Docker"));

            Assert.Equal("weak", item.Strength);
            Assert.Equal("skill", item.Evidence.Single().Kind);
        }

        [Fact]
        public void Listed_language_is_enough_for_a_language_requirement()
        {
            var item = MatchSingle(Requirement("İyi derecede İngilizce", "language", "İngilizce"));
            Assert.Equal("strong", item.Strength);
        }

        [Fact]
        public void Nothing_in_the_vault_is_missing()
        {
            var item = MatchSingle(Requirement("Kubernetes deneyimi", "technical", "Kubernetes"));

            Assert.Equal("missing", item.Strength);
            Assert.Empty(item.Evidence);
        }

        [Fact]
        public void Requirement_asking_for_two_things_is_weak_when_only_one_is_there()
        {
            var item = MatchSingle(Requirement("Docker ve Kubernetes deneyimi", "technical", "Kubernetes", "SQL Server"));
            Assert.Equal("weak", item.Strength);
        }

        [Fact]
        public void Comma_separated_options_need_only_one()
        {
            var item = MatchSingle(Requirement("En az 1 backend dili (C#, Java, Python)", "technical", "C#", "Java", "Python"));
            Assert.Equal("strong", item.Strength);
        }

        [Fact]
        public void Soft_requirement_without_terms_is_not_measured()
        {
            var item = MatchSingle(Requirement("Takım çalışmasına yatkın", "soft"));
            Assert.Equal("unknown", item.Strength);
        }

        [Fact]
        public void Years_requirement_uses_jobs_where_the_term_appears()
        {
            // ASP.NET Core sadece 2024-03'ten beri süren işte geçiyor: 2026-10'a kadar 2 yıl 8 ay.
            var enough = MatchSingle(Requirement("En az 2 yıl ASP.NET Core deneyimi", "technical", "ASP.NET Core"));
            var halfway = MatchSingle(Requirement("En az 5 yıl ASP.NET Core deneyimi", "technical", "ASP.NET Core"));
            var farOff = MatchSingle(Requirement("En az 10 yıl ASP.NET Core deneyimi", "technical", "ASP.NET Core"));

            Assert.Equal("strong", enough.Strength);
            Assert.Equal("Toplam 2 yıl 8 ay", enough.Evidence[0].Text);
            // İstenenin yarısından fazlası zayıf, azı yok sayılıyor.
            Assert.Equal("weak", halfway.Strength);
            Assert.Equal("missing", farOff.Strength);
        }

        [Fact]
        public void General_years_requirement_skips_internships_and_counts_overlaps_once()
        {
            var profile = Profile();
            profile.Experiences.Add(new ExperienceDto { Id = Guid.NewGuid(), Title = "Serbest Geliştirici", Company = "Kendi işim", StartDate = "2025-01", EndDate = "2025-06" });

            var item = MatchSingle(Requirement("En az 3 yıl iş deneyimi", "experience"), profile);

            // Staj (2023) sayılmıyor, 2025'teki serbest iş devam eden işle çakışıyor: toplam yine 2 yıl 8 ay.
            Assert.Equal("Toplam 2 yıl 8 ay", item.Evidence[0].Text);
            Assert.Equal("weak", item.Strength);
        }

        [Fact]
        public void Verdict_counts_only_measurable_must_requirements()
        {
            var analysis = Analysis(
                Requirement("ASP.NET Core bilgisi", "technical", "ASP.NET Core"),
                Requirement("Kubernetes deneyimi", "technical", "Kubernetes"),
                Requirement("İletişimi güçlü", "soft"));

            var result = _matcher.Match(analysis, Profile(), Today);

            Assert.Equal(2, result.MustMeasurable);
            Assert.Equal(1, result.MustStrong);
            Assert.Equal(1, result.MustMissing);
            Assert.StartsWith("Ölçülebilen 2 zorunlu gereksinimin 1'ini", result.Verdict);
        }

        [Fact]
        public void Verdict_says_none_instead_of_zero()
        {
            var result = _matcher.Match(Analysis(Requirement("Kubernetes deneyimi", "technical", "Kubernetes")), Profile(), Today);
            Assert.Equal("Ölçülebilen 1 zorunlu gereksinimin hiçbirini kasandaki bilgilerle kanıtlayamıyorsun.", result.Verdict);
        }
    }
}
