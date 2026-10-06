namespace CvTailor.Api.Models
{
    // İlana göre üretilmiş CV. İçerik ve değişiklik listesi JSON belge olarak duruyor:
    // kasa sonradan değişse de o ilana gönderilen CV bozulmasın.
    public class TailoredCv
    {
        public Guid Id { get; set; }
        public Guid JobTargetId { get; set; }

        // draft: prova ekranında, ready: kullanıcı onayladı, indirilebilir.
        public string Status { get; set; } = "draft";

        // Bölümler ve maddeler; her madde dayandığı kasa kayıtlarının Id'lerini taşıyor.
        public string ContentJson { get; set; } = "{}";
        // Her değişiklik: eski, yeni, neden, kabul/ret durumu, uydurma kontrolünün sonucu.
        public string ChangesJson { get; set; } = "[]";

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public JobTarget? JobTarget { get; set; }
    }
}
