using System.Text.Json;

namespace CvTailor.Api.Services.Ai
{
    // Hangi sağlayıcının kullanılacağına karar veriyor ve yapılandırılmış (JSON) cevapları okuyor.
    // CV ayrıştırma, ilan analizi, yeniden yazım hep buradan geçiyor.
    public class AiService
    {
        // AI:Preferred boşsa bu sırayla, anahtarı tanımlı ilk sağlayıcı.
        private static readonly string[] ProviderOrder = { "anthropic", "openai", "gemini", "deepseek" };

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly IEnumerable<IAiProvider> _providers;
        private readonly string? _preferred;

        public AiService(IEnumerable<IAiProvider> providers, IConfiguration configuration)
        {
            _providers = providers;
            _preferred = configuration["AI:Preferred"];
        }

        // Hiç anahtar yoksa null; çağıran taraf yedek yola düşüyor.
        public IAiProvider? ResolveProvider()
        {
            var configured = _providers.Where(p => p.IsConfigured).ToList();
            return configured.FirstOrDefault(p => p.Key == _preferred)
                ?? ProviderOrder.Select(key => configured.FirstOrDefault(p => p.Key == key)).FirstOrDefault(p => p != null);
        }

        // Cevap T'ye çevrilemezse AiProviderException; ham JSON kullanıcıya hiç gösterilmiyor.
        public async Task<(T Value, AiCompletion Completion)> CompleteJsonAsync<T>(string systemPrompt, string userMessage, CancellationToken cancellationToken) where T : class
        {
            var provider = ResolveProvider() ?? throw new AiProviderException("AI anahtarı tanımlı değil.");
            var completion = await provider.CompleteAsync(systemPrompt, userMessage, cancellationToken, jsonOutput: true);
            return (ParseJson<T>(completion.Text), completion);
        }

        // JSON modu olmayan modeller başına/sonuna açıklama ya da ``` ekleyebiliyor; ilk { ile son } arası.
        public static T ParseJson<T>(string text) where T : class
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) throw new AiProviderException("AI cevabı okunabilir bir formatta dönmedi.");

            try
            {
                return JsonSerializer.Deserialize<T>(text[start..(end + 1)], JsonOptions)
                    ?? throw new AiProviderException("AI boş bir cevap döndürdü.");
            }
            catch (JsonException ex)
            {
                throw new AiProviderException("AI cevabı okunabilir bir formatta dönmedi.", ex);
            }
        }
    }
}
