using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Interview;
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

        // Gereksinim–kanıt matrisi: o anki kasaya göre hesaplanıyor.
        [HttpGet("{id:guid}/match")]
        public async Task<ActionResult<MatchResult>> Match(Guid id, CancellationToken cancellationToken)
        {
            var match = await _targets.MatchAsync(User.GetUserId(), id, cancellationToken);
            return match == null ? NotFound(MessageResponse.Fail("İlan bulunamadı.")) : Ok(match);
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

        // Eşleşmede açık kalanlar için sorular. Cevaplar kasaya yeni madde olarak yazılıyor.
        [HttpGet("{id:guid}/interview")]
        public async Task<ActionResult<InterviewResponse>> Interview(Guid id, [FromServices] InterviewService interview, CancellationToken cancellationToken)
        {
            var response = await interview.GetAsync(User.GetUserId(), id, cancellationToken);
            return response == null ? NotFound(MessageResponse.Fail("Hedef bulunamadı.")) : Ok(response);
        }

        [HttpPost("{id:guid}/interview")]
        public async Task<IActionResult> Answer(Guid id, [FromBody] InterviewAnswersRequest request, [FromServices] InterviewService interview, CancellationToken cancellationToken)
        {
            var outcome = await interview.AnswerAsync(User.GetUserId(), id, request.Answers, cancellationToken);
            return outcome.Response != null ? Ok(outcome.Response) : BadRequest(MessageResponse.Fail(outcome.Error!));
        }

        [HttpGet("professions")]
        public ActionResult<List<ProfessionSummaryDto>> Professions() => Ok(_targets.Professions());

        // AI kullanmadığı için "ai" rate limit'i yok.
        [HttpPost("profession")]
        public async Task<IActionResult> CreateFromProfession([FromBody] CreateProfessionTargetRequest request, CancellationToken cancellationToken)
        {
            var outcome = await _targets.CreateFromProfessionAsync(User.GetUserId(), request, cancellationToken);
            return outcome.Target != null ? Ok(outcome.Target) : BadRequest(MessageResponse.Fail(outcome.Error!));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
            await _targets.DeleteAsync(User.GetUserId(), id, cancellationToken) ? NoContent() : NotFound(MessageResponse.Fail("İlan bulunamadı."));
    }
}
