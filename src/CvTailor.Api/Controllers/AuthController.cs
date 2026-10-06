using CvTailor.Api.Dtos;
using CvTailor.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CvTailor.Api.Controllers
{
    // Giriş gerektirmeyen uçlar. Hepsi IP başına dakikada 10 istekle sınırlı (Program.cs > "auth").
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("auth")]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IUserService userService, IConfiguration configuration, ILogger<AuthController> logger)
        {
            _userService = userService;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
        {
            var result = await _userService.RegisterAsync(request.Email ?? "", request.Password ?? "", request.DisplayName ?? "");
            if (!result.Success)
                return BadRequest(new AuthResponse { Success = false, Message = result.Error });

            _logger.LogInformation("Yeni kullanıcı kaydı: {UserId}", result.User!.Id);
            return Ok(SignIn(result, persistent: true, "Kayıt başarılı. Otomatik giriş yapıldı."));
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
        {
            var result = await _userService.LoginAsync(request.Email ?? "", request.Password ?? "");
            if (!result.Success)
                return Unauthorized(new AuthResponse { Success = false, Message = result.Error });

            return Ok(SignIn(result, request.RememberMe ?? true, "Giriş başarılı"));
        }

        // Token süresi dolmuş olsa da çerez silinebilsin diye [Authorize] yok; CSRF kontrolünden de muaf (Program.cs).
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            AuthCookie.Delete(Response);
            return NoContent();
        }

        [HttpPost("forgot-password")]
        public async Task<ActionResult<MessageResponse>> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            await _userService.RequestPasswordResetAsync(request.Email ?? "", $"{Request.Scheme}://{Request.Host}");
            // Mail kayıtlı olsa da olmasa da aynı cevap; yoksa kimin üye olduğu buradan öğrenilebilir.
            return Ok(MessageResponse.Ok("Bu e-posta kayıtlıysa şifre sıfırlama bağlantısı gönderildi."));
        }

        [HttpPost("reset-password")]
        public async Task<ActionResult<MessageResponse>> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var (success, error) = await _userService.ResetPasswordAsync(request.Email ?? "", request.Token ?? "", request.NewPassword ?? "");
            return success
                ? Ok(MessageResponse.Ok("Şifren güncellendi. Yeni şifrenle giriş yapabilirsin."))
                : BadRequest(MessageResponse.Fail(error!));
        }

        // Token'ı gövdede göndermiyorum; tarayıcı sadece kullanıcı bilgisini ve bitiş zamanını görüyor.
        private AuthResponse SignIn(AuthResult result, bool persistent, string message)
        {
            var minutes = int.TryParse(_configuration["Jwt:ExpiryMinutes"], out var m) && m > 0 ? m : 60;
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(minutes);
            AuthCookie.Append(Response, result.Token!, expiresAt, persistent);

            return new AuthResponse
            {
                Success = true,
                Message = message,
                User = UserService.ToUserInfo(result.User!),
                ExpiresAt = expiresAt
            };
        }
    }
}
