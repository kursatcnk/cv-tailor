using CvTailor.Api.Dtos;
using CvTailor.Api.Models;

namespace CvTailor.Api.Services
{
    public interface IUserService
    {
        Task<AuthResult> RegisterAsync(string email, string password, string displayName);

        Task<AuthResult> LoginAsync(string email, string password);

        // Mail kayıtlı değilse de sessizce dönüyor, bilerek.
        Task RequestPasswordResetAsync(string email, string appBaseUrl);

        Task<(bool success, string? error)> ResetPasswordAsync(string email, string token, string newPassword);

        // Mail gönderilemezse false; kaydı bozmasın diye exception fırlatmıyor.
        Task<bool> SendEmailVerificationAsync(User user);
    }

    public record AuthResult(bool Success, string? Token, string? Error, User? User)
    {
        public static AuthResult Fail(string error) => new(false, null, error, null);
    }
}
