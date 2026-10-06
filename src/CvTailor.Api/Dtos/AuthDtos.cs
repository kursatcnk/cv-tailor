namespace CvTailor.Api.Dtos
{
    public class RegisterRequest
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? DisplayName { get; set; }
    }

    public class LoginRequest
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
        // Beni hatırla: kalıcı çerez mi, tarayıcı kapanınca giden oturum çerezi mi.
        public bool? RememberMe { get; set; }
    }

    public class ForgotPasswordRequest
    {
        public string? Email { get; set; }
    }

    // Maildeki linkten geliyor.
    public class ResetPasswordRequest
    {
        public string? Email { get; set; }
        public string? Token { get; set; }
        public string? NewPassword { get; set; }
    }

    // { success, user, expiresAt }. Token gövdede değil, httpOnly çerezde (AuthCookie).
    public class AuthResponse
    {
        public bool Success { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string? Message { get; set; }
        public UserInfo? User { get; set; }
    }

    // Dışarıya giden kullanıcı modeli; hash burada yok.
    public class UserInfo
    {
        public Guid Id { get; set; }
        public string? Email { get; set; }
        public string? DisplayName { get; set; }
        public bool EmailConfirmed { get; set; }
        public string Plan { get; set; } = "free";
        public DateTime CreatedAt { get; set; }
    }

    public class MessageResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }

        public static MessageResponse Ok(string message) => new() { Success = true, Message = message };
        public static MessageResponse Fail(string message) => new() { Success = false, Message = message };
    }
}
