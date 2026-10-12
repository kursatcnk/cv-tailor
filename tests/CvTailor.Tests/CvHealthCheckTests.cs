using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Quality;

namespace CvTailor.Tests
{
    public class CvHealthCheckTests
    {
        private static readonly DateTime Today = new(2026, 10, 7);

        private static ExperienceDto Job(string company, string? start, string? end, bool current = false, string? type = null, params string[] bullets) => new()
        {
            Company = company,
            Title = "Backend Developer",
            StartDate = start,
            EndDate = end,
            IsCurrent = current,
            EmploymentType = type,
            Achievements = bullets.Select(b => new AchievementDto { Text = b }).ToList()
        };

        private static List<string> Codes(ProfileDto profile) => CvHealthCheck.Check(profile, Today).Issues.Select(i => i.Code).ToList();

        [Fact]
        public void Clean_profile_has_no_issues()
        {
            var profile = new ProfileDto
            {
                Email = "ayse.yilmaz@example.com",
                Experiences =
                {
                    Job("Kuzey Yazılım", "2023-05", null, current: true, bullets: new[] { "Sipariş servisini 3 ayda yeniden yazdım.", "12 kişilik ekipte kod incelemesi yaptım." }),
                    Job("Mavi Lojistik", "2021-02", "2023-04", bullets: new[] { "Raporlama süresini %40 kısalttım.", "4 entegrasyonu tamamladım." })
                }
            };

            Assert.Empty(CvHealthCheck.Check(profile, Today).Issues);
        }

        [Fact]
        public void Reversed_and_future_dates_are_errors()
        {
            var profile = new ProfileDto
            {
                Email = "a@example.com",
                Experiences = { Job("A", "2024-06", "2023-01", bullets: "1 iş"), Job("B", "2027-01", null, current: true, bullets: "1 iş") }
            };

            var codes = Codes(profile);
            Assert.Contains("end-before-start", codes);
            Assert.Contains("future", codes);
        }

        [Fact]
        public void Gap_of_six_months_or_more_is_reported_with_both_jobs()
        {
            var profile = new ProfileDto
            {
                Email = "a@example.com",
                Experiences = { Job("Yeni", "2024-03", null, current: true, bullets: "1 iş"), Job("Eski", "2021-01", "2023-06", bullets: "1 iş") }
            };

            var gap = Assert.Single(CvHealthCheck.Check(profile, Today).Issues, i => i.Code == "gap");
            Assert.Equal("8 aylık boşluk", gap.Message);
            Assert.Contains("Eski", gap.Hint);
        }

        [Fact]
        public void Short_gap_and_internship_overlap_are_fine()
        {
            var profile = new ProfileDto
            {
                Email = "a@example.com",
                Experiences =
                {
                    Job("Yeni", "2023-09", null, current: true, bullets: "1 iş"),
                    Job("Eski", "2021-01", "2023-06", bullets: "1 iş"),
                    Job("Staj", "2022-06", "2022-09", type: "internship", bullets: "1 iş")
                }
            };

            var codes = Codes(profile);
            Assert.DoesNotContain("gap", codes);
            Assert.DoesNotContain("overlap", codes);
        }

        [Fact]
        public void Two_full_time_jobs_at_the_same_time_are_flagged()
        {
            var profile = new ProfileDto
            {
                Email = "a@example.com",
                // "06/2022" formdan elle yazılmış biçim; yine de okunmalı.
                Experiences = { Job("A", "2021-01", "2023-01", bullets: "1 iş"), Job("B", "06/2022", "2024-01", bullets: "1 iş") }
            };

            Assert.Contains("overlap", Codes(profile));
        }

        [Fact]
        public void Cliches_weak_verbs_and_casual_email_are_flagged()
        {
            var profile = new ProfileDto
            {
                Email = "prenses_1995@example.com",
                Summary = "Takım çalışmasına yatkın, sonuç odaklı bir geliştiriciyim.",
                Experiences = { Job("A", "2022-01", null, current: true, bullets: new[] { "Ödeme projesinde görev aldım.", "Raporlamadan sorumluydum." }) }
            };

            var report = CvHealthCheck.Check(profile, Today);
            Assert.Contains(report.Issues, i => i.Code == "email");
            Assert.Equal(2, report.Issues.Count(i => i.Code == "cliche" && i.Path == "summary"));
            Assert.Contains(report.Issues, i => i.Code == "weak-verb" && i.Path == "experiences.0.achievements.0");
            Assert.Contains(report.Issues, i => i.Code == "weak-verb" && i.Path == "experiences.0.achievements.1");
        }

        [Fact]
        public void Bullets_without_numbers_are_counted_once()
        {
            var profile = new ProfileDto
            {
                Email = "a@example.com",
                Experiences = { Job("A", "2022-01", null, current: true, bullets: new[] { "API yazdım.", "Testleri kurdum.", "Arayüzü yeniledim.", "2 servisi birleştirdim." }) }
            };

            var report = CvHealthCheck.Check(profile, Today);
            Assert.Equal(4, report.Bullets);
            Assert.Equal(1, report.BulletsWithNumbers);
            Assert.Single(report.Issues, i => i.Code == "few-numbers");
            Assert.DoesNotContain(report.Issues, i => i.Code == "no-number");
        }

        [Fact]
        public void Number_answers_are_not_counted_as_separate_bullets()
        {
            var bullet = new AchievementDto { Id = Guid.NewGuid(), Text = "Raporlama ekranını yeniledim." };
            var answer = new AchievementDto { Text = "Rapor süresi 10 dakikadan 2 dakikaya indi", Source = "interview", RelatedId = bullet.Id };
            var job = Job("A", "2022-01", null, current: true);
            job.Achievements = new() { bullet, answer };

            Assert.Equal(1, CvHealthCheck.Check(new ProfileDto { Email = "a@example.com", Experiences = { job } }, Today).Bullets);
        }

        [Fact]
        public void Mixed_moods_in_one_job_are_reported()
        {
            var profile = new ProfileDto
            {
                Email = "a@example.com",
                Experiences = { Job("A", "2022-01", null, current: true, bullets: new[] { "3 servis geliştirdim.", "2 rapor ekranı hazırlandı." }) }
            };

            Assert.Contains("mood", Codes(profile));
        }

        [Fact]
        public void Null_lists_from_the_form_do_not_crash()
        {
            var profile = new ProfileDto { Email = "a@example.com", Experiences = null!, Projects = null! };
            Assert.Empty(CvHealthCheck.Check(profile, Today).Issues);
        }

        [Theory]
        [InlineData("API'yi geliştirdim", "birinci")]
        [InlineData("API geliştirildi.", "edilgen")]
        [InlineData("Ödeme ekranının tasarlanması", "isim")]
        [InlineData("Raporlama", "isim")]
        public void Mood_reads_the_last_word(string text, string mood) =>
            Assert.Equal(mood, BulletLinter.Mood(text));

        [Fact]
        public void Linter_explains_each_issue()
        {
            var issues = BulletLinter.Lint("Ben çeşitli projelerde yer aldım");
            Assert.Contains(issues, i => i.Code == "pronoun");
            Assert.Equal(2, issues.Count(i => i.Code == "weak-verb"));
            Assert.Contains(issues, i => i.Code == "no-number");
            Assert.All(issues, i => Assert.False(string.IsNullOrWhiteSpace(i.Hint)));
        }
    }
}
