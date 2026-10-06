namespace CvTailor.Api.Models
{
    // CV'nin hedefi: yapıştırılmış bir ilan ya da (ilan yoksa) meslek + seviye.
    public class JobTarget
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }

        public string Title { get; set; } = string.Empty;
        public string? Company { get; set; }

        // İlan modunda dolu. Meslek modunda boş, ProfessionKey dolu.
        public string? PostingText { get; set; }
        public string? ProfessionKey { get; set; }
        // junior | mid | senior
        public string? Seniority { get; set; }

        // İlan çözümleyicinin çıktısı (gereksinimler, anahtar kelimeler). O anın fotoğrafı, JSON olarak.
        public string? AnalysisJson { get; set; }

        public DateTime CreatedAt { get; set; }

        public User? User { get; set; }
        public List<TailoredCv> TailoredCvs { get; set; } = new();
    }
}
