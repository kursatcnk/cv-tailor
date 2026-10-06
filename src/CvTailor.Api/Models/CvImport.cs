namespace CvTailor.Api.Models
{
    // Yüklenen CV'nin çıkarılmış metni. Dosyanın kendisi saklanmıyor; metin, ayrıştırma bozuk çıkarsa
    // tekrar denemek ve kullanıcıya "şunu okuyabildim" diye göstermek için duruyor.
    public class CvImport
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }

        public string FileName { get; set; } = string.Empty;
        public string ExtractedText { get; set; } = string.Empty;
        public DateTime ImportedAt { get; set; }

        public User? User { get; set; }
    }
}
