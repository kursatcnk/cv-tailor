using CvTailor.Api.Services.Tailoring;

namespace CvTailor.Tests
{
    // AI'ın kurallara uymadığı durumlarda Clean'in davranışı. AI çağrılmıyor, cevap elle kuruluyor.
    public class BulletRewriterTests
    {
        private static RewriteInput Input() => new()
        {
            TargetTitle = "Junior .NET Developer",
            Requirements = { new RewriteRequirement("r1", "ASP.NET Core bilgisi", "must", "strong") },
            Entries =
            {
                new RewriteInputEntry { Token = "e1", Title = "Yazılım Uzmanı", Facts = { new RewriteFact("f1", "API'yi geliştirdim.", false), new RewriteFact("f2", "Raporları hızlandırdım.", false) } },
                new RewriteInputEntry { Token = "e2", Kind = "project", Title = "Kütüphane", Facts = { new RewriteFact("f3", "Ödünç takibi yaptım.", false) } }
            }
        };

        private static RewriteBullet Bullet(string text, params string[] sources) => new() { Text = text, Sources = sources.ToList(), Requirements = { "r1", "r99" } };

        [Fact]
        public void Bullet_cannot_use_a_fact_from_another_job()
        {
            var output = new RewriteOutput { Entries = { new RewriteEntry { Id = "e1", Bullets = { Bullet("Ödünç takibi.", "f3"), Bullet("API.", "f1") } } } };

            var clean = BulletRewriter.Clean(output, Input());
            var e1 = clean.Entries.Single(e => e.Id == "e1");

            Assert.DoesNotContain(e1.Bullets, b => b.Sources.Contains("f3"));
            Assert.Equal(new[] { "r1" }, e1.Bullets[0].Requirements);
        }

        [Fact]
        public void A_fact_is_used_only_once()
        {
            var output = new RewriteOutput { Entries = { new RewriteEntry { Id = "e1", Bullets = { Bullet("Bir.", "f1"), Bullet("İki.", "f1"), Bullet("Üç.", "f2") } } } };

            var e1 = BulletRewriter.Clean(output, Input()).Entries.Single(e => e.Id == "e1");

            Assert.Equal(2, e1.Bullets.Count);
            Assert.Equal(new[] { "Bir.", "Üç." }, e1.Bullets.Select(b => b.Text));
        }

        [Fact]
        public void Facts_the_ai_skipped_come_back_unchanged()
        {
            var output = new RewriteOutput { Entries = { new RewriteEntry { Id = "e1", Bullets = { Bullet("API'yi ASP.NET Core ile geliştirdim.", "f1") } } } };

            var clean = BulletRewriter.Clean(output, Input());

            Assert.Contains(clean.Entries.Single(e => e.Id == "e1").Bullets, b => b.Text == "Raporları hızlandırdım." && b.Reason == "Değiştirilmedi.");
            Assert.Equal("Ödünç takibi yaptım.", clean.Entries.Single(e => e.Id == "e2").Bullets.Single().Text);
        }

        [Fact]
        public void Summary_without_real_sources_is_dropped()
        {
            var output = new RewriteOutput { Summary = new RewriteSummary { Text = "Deneyimli geliştirici.", Sources = new() { "f99" } } };
            Assert.Null(BulletRewriter.Clean(output, Input()).Summary);
        }
    }
}
