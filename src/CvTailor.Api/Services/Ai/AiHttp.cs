using System.Net;

namespace CvTailor.Api.Services.Ai
{
    // Sağlayıcılara giden HTTP istekleri. Geçici hatada (5xx, bağlantı kopması) kısa bekleyip tekrar deniyor;
    // çoğu birkaç saniyede düzeliyor ve ilk hatada yerel motora düşmek gereksiz.
    // 429'u tekrar denemiyorum: dolmuş kotayı zorlamak sadece yeni bir 429 getiriyor. 401/404 zaten kalıcı.
    public static class AiHttp
    {
        // 1 sn, sonra 3 sn. En kötü ihtimalle ~4 sn ek bekleme.
        private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3) };

        private static bool IsTransient(HttpStatusCode status) =>
            status is HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

        // HttpRequestMessage ikinci kez gönderilemiyor, her denemede createRequest ile yenisini kuruyorum.
        public static async Task<HttpResponseMessage> SendWithRetryAsync(HttpClient client, Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                var lastAttempt = attempt == RetryDelays.Length;
                try
                {
                    using var request = createRequest();
                    var response = await client.SendAsync(request, cancellationToken);
                    if (lastAttempt || !IsTransient(response.StatusCode)) return response;
                    response.Dispose();
                }
                catch (HttpRequestException) when (!lastAttempt)
                {
                    // bağlantı koptu, aşağıda bekleyip tekrar
                }
                await Task.Delay(RetryDelays[attempt], cancellationToken);
            }
        }
    }
}
