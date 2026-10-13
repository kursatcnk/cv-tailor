using System.Net;
using System.Text.Json;
using CvTailor.Api.Dtos;
using CvTailor.Api.Services;
using CvTailor.Api.Services.Cv;
using CvTailor.Api.Services.Tailoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvTailor.Tests.Api
{
    // Uygulamanın en riskli yerleri uçtan uca: oturum, CSRF, kullanıcıların birbirinin verisini görememesi,
    // aylık kotanın aynı anda gelen isteklerle aşılamaması, hesap silmenin her şeyi silmesi.
    // AI anahtarı yok: CV hazırlama yedek motorla çalışıyor (maddeler seçilip sıralanıyor, yeniden yazılmıyor).
    [Collection(ApiCollection.Name)]
    public class ApiTests
    {
        private readonly ApiFactory _factory;

        public ApiTests(ApiFactory factory) => _factory = factory;

        // Kasa (proje maddesi dahil, hesap silmedeki eski hatanın sebebi), meslek hedefi, CV ve CV yükleme kaydı.
        private static async Task<(TargetDto Target, TailoredCvDto Cv)> CreateDataAsync(ApiClient client, string name)
        {
            var vault = await client.PutAsync("/api/vault", new
            {
                fullName = name,
                email = $"{name.Replace(' ', '.').ToLowerInvariant()}@ornek.test",
                experiences = new[]
                {
                    new { company = "Kuzey Ticaret", title = "Yazılım Geliştirici", startDate = "2022-03", isCurrent = true,
                          achievements = new[] { new { text = "Sipariş servisini ASP.NET Core ile 3 ayda yeniden yazdım." } } }
                },
                projects = new[] { new { name = "Kütüphane Takip", achievements = new[] { new { text = "React ile ödünç takip ekranlarını geliştirdim." } } } },
                skills = new[] { new { name = "C#", category = "technical" } }
            });
            vault.EnsureSuccessStatusCode();
            var imported = await client.PostAsync("/api/cv/import-text", new { text = $"{name}\nYazılım Geliştirici\n{new string('-', 20)}\nSipariş servisini ASP.NET Core ile yeniden yazdım. Kütüphane takip uygulaması geliştirdim." });
            imported.EnsureSuccessStatusCode();
            var target = await ApiClient.ReadAsync<TargetDto>(await client.PostAsync("/api/targets/profession", new { professionKey = "backend-developer", seniority = "mid" }));
            var cv = await ApiClient.ReadAsync<TailoredCvDto>(await client.PostAsync("/api/cvs", new { targetId = target.Id }));
            return (target, cv);
        }

        [Fact]
        public async Task Api_requires_sign_in()
        {
            var anonymous = _factory.Client();

            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/vault")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/targets")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/account/export")).StatusCode);
        }

        [Fact]
        public async Task Changing_requests_need_the_csrf_token()
        {
            var client = await _factory.Client().RegisterAsync();
            client.Http.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");

            var rejected = await client.PutAsync("/api/vault", new { fullName = "Deniz" });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Contains("csrf_invalid", await rejected.Content.ReadAsStringAsync());

            await client.RefreshCsrfAsync();
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsync("/api/vault", new { fullName = "Deniz" })).StatusCode);
        }

        [Fact]
        public async Task Users_cannot_reach_each_others_data()
        {
            var owner = await _factory.Client().RegisterAsync();
            var (target, cv) = await CreateDataAsync(owner, "Ayşe Sahibi");
            var other = await _factory.Client().RegisterAsync();

            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/targets/{target.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/targets/{target.Id}/match")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/cvs/{cv.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/cvs/{cv.Id}/pdf")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/cvs/{cv.Id}/docx")).StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, (await other.PatchAsync($"/api/cvs/{cv.Id}/changes/{cv.Changes[0].Id}", new { decision = "rejected" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/targets/{target.Id}")).StatusCode);
            Assert.Empty(await other.GetJsonAsync<List<TargetSummaryDto>>("/api/targets"));
            Assert.False((await other.GetJsonAsync<VaultResponse>("/api/vault")).Exists);

            // Sahibinin verisi yerinde.
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/cvs/{cv.Id}")).StatusCode);
        }

        [Fact]
        public async Task Monthly_quota_holds_under_concurrent_requests()
        {
            var client = await _factory.Client().RegisterAsync();
            var limit = UsageService.GetPlan("free").MonthlyLimit;

            // Kota kontrolü ve kayıt ayrı adımlar; kilit olmasa aynı anda gelen istekler hepsi "hak var" görüp geçerdi.
            var attempts = Enumerable.Range(0, limit + 15).Select(async _ =>
            {
                using var scope = _factory.Services.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<UsageService>().TryReserveAsync(client.User.Id, "free", "rewrite");
            });
            var results = await Task.WhenAll(attempts);

            Assert.Equal(limit, results.Count(r => r != null));
            await using var db = _factory.Db();
            Assert.Equal(limit, await db.UsageTracking.CountAsync(u => u.UserId == client.User.Id));
        }

        [Fact]
        public async Task Deleting_the_account_removes_everything()
        {
            var client = await _factory.Client().RegisterAsync();
            await CreateDataAsync(client, "Silinecek Kişi");
            var userId = client.User.Id;

            var deleted = await client.PostAsync("/api/account/delete", new { password = ApiClient.Password });
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

            await using var db = _factory.Db();
            Assert.False(await db.Users.AnyAsync(u => u.Id == userId));
            Assert.False(await db.CareerProfiles.AnyAsync(p => p.UserId == userId));
            Assert.False(await db.Achievements.AnyAsync(a => a.Experience!.Profile!.UserId == userId || a.Project!.Profile!.UserId == userId));
            Assert.False(await db.JobTargets.AnyAsync(t => t.UserId == userId));
            Assert.False(await db.TailoredCvs.AnyAsync(c => c.JobTarget!.UserId == userId));
            Assert.False(await db.CvImports.AnyAsync(i => i.UserId == userId));
            // Oturum çerezi de gitti.
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/vault")).StatusCode);
        }

        [Fact]
        public async Task Export_contains_only_the_users_own_data()
        {
            var client = await _factory.Client().RegisterAsync();
            var (target, cv) = await CreateDataAsync(client, "Deniz Kendisi");
            var other = await _factory.Client().RegisterAsync();
            await CreateDataAsync(other, "Başka Biri");

            var response = await client.GetAsync("/api/account/export");
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.StartsWith("attachment", response.Content.Headers.ContentDisposition?.ToString());
            var json = await response.Content.ReadAsStringAsync();
            using var export = JsonDocument.Parse(json);
            var root = export.RootElement;

            Assert.Equal("Deniz Kendisi", root.GetProperty("vault").GetProperty("fullName").GetString());
            Assert.Equal(target.Id, root.GetProperty("targets")[0].GetProperty("id").GetGuid());
            Assert.Equal(cv.Id, root.GetProperty("targets")[0].GetProperty("cvs")[0].GetProperty("id").GetGuid());
            Assert.Equal(1, root.GetProperty("imports").GetArrayLength());
            Assert.DoesNotContain("Başka Biri", json);
            Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Generated_cv_downloads_and_passes_the_ats_test()
        {
            var client = await _factory.Client().RegisterAsync();
            var (_, cv) = await CreateDataAsync(client, "Deniz Aksoy");

            var pdf = await client.GetAsync($"/api/cvs/{cv.Id}/pdf");
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));
            var docx = await client.GetAsync($"/api/cvs/{cv.Id}/docx");
            Assert.Equal("PK", System.Text.Encoding.ASCII.GetString((await docx.Content.ReadAsByteArrayAsync())[..2]));
            Assert.True((await client.GetJsonAsync<AtsReport>($"/api/cvs/{cv.Id}/ats")).Passed);
        }

        [Fact]
        public async Task Sign_in_attempts_are_rate_limited_per_address()
        {
            var client = _factory.Client();
            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < 12; i++)
                statuses.Add((await client.PostAsync("/api/auth/login", new { email = "yok@example.test", password = "yanlis-sifre" })).StatusCode);

            Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
            Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
            // Başka bir adresten gelen kullanıcı etkilenmiyor.
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.Client().PostAsync("/api/auth/login", new { email = "yok@example.test", password = "yanlis-sifre" })).StatusCode);
        }
    }
}
