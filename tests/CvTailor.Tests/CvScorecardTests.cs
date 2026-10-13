using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Tailoring;
using static CvTailor.Tests.TestData;

namespace CvTailor.Tests
{
    public class CvScorecardTests
    {
        private static JobAnalysis Posting()
        {
            var analysis = Analysis(
                Requirement("MSSQL bilgisi", "technical", "MSSQL"),
                Requirement("ASP.NET Core ile API geliştirme", "technical", "ASP.NET Core"),
                Requirement("Docker tercih sebebi", "tool", "Docker"),
                Requirement("Kubernetes deneyimi", "tool", "Kubernetes"));
            analysis.Requirements[2].Importance = "nice";
            analysis.Keywords = new() { "MSSQL", "ASP.NET Core", "Docker", "REST API" };
            return analysis;
        }

        [Fact]
        public void Rows_count_keywords_requirements_and_numbers()
        {
            var card = CvScorecard.Compute(Posting(), Dictionary,
                beforeText: "Web servisleri yazdım. SQL Server kullandım.",
                beforeBullets: new[] { "Web servisleri yazdım.", "SQL Server kullandım." },
                afterText: "ASP.NET Core ile REST API geliştirdim. SQL Server sorgularını 3 kat hızlandırdım.",
                afterBullets: new[] { "ASP.NET Core ile REST API geliştirdim.", "SQL Server sorgularını 3 kat hızlandırdım." });

            var keywords = card.Rows.Single(r => r.Label == "İlandaki anahtar kelimeler");
            // MSSQL, ASP.NET Core, Docker, REST API → önce hiçbiri ilandaki yazılışıyla yok, sonra ikisi var.
            Assert.Equal((0, 2, 4), (keywords.Before, keywords.After, keywords.Total!.Value));
            // "SQL Server", MSSQL'in eşdeğeri; insan okuyucu için gereksinim görünüyor ama ATS satırında sayılmıyor.
            var must = card.Rows.Single(r => r.Label == "Zorunlu gereksinimler");
            Assert.Equal((1, 2, 3), (must.Before, must.After, must.Total!.Value));
            var numbers = card.Rows.Single(r => r.Label == "Rakam içeren maddeler");
            Assert.Equal((0, 1), (numbers.Before, numbers.After));
            Assert.Equal(new[] { "Kubernetes deneyimi" }, card.MissingRequirements);
        }

        [Fact]
        public void Option_lists_are_not_counted_as_keywords_when_the_posting_has_its_own()
        {
            var analysis = Analysis(Requirement("En az 1 backend dili (C#, Java, Python)", "technical", "C#", "Java", "Python"));
            analysis.Keywords = new() { "C#", "API" };

            var row = CvScorecard.Compute(analysis, Dictionary, "", Array.Empty<string>(), "C# ile API yazdım.", Array.Empty<string>()).Rows[0];
            Assert.Equal((2, 2), (row.After, row.Total!.Value));
        }

        [Fact]
        public void Bullet_lines_are_read_from_the_uploaded_text()
        {
            var lines = CvScorecard.BulletLines("Deniz Aksoy\nDENEYİM\n• Depo takip sisteminin API'sini geliştirdim.\n- 3 kişilik ekibe teknik liderlik yaptım.\n• Kısa\nC#, SQL");
            Assert.Equal(new[] { "Depo takip sisteminin API'sini geliştirdim.", "3 kişilik ekibe teknik liderlik yaptım." }, lines);
        }

        [Fact]
        public void Printed_text_follows_decisions()
        {
            var cv = CvPdfRendererTests.Cv();

            var text = CvScorecard.PrintedText(cv);
            Assert.Contains("ASP.NET Core ile geliştirdim", text);
            Assert.Contains("Orijinal madde.", text);
            Assert.DoesNotContain("%90", text);
            Assert.Equal(3, CvScorecard.PrintedBullets(cv).Count);
        }
    }
}
