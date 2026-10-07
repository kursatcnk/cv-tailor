namespace CvTailor.Api.Models
{
    public class Education : IVaultItem
    {
        public Guid Id { get; set; }
        public Guid ProfileId { get; set; }

        public string School { get; set; } = string.Empty;
        // Lisans, Yüksek Lisans, Önlisans, Lise...
        public string? Degree { get; set; }
        public string? Field { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        // Yazıldığı gibi tutuluyor ("3.12/4", "78/100"); dönüştürme sonraya.
        public string? Gpa { get; set; }

        public int SortOrder { get; set; }

        public CareerProfile? Profile { get; set; }
    }
}
