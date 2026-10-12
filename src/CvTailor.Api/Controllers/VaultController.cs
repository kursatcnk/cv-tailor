using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Cv;
using CvTailor.Api.Services.Quality;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CvTailor.Api.Controllers
{
    // Kariyer kasası. Kontrol ekranı profilin tamamını tek seferde gönderiyor; listede olmayan kayıt siliniyor.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class VaultController : ControllerBase
    {
        private readonly VaultService _vault;

        public VaultController(VaultService vault) => _vault = vault;

        [HttpGet]
        public async Task<ActionResult<VaultResponse>> Get(CancellationToken cancellationToken) =>
            Ok(await _vault.GetAsync(User.GetUserId(), cancellationToken));

        [HttpPut]
        public async Task<ActionResult<VaultResponse>> Save([FromBody] ProfileDto profile, CancellationToken cancellationToken) =>
            Ok(await _vault.SaveAsync(User.GetUserId(), profile, cancellationToken));

        // Formdaki (henüz kaydedilmemiş olabilir) profilin sağlık kontrolü. AI yok, kota harcamıyor.
        [HttpPost("health")]
        public ActionResult<HealthReport> Health([FromBody] ProfileDto profile) =>
            Ok(CvHealthCheck.Check(profile, DateTime.Today));

        // Koç: yazılmakta olan tek madde.
        [HttpPost("lint")]
        public ActionResult<List<LintIssue>> Lint([FromBody] LintRequest request) =>
            Ok(BulletLinter.Lint(request.Text));
    }

    public class LintRequest
    {
        public string? Text { get; set; }
    }
}
