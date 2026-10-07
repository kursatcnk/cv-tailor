using CvTailor.Api.Services.Targets;

namespace CvTailor.Tests
{
    // Meslek profilleri elle yazılıyor; yazım hatası bir gereksinimi sessizce kaybettirmesin.
    public class ProfessionCatalogTests
    {
        private static readonly ProfessionCatalog Catalog =
            ProfessionCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Knowledge", "professions"));

        public static TheoryData<string, string> Cases()
        {
            var data = new TheoryData<string, string>();
            foreach (var profession in Catalog.List())
                foreach (var level in ProfessionCatalog.SeniorityLabels.Keys)
                    data.Add(profession.Key, level);
            return data;
        }

        [Fact]
        public void All_five_professions_load()
        {
            Assert.Equal(5, Catalog.List().Count);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Every_level_has_enough_valid_requirements(string key, string level)
        {
            var profile = Catalog.Find(key)!;
            var analysis = ProfessionCatalog.BuildAnalysis(profile, level);

            Assert.True(analysis.Requirements.Count(r => r.Importance == "must") >= 4, $"{key}/{level}: en az 4 zorunlu gereksinim olmalı");
            // Normalize bilinmeyen kategoriyi "other"a çeviriyor; dosyada "other" yazmayan her şey korunmuş olmalı.
            var written = profile.Requirements.Where(r => r.Levels == null || r.Levels.Contains(level)).Select(r => r.Category).ToList();
            Assert.All(written, c => Assert.Contains(c, JobAnalyzer.Categories));
            Assert.Equal(written.Count, analysis.Requirements.Count);
        }

        [Fact]
        public void Seniority_filters_requirements()
        {
            var backend = Catalog.Find("backend-developer")!;
            var junior = ProfessionCatalog.BuildAnalysis(backend, "junior").Requirements.Select(r => r.Text).ToList();
            var senior = ProfessionCatalog.BuildAnalysis(backend, "senior").Requirements.Select(r => r.Text).ToList();

            Assert.DoesNotContain("En az 5 yıl backend geliştirme deneyimi", junior);
            Assert.Contains("En az 5 yıl backend geliştirme deneyimi", senior);
        }

        [Fact]
        public void Every_profession_teaches_with_tips_and_examples()
        {
            foreach (var summary in Catalog.List())
            {
                var profile = Catalog.Find(summary.Key)!;
                Assert.True(profile.Tips.Count >= 3, summary.Key);
                Assert.NotEmpty(profile.Examples);
                Assert.All(profile.Examples, e =>
                {
                    Assert.NotEmpty(e.Bad);
                    Assert.NotEmpty(e.Good);
                    Assert.NotEmpty(e.Why);
                });
            }
        }
    }
}
