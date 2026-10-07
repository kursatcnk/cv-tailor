using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Targets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CvTailor.Api.Controllers
{
    // CV hazırlanacak ilanlar.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TargetsController : ControllerBase
    {
        private readonly TargetService _targets;

        public TargetsController(TargetService targets) => _targets = targets;

        [HttpGet]
        public async Task<ActionResult<List<TargetSummaryDto>>> List(CancellationToken cancellationToken) =>
            Ok(await _targets.ListAsync(User.GetUserId(), cancellationToken));

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<TargetDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            var target = await _targets.GetAsync(User.GetUserId(), id, cancellationToken);
            return target == null ? NotFound(MessageResponse.Fail("İlan bulunamadı.")) : Ok(target);
        }

        [HttpPost]
        [EnableRateLimiting("ai")]
        public async Task<IActionResult> Create([FromBody] CreateTargetRequest request, CancellationToken cancellationToken)
        {
            var outcome = await _targets.CreateAsync(User.GetUserId(), request, cancellationToken);
            if (outcome.Target != null) return Ok(outcome.Target);
            if (outcome.Usage != null)
                return StatusCode(StatusCodes.Status429TooManyRequests, new QuotaExceededResponse { Message = outcome.Error!, Usage = outcome.Usage });
            return BadRequest(MessageResponse.Fail(outcome.Error!));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
            await _targets.DeleteAsync(User.GetUserId(), id, cancellationToken) ? NoContent() : NotFound(MessageResponse.Fail("İlan bulunamadı."));
    }
}
