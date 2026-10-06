namespace CvTailor.Api.Dtos
{
    public class UsageDto
    {
        public string Plan { get; set; } = "free";
        public int Used { get; set; }
        public int Limit { get; set; }
        public DateTime ResetsAt { get; set; } // bir sonraki ayın ilk günü, UTC
    }

    // Aylık kota dolunca 429 ile dönüyor. Code alanı, dakikalık istek sınırının 429'undan ayırt etmek için.
    public class QuotaExceededResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Code { get; set; } = "quota_exceeded";
        public UsageDto Usage { get; set; } = new();
    }

    public class AiStatusDto
    {
        public bool Enabled { get; set; }
        public string? Provider { get; set; }
        public string? Model { get; set; }
    }

    // GET /api/account/me: arayüz açılışta ihtiyaç duyduğu her şeyi tek istekte alıyor.
    public class MeResponse
    {
        public UserInfo User { get; set; } = new();
        public UsageDto Usage { get; set; } = new();
        public AiStatusDto Ai { get; set; } = new();
    }

    public class UpdateProfileRequest
    {
        public string? DisplayName { get; set; }
    }

    public class ChangePasswordRequest
    {
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }
    }

    public class CodeRequest
    {
        public string? Code { get; set; }
    }

    // Hesap silmede şifre tekrar soruluyor.
    public class PasswordConfirmRequest
    {
        public string? Password { get; set; }
    }
}
