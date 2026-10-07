using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Cv;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CvTailor.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [EnableRateLimiting("ai")]
    public class CvController : ControllerBase
    {
        private readonly CvImportService _imports;

        public CvController(CvImportService imports) => _imports = imports;

        // multipart/form-data, alan adı "file". Dosya diske yazılmıyor, akış doğrudan okunuyor.
        [HttpPost("import")]
        [RequestSizeLimit(CvTextExtractor.MaxFileBytes + 64 * 1024)]
        public async Task<IActionResult> Import(IFormFile? file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
                return BadRequest(MessageResponse.Fail("Bir CV dosyası seç."));
            if (file.Length > CvTextExtractor.MaxFileBytes)
                return BadRequest(MessageResponse.Fail("Dosya en fazla 5 MB olabilir."));

            await using var stream = file.OpenReadStream();
            return ToResult(await _imports.ImportFileAsync(User.GetUserId(), stream, file.FileName, cancellationToken));
        }

        [HttpPost("import-text")]
        public async Task<IActionResult> ImportText([FromBody] ImportTextRequest request, CancellationToken cancellationToken) =>
            ToResult(await _imports.ImportTextAsync(User.GetUserId(), request.Text ?? "", cancellationToken));

        private IActionResult ToResult(CvImportOutcome outcome)
        {
            if (outcome.Response != null) return Ok(outcome.Response);
            if (outcome.Usage != null)
                return StatusCode(StatusCodes.Status429TooManyRequests, new QuotaExceededResponse { Message = outcome.Error!, Usage = outcome.Usage });
            return BadRequest(MessageResponse.Fail(outcome.Error!));
        }
    }
}
