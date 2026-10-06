using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace CvTailor.Api.Services.Ai
{
    // Claude, resmi Anthropic C# SDK'sı ile.
    public class AnthropicProvider : IAiProvider
    {
        private readonly AiProviderOptions _options;
        // Provider singleton; client'ı her çağrıda yeniden kurmak yerine bir kere oluşturup tutuyorum.
        private readonly Lazy<AnthropicClient> _client;

        public AnthropicProvider(IConfiguration configuration)
        {
            _options = configuration.GetSection("AI:Anthropic").Get<AiProviderOptions>() ?? new();
            _client = new Lazy<AnthropicClient>(() => new AnthropicClient { ApiKey = _options.ApiKey });
        }

        public string Key => "anthropic";
        public string DisplayName => "Claude";
        public string Model => string.IsNullOrWhiteSpace(_options.Model) ? "claude-sonnet-5-5" : _options.Model;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

        // Claude'da ayrı bir JSON modu kullanmıyorum; sistem promptu "sadece JSON" diyor, AiService metinden ayıklıyor.
        public async Task<AiCompletion> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken, bool jsonOutput = false)
        {
            try
            {
                BetaMessage response = await _client.Value.Beta.Messages.Create(new MessageCreateParams
                {
                    Model = Model,
                    MaxTokens = 16000,
                    System = systemPrompt,
                    // Güvenlik sınıflandırıcısı reddederse Anthropic isteği kendi seçtiği yedek modelle tekrar deniyor.
                    Betas = ["server-side-fallback-2026-07-01"],
                    Fallbacks = new Default(),
                    Messages = [new() { Role = Role.User, Content = userMessage }],
                }, cancellationToken);

                // Ret durumunda içerik anlamsız olabiliyor, önce stop reason'a bakıyorum.
                if (response.StopReason == "refusal")
                    throw new AiProviderException("Claude bu isteği işlemeyi reddetti.");

                var text = string.Join("\n", response.Content
                    .Select(block => block.Value)
                    .OfType<BetaTextBlock>()
                    .Select(block => block.Text)).Trim();

                if (text.Length == 0)
                    throw new AiProviderException("Claude boş bir cevap döndürdü.");

                return new AiCompletion(text, (int)response.Usage.InputTokens, (int)response.Usage.OutputTokens, response.Model.ToString());
            }
            // Özelden genele; her birinde kullanıcıya söylenecek şey farklı.
            catch (AnthropicUnauthorizedException ex)
            {
                throw new AiProviderException("Claude API anahtarı geçersiz.", ex);
            }
            catch (AnthropicRateLimitException ex)
            {
                throw new AiProviderException("Claude şu an çok yoğun (rate limit). Birazdan tekrar dene.", ex);
            }
            catch (AnthropicApiException ex)
            {
                throw new AiProviderException("Claude isteği tamamlanamadı.", ex);
            }
            catch (AnthropicIOException ex)
            {
                throw new AiProviderException("Claude sunucusuna bağlanılamadı.", ex);
            }
        }
    }
}
