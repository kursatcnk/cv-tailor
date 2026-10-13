using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Ai;
using CvTailor.Api.Services.Privacy;
using CvTailor.Api.Services.Tailoring;
using Microsoft.Extensions.Configuration;

namespace CvTailor.Tests
{
    // Gerçek bir AI çağrısı yok: sahte sağlayıcı kendisine gönderilen metni kaydediyor ve hazır bir cevap dönüyor.
    // Böylece "AI'a kişisel veri gitmiyor" iddiası testle kanıtlanıyor.
    public class RewritePrivacyTests
    {
        internal sealed class RecordingProvider : IAiProvider
        {
            public string? LastMessage { get; private set; }
            public string Reply { get; set; } = "{}";
            public string Key => "fake";
            public string DisplayName => "Sahte";
            public string Model => "sahte-model";
            public bool IsConfigured => true;

            public Task<AiCompletion> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken, bool jsonOutput = false)
            {
                LastMessage = userMessage;
                return Task.FromResult(new AiCompletion(Reply, 10, 10, Model));
            }
        }

        private static (BulletRewriter Rewriter, RecordingProvider Provider) Create()
        {
            var provider = new RecordingProvider();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AI:Preferred"] = "fake" }).Build();
            return (new BulletRewriter(new AiService(new[] { provider }, config)), provider);
        }

        private static readonly ProfileDto Profile = new() { FullName = "Deniz Aksoy", Email = "deniz.aksoy@ornek.test", Phone = "0555 000 00 00" };

        private static RewriteInput Input() => new()
        {
            TargetTitle = "Backend Developer",
            Summary = "Deniz Aksoy, backend geliştirici. İletişim: deniz.aksoy@ornek.test",
            Entries =
            {
                new RewriteInputEntry
                {
                    Token = "e1", Title = "Yazılım Uzmanı", Subtitle = "Örnek Lojistik",
                    Facts = { new RewriteFact("f1", "Müşteriler sorunları 0555 000 00 00 numarasından bana (Deniz) iletiyordu; destek talebi sistemini kurdum.", false) }
                }
            }
        };

        [Fact]
        public async Task Personal_data_never_reaches_the_ai()
        {
            var (rewriter, provider) = Create();
            await rewriter.RewriteAsync(Input(), PiiMask.ForProfile(Profile), CancellationToken.None);

            Assert.NotNull(provider.LastMessage);
            Assert.DoesNotContain("Deniz", provider.LastMessage);
            Assert.DoesNotContain("Aksoy", provider.LastMessage);
            Assert.DoesNotContain("ornek.test", provider.LastMessage);
            Assert.DoesNotContain("0555", provider.LastMessage);
            // İş bilgisi gidiyor; AI'ın yeniden yazacağı şey bu.
            Assert.Contains("destek talebi sistemini kurdum", provider.LastMessage);
        }

        [Fact]
        public async Task Placeholders_in_the_answer_are_turned_back_into_real_values()
        {
            var (rewriter, provider) = Create();
            // AI maskeli metni gördü; cevabında yer tutucuyu kullanıyor.
            provider.Reply = """
                { "summary": null,
                  "entries": [ { "id": "e1", "bullets": [ { "text": "[TELEFON_1] hattından gelen talepler için destek sistemi kurdum.", "sources": ["f1"], "requirements": [], "alternatives": [], "reason": "Netleştirdim." } ] } ] }
                """;

            var (output, _) = await rewriter.RewriteAsync(Input(), PiiMask.ForProfile(Profile), CancellationToken.None);

            Assert.Equal("0555 000 00 00 hattından gelen talepler için destek sistemi kurdum.", output.Entries.Single().Bullets.Single().Text);
        }

        [Fact]
        public async Task Facts_the_ai_dropped_come_back_unmasked()
        {
            var (rewriter, _) = Create();
            var (output, _) = await rewriter.RewriteAsync(Input(), PiiMask.ForProfile(Profile), CancellationToken.None);

            // AI hiçbir şey dönmedi; madde orijinal (maskesiz) hâliyle geri geliyor.
            Assert.Contains("(Deniz)", output.Entries.Single().Bullets.Single().Text);
        }
    }
}
