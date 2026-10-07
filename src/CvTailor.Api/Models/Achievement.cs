namespace CvTailor.Api.Models
{
    // CV'deki tek bir madde. Ya bir deneyime ya bir projeye bağlı, ikisinden biri dolu.
    // Uydurma koruması bu kayıtların Id'leri üzerinden çalışıyor: yeniden yazılan her madde
    // hangi Achievement'lardan türediğini söylemek zorunda.
    public class Achievement : IVaultItem
    {
        public Guid Id { get; set; }
        public Guid? ExperienceId { get; set; }
        public Guid? ProjectId { get; set; }

        public string Text { get; set; } = string.Empty;

        // cv | interview | manual. Röportajdan gelen bilgi CV'de yoktu, kullanıcı sonradan söyledi.
        public string Source { get; set; } = "cv";

        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }

        public Experience? Experience { get; set; }
        public Project? Project { get; set; }
    }
}
