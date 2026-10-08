using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Tailoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CvTailor.Api.Controllers
{
    // İlana göre üretilmiş CV'ler.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CvsController : ControllerBase
    {
        private readonly TailoringService _tailoring;

        public CvsController(TailoringService tailoring) => _tailoring = tailoring;

        // Hedef için yeni taslak. Her çağrı yeni bir sürüm; eskiler duruyor.
        [HttpPost]
        [EnableRateLimiting("ai")]
        public async Task<IActionResult> Generate([FromBody] GenerateCvRequest request, CancellationToken cancellationToken)
        {
            var outcome = await _tailoring.GenerateAsync(User.GetUserId(), request.TargetId, cancellationToken);
            if (outcome.Cv != null) return Ok(outcome.Cv);
            if (outcome.Usage != null)
                return StatusCode(StatusCodes.Status429TooManyRequests, new QuotaExceededResponse { Message = outcome.Error!, Usage = outcome.Usage });
            return BadRequest(MessageResponse.Fail(outcome.Error!));
        }

        [HttpGet]
        public async Task<ActionResult<List<TailoredCvSummaryDto>>> List([FromQuery] Guid targetId, CancellationToken cancellationToken) =>
            Ok(await _tailoring.ListAsync(User.GetUserId(), targetId, cancellationToken));

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<TailoredCvDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            var cv = await _tailoring.GetAsync(User.GetUserId(), id, cancellationToken);
            return cv == null ? NotFound(MessageResponse.Fail("CV bulunamadı.")) : Ok(cv);
        }
    }

    public class GenerateCvRequest
    {
        public Guid TargetId { get; set; }
    }
}
