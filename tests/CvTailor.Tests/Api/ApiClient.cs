using System.Net.Http.Json;
using System.Text.Json;
using CvTailor.Api.Dtos;

namespace CvTailor.Tests.Api
{
    // Tarayıcının yaptığını yapan istemci: çerezle oturum, değiştiren her istekte X-CSRF-TOKEN.
    // CSRF token'ı oturuma bağlı; arayüz gibi girişten sonra yeniden alınıyor.
    public sealed class ApiClient
    {
        public const string Password = "Test-sifresi-1234";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public HttpClient Http { get; }
        public UserInfo User { get; private set; } = new();

        public ApiClient(HttpClient http) => Http = http;

        public async Task<ApiClient> RegisterAsync(string? email = null)
        {
            var response = await Http.PostAsJsonAsync("/api/auth/register", new { email = email ?? $"test-{Guid.NewGuid():N}@example.test", password = Password, displayName = "Test" });
            response.EnsureSuccessStatusCode();
            User = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!.User!;
            await RefreshCsrfAsync();
            return this;
        }

        public async Task RefreshCsrfAsync()
        {
            var csrf = await Http.GetFromJsonAsync<JsonElement>("/api/csrf");
            Http.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Http.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        }

        public Task<HttpResponseMessage> GetAsync(string url) => Http.GetAsync(url);
        public Task<HttpResponseMessage> PostAsync(string url, object? body = null) => Http.PostAsJsonAsync(url, body ?? new { });
        public Task<HttpResponseMessage> PutAsync(string url, object body) => Http.PutAsJsonAsync(url, body);
        public Task<HttpResponseMessage> PatchAsync(string url, object body) => Http.PatchAsJsonAsync(url, body);
        public Task<HttpResponseMessage> DeleteAsync(string url) => Http.DeleteAsync(url);

        public async Task<T> GetJsonAsync<T>(string url)
        {
            var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<T>(Json))!;
        }

        public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        {
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<T>(Json))!;
        }
    }
}
