using System.Net;
using CvTailor.Api.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvTailor.Tests.Api
{
    // Uygulamanın kendisi, gerçek SQL Server'la (LocalDB) ve her çalıştırmada sıfırdan oluşan bir veritabanıyla.
    // Kota kilidi (sp_getapplock), cascade silme ve migration'lar SQL Server'a özel; bellek içi veritabanı bunları test edemez.
    // Ortam "Testing": geliştirme ortamındaki user secrets (AI anahtarları) yüklenmiyor, testler gerçek AI çağırmıyor.
    public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly string _database = $"CvTailorTests_{Guid.NewGuid():N}";
        private int _nextIp;

        private string ConnectionString => $@"Server=(localdb)\MSSQLLocalDB;Database={_database};Trusted_Connection=True;TrustServerCertificate=True;";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
            builder.UseSetting("Jwt:Secret", "test-only-secret-that-is-long-enough-for-hs256-0123456789");
            foreach (var provider in new[] { "Anthropic", "OpenAI", "Gemini", "DeepSeek" })
                builder.UseSetting($"AI:{provider}:ApiKey", "");
            builder.UseSetting("Email:Smtp:Host", "");

            // Giriş istek sınırı IP başına. Test sunucusunda IP yok, bütün testler aynı kovaya düşerdi;
            // her istemci kendi IP'sini başlıkla söylüyor.
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, TestIpStartupFilter>());
        }

        // Her test kullanıcısı ayrı bir "makineden" geliyor.
        public ApiClient Client()
        {
            var http = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            var n = Interlocked.Increment(ref _nextIp);
            http.DefaultRequestHeaders.Add(TestIpStartupFilter.Header, $"10.0.{n / 250}.{n % 250 + 1}");
            return new ApiClient(http);
        }

        public CvTailorDbContext Db() => Services.CreateScope().ServiceProvider.GetRequiredService<CvTailorDbContext>();

        public Task InitializeAsync() => Task.CompletedTask;

        async Task IAsyncLifetime.DisposeAsync()
        {
            // Test veritabanı makinede kalmasın.
            await using var db = new CvTailorDbContext(new DbContextOptionsBuilder<CvTailorDbContext>().UseSqlServer(ConnectionString).Options);
            await db.Database.EnsureDeletedAsync();
            await base.DisposeAsync();
        }

        private sealed class TestIpStartupFilter : IStartupFilter
        {
            public const string Header = "X-Test-Ip";

            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (IPAddress.TryParse(context.Request.Headers[Header], out var ip)) context.Connection.RemoteIpAddress = ip;
                    await nextMiddleware();
                });
                next(app);
            };
        }
    }

    [CollectionDefinition(Name)]
    public class ApiCollection : ICollectionFixture<ApiFactory>
    {
        public const string Name = "api";
    }
}
