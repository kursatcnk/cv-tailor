namespace CvTailor.Api.Models
{
    // Her AI işlemi (CV ayrıştırma, ilan analizi, yeniden yazım) bir satır. Aylık kota bu ayki satır sayısı.
    public class UsageTracking
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }

        // İstek başlarken "pending" yazılıyor (hak ayrıldı), bitince modelin adı oluyor.
        // Yarım kalan pending satırları CleanupService siliyor.
        public string? Provider { get; set; }

        // parse | analyze | rewrite ...; hangi adımın kotayı ne kadar yediğini görmek için.
        public string? Operation { get; set; }

        public int? TokensUsed { get; set; }
        public DateTime Date { get; set; }

        public User? User { get; set; }
    }
}
