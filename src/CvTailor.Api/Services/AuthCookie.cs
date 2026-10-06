namespace CvTailor.Api.Services
{
    // JWT tarayıcıda JS'in okuyabildiği bir yerde durmuyor, httpOnly cookie'de gidip geliyor.
    // Swagger ve dış istemciler için Authorization: Bearer başlığı da çalışıyor.
    public static class AuthCookie
    {
        public const string Name = "cvt.auth";

        // Beni hatırla kapalıysa oturum çerezi: tarayıcı kapanınca gidiyor.
        public static void Append(HttpResponse response, string token, DateTimeOffset expiresAt, bool persistent)
        {
            var options = BaseOptions(response.HttpContext.Request);
            if (persistent) options.Expires = expiresAt;
            response.Cookies.Append(Name, token, options);
        }

        // Silerken de aynı path/samesite/secure değerleri lazım, yoksa tarayıcı çerezi eşleştirmiyor.
        public static void Delete(HttpResponse response) =>
            response.Cookies.Delete(Name, BaseOptions(response.HttpContext.Request));

        // Canlıda proxy X-Forwarded-Proto gönderdiği için IsHttps doğru geliyor; localhost'ta http ile de çalışsın.
        private static CookieOptions BaseOptions(HttpRequest request) => new()
        {
            HttpOnly = true,
            Secure = request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true
        };
    }
}
