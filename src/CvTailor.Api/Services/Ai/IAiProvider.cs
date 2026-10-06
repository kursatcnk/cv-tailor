namespace CvTailor.Api.Services.Ai
{
    // Her sağlayıcı bu arayüzü uyguluyor; AiService karşısındakinin Claude mu GPT mi olduğunu bilmiyor.
    public interface IAiProvider
    {
        // appsettings'teki bölüm adı: anthropic, openai, gemini, deepseek
        string Key { get; }
        string DisplayName { get; }
        string Model { get; }

        // Anahtar yoksa bu sağlayıcı hiç seçilmiyor.
        bool IsConfigured { get; }

        // jsonOutput: sağlayıcı destekliyorsa JSON modunu açıyor (CV ayrıştırma, ilan analizi gibi yapılandırılmış cevaplar için).
        Task<AiCompletion> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken, bool jsonOutput = false);
    }

    public record AiCompletion(string Text, int InputTokens, int OutputTokens, string Model);

    // Mesajı direkt kullanıcıya gösteriliyor; o yüzden Türkçe ve içinde anahtar/ham hata detayı yok.
    public class AiProviderException : Exception
    {
        public AiProviderException(string message, Exception? inner = null) : base(message, inner) { }
    }

    public class AiProviderOptions
    {
        public string? ApiKey { get; set; }
        public string? Model { get; set; }
    }
}
