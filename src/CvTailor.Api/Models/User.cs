namespace CvTailor.Api.Models
{
    public class User
    {
        public Guid Id { get; set; }

        // Hep küçük harfe çevrilmiş hâliyle saklanıyor (UserService.NormalizeEmail), unique index var.
        public string Email { get; set; } = string.Empty;

        // BCrypt hash; düz şifre hiçbir yerde tutulmuyor.
        public string PasswordHash { get; set; } = string.Empty;

        public string? DisplayName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // false olan hesap giriş yapamıyor. Şimdilik elle kapatmak için, arayüzde karşılığı yok.
        public bool IsActive { get; set; }

        public bool EmailConfirmed { get; set; }

        // free | pro; aylık kotayı belirliyor (UsageService.Plans).
        public string Plan { get; set; } = "free";
    }
}
