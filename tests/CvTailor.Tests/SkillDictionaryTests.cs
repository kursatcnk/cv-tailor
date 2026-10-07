using CvTailor.Api.Services.Matching;

namespace CvTailor.Tests
{
    public class SkillDictionaryTests
    {
        private static readonly SkillDictionary Dictionary = TestData.Dictionary;

        [Theory]
        [InlineData("Raporları Excel'de hazırladım.", "Excel")]
        [InlineData("C#'ta servis yazdım.", "C#")]
        [InlineData(".NET 8 ile API geliştirdim.", ".NET 8")]
        [InlineData("Projeleri Git ile yönettim.", "Git")]
        public void Finds_proper_terms_with_apostrophe_suffixes(string text, string term) =>
            Assert.True(Dictionary.Contains(text, term, strict: true));

        [Theory]
        [InlineData("Toplantıya gitti.", "Git")]
        [InlineData("Excellent communication skills", "Excel")]
        [InlineData("C++ ile oyun yaptım.", "C")]
        public void Strict_terms_do_not_match_inside_other_words(string text, string term) =>
            Assert.False(Dictionary.Contains(text, term, strict: true));

        [Theory]
        [InlineData("Muhasebede 3 yıl çalıştım.", "muhasebe")]
        [InlineData("İngilizceyi iş hayatında kullandım.", "İngilizce")]
        [InlineData("INGILIZCE: ileri seviye", "İngilizce")]
        [InlineData("Bilgisayar   Mühendisliği bölümü", "Bilgisayar Mühendisliği")]
        public void Loose_terms_match_turkish_suffixes_and_dotless_i(string text, string term) =>
            Assert.True(Dictionary.Contains(text, term, strict: false));

        [Fact]
        public void Synonyms_are_evidence_for_each_other()
        {
            var terms = Dictionary.EvidenceTerms("MSSQL");
            Assert.Contains("SQL Server", terms);
            Assert.Contains("T-SQL", terms);
        }

        [Fact]
        public void Wider_terms_count_as_evidence_but_not_the_other_way()
        {
            // GitHub Actions kullanan CI/CD de biliyordur; CI/CD bilen GitHub Actions biliyor demek değil.
            Assert.Contains("GitHub Actions", Dictionary.EvidenceTerms("CI/CD"));
            Assert.DoesNotContain("CI/CD", Dictionary.EvidenceTerms("GitHub Actions"));
        }

        [Fact]
        public void Implied_terms_follow_chains()
        {
            // Next.js → React → JavaScript
            Assert.Contains("Next.js", Dictionary.EvidenceTerms("JavaScript"));
        }

        [Fact]
        public void Unknown_terms_are_searched_as_written()
        {
            var terms = Dictionary.EvidenceTerms("Depo yönetimi");
            Assert.Equal(new[] { "Depo yönetimi" }, terms);
        }
    }
}
