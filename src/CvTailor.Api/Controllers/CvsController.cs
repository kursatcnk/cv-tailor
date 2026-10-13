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
        public async Task<IActionResult> Generate([FromBody] GenerateCvRequest request, CancellationToken cancellationToken) =>
            WithQuota(await _tailoring.GenerateAsync(User.GetUserId(), request.TargetId, cancellationToken));

        // Ön yazı: provadaki son hâliyle CV'den. Tekrar istenirse eskisinin yerine yenisi yazılıyor.
        [HttpPost("{id:guid}/cover-letter")]
        [EnableRateLimiting("ai")]
        public async Task<IActionResult> CoverLetter(Guid id, CancellationToken cancellationToken) =>
            WithQuota(await _tailoring.WriteCoverLetterAsync(User.GetUserId(), id, cancellationToken));

        [HttpGet]
        public async Task<ActionResult<List<TailoredCvSummaryDto>>> List([FromQuery] Guid targetId, CancellationToken cancellationToken) =>
            Ok(await _tailoring.ListAsync(User.GetUserId(), targetId, cancellationToken));

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<TailoredCvDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            var cv = await _tailoring.GetAsync(User.GetUserId(), id, cancellationToken);
            return cv == null ? NotFound(MessageResponse.Fail("CV bulunamadı.")) : Ok(cv);
        }

        // Prova: tek bir değişikliği kabul et, reddet, düzenle ya da alternatifini seç.
        [HttpPatch("{id:guid}/changes/{changeId}")]
        public async Task<IActionResult> UpdateChange(Guid id, string changeId, [FromBody] UpdateChangeRequest request, CancellationToken cancellationToken) =>
            ToResult(await _tailoring.UpdateChangeAsync(User.GetUserId(), id, changeId, request, cancellationToken));

        // Basılan metin her maddenin son kararına göre (kabul → yeni hâli, değilse orijinali).
        [HttpGet("{id:guid}/pdf")]
        public async Task<IActionResult> Pdf(Guid id, CancellationToken cancellationToken)
        {
            var cv = await _tailoring.GetAsync(User.GetUserId(), id, cancellationToken);
            if (cv == null) return NotFound(MessageResponse.Fail("CV bulunamadı."));
            return File(CvPdfRenderer.Render(cv), "application/pdf", FileName(cv, "pdf"));
        }

        // Bazı firmalar ve ilan siteleri Word istiyor; içerik PDF'le aynı.
        [HttpGet("{id:guid}/docx")]
        public async Task<IActionResult> Docx(Guid id, CancellationToken cancellationToken)
        {
            var cv = await _tailoring.GetAsync(User.GetUserId(), id, cancellationToken);
            if (cv == null) return NotFound(MessageResponse.Fail("CV bulunamadı."));
            return File(CvDocxRenderer.Render(cv), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", FileName(cv, "docx"));
        }

        // Provadaki canlı önizleme: PDF'in sayfası resim olarak. Tarayıcının PDF göstericisine güvenmiyoruz
        // (telefonda çoğu zaman açmıyor); sunucuda aynı belgeden çiziliyor, ne indirilecekse o görünüyor.
        [HttpGet("{id:guid}/preview/{page:int}")]
        public async Task<IActionResult> Preview(Guid id, int page, CancellationToken cancellationToken)
        {
            var cv = await _tailoring.GetAsync(User.GetUserId(), id, cancellationToken);
            if (cv == null) return NotFound(MessageResponse.Fail("CV bulunamadı."));
            var pages = CvPdfRenderer.Preview(cv);
            if (page < 1 || page > pages.Count) return NotFound(MessageResponse.Fail("Sayfa yok."));
            Response.Headers["X-Page-Count"] = pages.Count.ToString();
            Response.Headers.CacheControl = "no-store";
            return File(pages[page - 1], "image/png");
        }

        // İndirilecek dosyaların ikisini de üretip CV okuyucumuzla geri okuyor; prova ekranındaki "ATS testi".
        [HttpGet("{id:guid}/ats")]
        public async Task<ActionResult<AtsReport>> Ats(Guid id, CancellationToken cancellationToken)
        {
            var cv = await _tailoring.GetAsync(User.GetUserId(), id, cancellationToken);
            if (cv == null) return NotFound(MessageResponse.Fail("CV bulunamadı."));
            return Ok(AtsRoundTrip.Check(cv));
        }

        // Önce/sonra karnesi: elindeki CV ile bu CV aynı ölçülerle.
        [HttpGet("{id:guid}/scorecard")]
        public async Task<ActionResult<Scorecard>> Scorecard(Guid id, CancellationToken cancellationToken)
        {
            var card = await _tailoring.ScorecardAsync(User.GetUserId(), id, cancellationToken);
            return card == null ? NotFound(MessageResponse.Fail("CV bulunamadı.")) : Ok(card);
        }

        private static string FileName(TailoredCvDto cv, string extension)
        {
            var name = string.Join("-", new[] { cv.Document.Header.FullName, cv.TargetTitle, "CV" }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-')).Trim('-') + "." + extension;
        }

        [HttpPost("{id:guid}/approve")]
        public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken) =>
            ToResult(await _tailoring.ApproveAsync(User.GetUserId(), id, cancellationToken));

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
            await _tailoring.DeleteAsync(User.GetUserId(), id, cancellationToken) ? NoContent() : NotFound(MessageResponse.Fail("CV bulunamadı."));

        private IActionResult ToResult(TailoringOutcome outcome) =>
            outcome.Cv != null ? Ok(outcome.Cv) : BadRequest(MessageResponse.Fail(outcome.Error!));

        // AI harcayan işlemler: kota dolduysa 429 ve güncel kullanım.
        private IActionResult WithQuota(TailoringOutcome outcome) =>
            outcome.Usage != null
                ? StatusCode(StatusCodes.Status429TooManyRequests, new QuotaExceededResponse { Message = outcome.Error!, Usage = outcome.Usage })
                : ToResult(outcome);
    }

    public class GenerateCvRequest
    {
        public Guid TargetId { get; set; }
    }
}
