namespace CvTailor.Api.Dtos
{
    // İlana göre üretilmiş CV. TailoredCv.ContentJson'da bu şekilde duruyor.
    // Maddelerin metni burada değil, değişiklik listesinde (CvChange); belge sadece hangi değişikliğin nerede durduğunu tutuyor.
    // Böylece kullanıcı bir maddeyi reddedince belge değişmeden o maddenin orijinali basılıyor.
    public class CvDocument
    {
        public CvHeader Header { get; set; } = new();
        // Özetin değişiklik kimliği; özet yoksa null
        public string? SummaryChangeId { get; set; }
        // experience | projects | education | skills | certificates
        public List<string> SectionOrder { get; set; } = new();
        public List<CvEntry> Experiences { get; set; } = new();
        public List<CvEntry> Projects { get; set; } = new();
        public List<CvEducation> Educations { get; set; } = new();
        public List<CvSkillGroup> Skills { get; set; } = new();
        public List<string> Certificates { get; set; } = new();
        // CV'ye girmeyenler ve nedeni; prova ekranında "neden çıkardık" diye gösteriliyor.
        public List<CvDrop> Dropped { get; set; } = new();
    }

    public class CvHeader
    {
        public string? FullName { get; set; }
        public string? Headline { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Location { get; set; }
        public List<string> Links { get; set; } = new();
    }

    public class CvEntry
    {
        // Kasadaki deneyimin ya da projenin Id'si
        public Guid SourceId { get; set; }
        public string Title { get; set; } = string.Empty;
        // Şirket ya da proje linki
        public string? Subtitle { get; set; }
        public string? Location { get; set; }
        // "Mart 2022 – Devam ediyor"
        public string? Period { get; set; }
        public List<string> BulletChangeIds { get; set; } = new();
    }

    public class CvEducation
    {
        public string School { get; set; } = string.Empty;
        public string? Detail { get; set; }
        public string? Period { get; set; }
    }

    public class CvSkillGroup
    {
        public string Label { get; set; } = string.Empty;
        public List<string> Items { get; set; } = new();
    }

    public class CvDrop
    {
        // bullet | experience | project
        public string Kind { get; set; } = "bullet";
        public Guid? SourceId { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    // Tek bir değişiklik: kasadaki madde(ler) → CV'deki yeni madde. Prova ekranının satırı.
    public class CvChange
    {
        public string Id { get; set; } = string.Empty;
        // bullet | summary
        public string Kind { get; set; } = "bullet";
        public Guid? ParentId { get; set; }
        // Dayandığı kasa maddeleri; uydurma koruması bunlara bakıyor.
        public List<Guid> SourceIds { get; set; } = new();
        // Kaynak maddelerin metni (özet için kasadaki özet)
        public string Before { get; set; } = string.Empty;
        public string After { get; set; } = string.Empty;
        public List<string> Alternatives { get; set; } = new();
        // Neden böyle yazıldı; öğretici kısım
        public string Reason { get; set; } = string.Empty;
        // Bu maddenin kanıt olduğu ilan gereksinimleri (r1, r3...)
        public List<string> Requirements { get; set; } = new();
        public GuardResult Guard { get; set; } = new();
        // accepted | rejected | pending. Doğrulanamayan maddeler pending başlıyor, kullanıcı onaylamadan basılmıyor.
        public string Decision { get; set; } = "accepted";
        // Kullanıcı yeni metni elle düzelttiyse
        public string? EditedText { get; set; }

        // CV'ye basılacak metin: kabul edildiyse yeni hâli, değilse orijinali.
        public string FinalText => Decision == "accepted" ? EditedText ?? After : Before;
    }

    public class GuardResult
    {
        // verified | flagged
        public string Status { get; set; } = "verified";
        // "Kaynakta olmayan rakam: %40" gibi
        public List<string> Issues { get; set; } = new();
    }

    public class TailoredCvDto
    {
        public Guid Id { get; set; }
        public Guid TargetId { get; set; }
        public string TargetTitle { get; set; } = string.Empty;
        public string Status { get; set; } = "draft";
        public CvDocument Document { get; set; } = new();
        public List<CvChange> Changes { get; set; } = new();
        public bool UsedAi { get; set; }
        public string? Notice { get; set; }
        // TC kimlik no, ev adresi gibi CV'de olmaması gerekenler
        public List<string> Warnings { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
