using CvTailor.Api.Services.Tailoring;
using static CvTailor.Tests.TestData;

namespace CvTailor.Tests
{
    public class FabricationGuardTests
    {
        private readonly FabricationGuard _guard = new(Dictionary);
        private static readonly string[] PostingTerms = { "Docker", "Kubernetes", "Lojistik", "MSSQL" };

        private const string Source = "Raporlama sorgularını SQL Server üzerinde iyileştirerek ekranın açılma süresini kısalttım. Günde 1.500 sipariş işleniyordu.";

        [Fact]
        public void Rewording_with_the_same_facts_is_verified()
        {
            var result = _guard.Check("SQL Server raporlama sorgularını iyileştirerek ekranın açılış süresini kısalttım; günde 1500 sipariş işleniyordu.", Source, PostingTerms);

            Assert.Equal("verified", result.Status);
            Assert.Empty(result.Issues);
        }

        [Fact]
        public void A_new_number_is_flagged()
        {
            var result = _guard.Check("Raporlama sorgularını iyileştirerek ekranın açılma süresini %40 kısalttım.", Source, PostingTerms);

            Assert.Equal("flagged", result.Status);
            Assert.Contains("Kaynakta olmayan rakam: 40", result.Issues);
        }

        [Fact]
        public void A_posting_keyword_that_the_facts_do_not_support_is_flagged()
        {
            var result = _guard.Check("Raporlama sorgularını Docker üzerinde iyileştirdim.", Source, PostingTerms);
            Assert.Contains("Kaynakta olmayan teknoloji ya da terim: Docker", result.Issues);
        }

        [Fact]
        public void Synonyms_and_wider_terms_are_not_fabrication()
        {
            // Kaynak SQL Server diyor, madde ilanın kelimesiyle MSSQL diyor.
            Assert.Equal("verified", _guard.Check("MSSQL raporlama sorgularını iyileştirdim; günde 1.500 sipariş işleniyordu.", Source, PostingTerms).Status);
            // Kaynak GitHub Actions diyor, madde CI/CD diyor.
            Assert.Equal("verified", _guard.Check("GitHub Actions ile CI/CD akışı kurdum.", "Her pull request için GitHub Actions ile build çalıştıran bir akış kurdum.", PostingTerms).Status);
        }

        [Fact]
        public void An_invented_company_or_product_name_is_flagged()
        {
            var result = _guard.Check("Raporlama sorgularını Trendyol entegrasyonu için iyileştirdim.", Source, PostingTerms);
            Assert.Contains("Kaynakta geçmeyen ad: Trendyol", result.Issues);
        }

        [Fact]
        public void Upgrading_the_role_to_leadership_is_flagged()
        {
            var result = _guard.Check("Raporlama ekibini yöneterek sorguları iyileştirdim.", Source, PostingTerms);
            Assert.Contains("Kaynakta olmayan yönetim ya da liderlik iddiası.", result.Issues);
        }

        [Fact]
        public void Leadership_that_is_in_the_source_is_fine()
        {
            var result = _guard.Check("3 kişilik ekibi yöneterek raporlama sorgularını iyileştirdim.", "3 kişilik ekibi yönettim; raporlama sorgularını iyileştirdik.", PostingTerms);
            Assert.Equal("verified", result.Status);
        }
    }
}
