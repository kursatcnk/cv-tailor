using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using CvTailor.Api.Data;
using CvTailor.Api.Services;
using CvTailor.Api.Services.Ai;
using CvTailor.Api.Services.Cv;
using CvTailor.Api.Services.Matching;
using CvTailor.Api.Services.Interview;
using CvTailor.Api.Services.Tailoring;
using CvTailor.Api.Services.Targets;

var builder = WebApplication.CreateBuilder(args);

// appsettings.json'daki örnek anahtar GitHub'da açık duruyor, canlıda onunla ayağa kalkmasın.
// HS256 en az 32 byte istiyor; kısa anahtarda hata ilk girişte değil burada çıksın.
const string SampleJwtSecret = "your-super-secret-key-minimum-32-characters-long-here-12345678";
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "";
if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
    throw new InvalidOperationException("Jwt:Secret en az 32 karakter olmalı.");
if (!builder.Environment.IsDevelopment() && jwtSecret == SampleJwtSecret)
    throw new InvalidOperationException("Canlı ortamda Jwt:Secret değiştirilmeli (user secrets veya appsettings.Production.json).");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Swagger'da Authorize butonu çıksın. Arayüz çerezle çalışıyor; bu tanım Bearer başlığı kullanan istemciler için.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT (başına Bearer yazmadan). Tarayıcıdan girişte token cvt.auth çerezinde durur."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
        // Arayüz token'ı httpOnly çerezde taşıyor; Authorization başlığı gelmişse (Swagger, dış istemci) o öncelikli.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token) && !context.Request.Headers.ContainsKey("Authorization"))
                    context.Token = context.Request.Cookies[AuthCookie.Name];
                return Task.CompletedTask;
            }
        };
    });

// Çerezle giriş yapınca tarayıcı çerezi her isteğe kendisi ekliyor; başka bir siteden tetiklenen
// isteklere karşı değiştiren her istekte X-CSRF-TOKEN başlığı aranıyor (aşağıdaki middleware).
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "cvt.csrf";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Giriş/kayıt/şifre sıfırlama: IP başına dakikada 10. Brute force ve mail bombardımanı için.
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));

    // AI çağrıları: aylık kota zaten var ama birinin 40 hakkı 10 saniyede yakmasını da istemiyorum.
    options.AddPolicy("ai", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddDbContext<CvTailorDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"), sql =>
        sql.EnableRetryOnFailure(maxRetryCount: 5)));

// Antiforgery token'larını imzalıyor. Anahtarları DB'de tutuyorum; paylaşımlı hosting'te
// uygulama yeniden başlayınca anahtar kaybolursa açık sekmelerdeki her istek CSRF hatası veriyor.
builder.Services.AddDataProtection()
    .SetApplicationName("CvTailor")
    .PersistKeysToDbContext<CvTailorDbContext>();

builder.Services.AddHealthChecks().AddDbContextCheck<CvTailorDbContext>("database");

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<UsageService>();
builder.Services.AddSingleton<CvTextExtractor>();
builder.Services.AddSingleton<CvParser>();
builder.Services.AddScoped<CvImportService>();
builder.Services.AddScoped<VaultService>();
builder.Services.AddSingleton<JobAnalyzer>();
builder.Services.AddScoped<TargetService>();
builder.Services.AddScoped<InterviewService>();
builder.Services.AddSingleton<BulletRewriter>();
builder.Services.AddSingleton<FabricationGuard>();
builder.Services.AddScoped<TailoringService>();
// Sözlük açılışta bir kez okunuyor; dosya bozuksa uygulama ilk istekte değil burada hata versin.
builder.Services.AddSingleton(SkillDictionary.Load(Path.Combine(AppContext.BaseDirectory, "Knowledge", "synonyms.json")));
builder.Services.AddSingleton<RequirementMatcher>();
builder.Services.AddSingleton(ProfessionCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Knowledge", "professions")));
builder.Services.AddScoped<OneTimeCodeService>();
builder.Services.AddHostedService<CleanupService>();

// SMTP tanımlı değilse mailler terminale düşüyor, geliştirirken işimi görüyor.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Email:Smtp:Host"]))
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();

// Hepsi IAiProvider olarak kayıtlı; AiService anahtarı tanımlı olanı kendisi seçiyor.
// Bütün bir CV'yi yeniden yazmak uzun sürebiliyor, süre 2 dakika.
builder.Services.AddHttpClient("ai", client => client.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddSingleton<IAiProvider, AnthropicProvider>();
builder.Services.AddSingleton<IAiProvider, OpenAiProvider>();
builder.Services.AddSingleton<IAiProvider, GeminiProvider>();
builder.Services.AddSingleton<IAiProvider, DeepSeekProvider>();
builder.Services.AddSingleton<AiService>();

var app = builder.Build();

// Bekleyen migration'lar açılışta uygulanıyor, sunucuda elle SQL çalıştırmaya gerek kalmıyor.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<CvTailorDbContext>().Database.Migrate();
}

// Tünel/proxy arkasında her istek 127.0.0.1'den geliyor gibi görünüyor. Gerçek IP ve https bilgisini
// X-Forwarded-* başlıklarından al; yoksa rate limit herkes için ortak işliyor, maildeki linkler localhost'a gidiyor.
// Varsayılan ayarda sadece loopback'ten gelen başlıklara güveniliyor.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Canlıda stack trace dışarı sızmasın.
    app.UseExceptionHandler(errorApp => errorApp.Run(context =>
        Results.Problem("Beklenmeyen bir hata oluştu.").ExecuteAsync(context)));
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

app.UseResponseCompression();

// Site ile API aynı origin'de, CORS'a gerek yok.
app.UseDefaultFiles();
// no-cache: tarayıcı her seferinde "değişti mi" diye soruyor, değişmediyse 304 geliyor.
// Arayüzü güncellediğimde kimse eski JS ile kalmasın diye.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
});

app.UseAuthentication();

// Sadece çerezle gelen değiştirici isteklerde CSRF kontrolü. Bearer başlığıyla gelen istekleri
// başka site tetikleyemez. Çıkış muaf: token'ın süresi dolmuş olsa da çerez silinebilmeli.
app.Use(async (context, next) =>
{
    var request = context.Request;
    var unsafeMethod = !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method));
    if (unsafeMethod
        && request.Path.StartsWithSegments("/api")
        && !request.Path.StartsWithSegments("/api/auth/logout")
        && request.Cookies.ContainsKey(AuthCookie.Name)
        && !request.Headers.ContainsKey("Authorization")
        && !await context.RequestServices.GetRequiredService<IAntiforgery>().IsRequestValidAsync(context))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { success = false, code = "csrf_invalid", message = "Oturum doğrulanamadı. Sayfayı yenileyip tekrar dene." });
        return;
    }
    await next();
});

app.UseAuthorization();
// "ai" limiti kullanıcı id'sine göre çalışıyor, o yüzden authentication'dan sonra.
app.UseRateLimiter();

app.MapHealthChecks("/health");

// CSRF token'ı oturuma bağlı; arayüz girişten sonra ve ilk değiştirici istekten önce buradan alıyor.
// AuthController'ın rate limit'ine takılmasın diye ayrı uç.
app.MapGet("/api/csrf", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { token = tokens.RequestToken });
});
app.MapControllers();

app.Run();
