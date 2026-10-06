namespace CvTailor.Api.Models
{
    // Tek kullanımlık kodlar. Purpose: email-verify, password-reset (bkz. OneTimeCodeService).
    // Kodun kendisi değil SHA-256'sı duruyor; DB sızsa da geçerli kod çıkmıyor.
    public class UserToken
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public int FailedAttempts { get; set; }
        public DateTime CreatedAt { get; set; }

        public User? User { get; set; }
    }
}
