using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Matching;

namespace CvTailor.Tests
{
    // Testlerde kullanılan kurgusal kasa ve ilan parçaları.
    internal static class TestData
    {
        public static readonly SkillDictionary Dictionary =
            SkillDictionary.Load(Path.Combine(AppContext.BaseDirectory, "Knowledge", "synonyms.json"));

        public static readonly DateTime Today = new(2026, 10, 1);

        public static ProfileDto Profile() => new()
        {
            FullName = "Deniz Aksoy",
            Headline = "Backend Geliştirici",
            Experiences =
            {
                new ExperienceDto
                {
                    Id = Guid.NewGuid(), Title = "Yazılım Uzmanı", Company = "Örnek Lojistik", StartDate = "2024-03", IsCurrent = true,
                    Achievements =
                    {
                        new AchievementDto { Id = Guid.NewGuid(), Text = "Depo takip sisteminin API'sini ASP.NET Core ile geliştirdim." },
                        new AchievementDto { Id = Guid.NewGuid(), Text = "Raporlama sorgularını SQL Server üzerinde iyileştirdim." }
                    }
                },
                new ExperienceDto
                {
                    Id = Guid.NewGuid(), Title = "Stajyer Geliştirici", Company = "Deneme Yazılım", EmploymentType = "internship",
                    StartDate = "2023-06", EndDate = "2023-09",
                    Achievements = { new AchievementDto { Id = Guid.NewGuid(), Text = "Web sitesi geliştirmelerinde görev aldım." } }
                }
            },
            Educations = { new EducationDto { Id = Guid.NewGuid(), School = "Ege Örnek Üniversitesi", Field = "Bilgisayar Mühendisliği", Degree = "Lisans" } },
            Skills =
            {
                new SkillDto { Id = Guid.NewGuid(), Name = "Docker", Category = "tool" },
                new SkillDto { Id = Guid.NewGuid(), Name = "İngilizce", Category = "language", Level = "B2" }
            }
        };

        public static JobRequirement Requirement(string text, string category, params string[] terms) =>
            new() { Key = "r1", Text = text, Importance = "must", Category = category, Terms = terms.ToList() };

        public static JobAnalysis Analysis(params JobRequirement[] requirements)
        {
            for (var i = 0; i < requirements.Length; i++) requirements[i].Key = $"r{i + 1}";
            return new JobAnalysis { Requirements = requirements.ToList() };
        }
    }
}
