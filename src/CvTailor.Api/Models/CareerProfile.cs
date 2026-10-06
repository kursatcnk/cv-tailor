namespace CvTailor.Api.Models
{
    // Kariyer kasası: kullanıcının tek ana profili. İlana özel her CV buradan seçilip yeniden yazılarak üretiliyor,
    // CV'ye sığmayan deneyimler de burada duruyor.
    public class CareerProfile
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }

        public string? FullName { get; set; }
        // CV'nin en üstündeki tek satır: "Backend Developer", "Muhasebe Uzmanı"
        public string? Headline { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        // Şehir yeterli; tam ev adresi CV'de olmamalı (maskeleme bunu ayrıca uyarıyor).
        public string? Location { get; set; }
        public string? LinkedInUrl { get; set; }
        public string? GitHubUrl { get; set; }
        public string? WebsiteUrl { get; set; }
        public string? Summary { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public User? User { get; set; }
        public List<Experience> Experiences { get; set; } = new();
        public List<Education> Educations { get; set; } = new();
        public List<Project> Projects { get; set; } = new();
        public List<Skill> Skills { get; set; } = new();
        public List<Certificate> Certificates { get; set; } = new();
    }
}
