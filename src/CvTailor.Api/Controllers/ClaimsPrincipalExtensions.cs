using System.Security.Claims;

namespace CvTailor.Api.Controllers
{
    public static class ClaimsPrincipalExtensions
    {
        // Kullanıcı id'sini hep token'dan alıyorum, body/URL'den değil. Böylece kimse başkası adına istek atamaz.
        // Sadece [Authorize] olan yerlerde çağrılıyor, orada claim her zaman dolu.
        public static Guid GetUserId(this ClaimsPrincipal user) =>
            Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
