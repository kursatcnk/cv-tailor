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

        // Rakam sorusuna verilen cevap hangi maddeye detay ekliyor? CV'ye o maddeyle birlikte giriyor ve onunla
        // tek madde olarak yazılıyor; tek başına "ilanla ilgisiz" diye elenmiyor.
        // FK yok: madde silinirse cevap bağımsız bir madde olarak kalıyor.
        public Guid? RelatedId { get; set; }

        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }

        public Experience? Experience { get; set; }
        public Project? Project { get; set; }
    }
}
