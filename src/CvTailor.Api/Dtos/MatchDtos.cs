namespace CvTailor.Api.Dtos
{
    // Gereksinim–kanıt matrisi: ilandaki her gereksinimin karşısında kasadaki kanıtlar.
    public class MatchResult
    {
        public List<MatchItem> Items { get; set; } = new();
        public int MustMeasurable { get; set; }
        public int MustStrong { get; set; }
        public int MustWeak { get; set; }
        public int MustMissing { get; set; }
        public int NiceTotal { get; set; }
        public int NiceMet { get; set; }
        // "Ölçülebilen 7 zorunlu gereksinimin 5'ini kanıtlayabiliyorsun."
        public string Verdict { get; set; } = string.Empty;
        // Ne yapmalı: başvur / eksikleri tamamla / başka ilana bak
        public string Advice { get; set; } = string.Empty;
    }

    public class MatchItem
    {
        public string Key { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Importance { get; set; } = "must";
        public string Category { get; set; } = "other";
        // strong: işte/projede kullanıldığı görünüyor · weak: sadece listelenmiş ya da kısmen · missing: yok
        // unknown: anahtar kelimeyle ölçülemiyor (iletişim, takım çalışması...)
        public string Strength { get; set; } = "missing";
        // Kullanıcıya neden bu gücü verdiğimizi anlatan cümle
        public string Note { get; set; } = string.Empty;
        public List<MatchEvidence> Evidence { get; set; } = new();
    }

    public class MatchEvidence
    {
        // achievement (iş maddesi) | projectAchievement (proje maddesi) | project | experience | education | skill | certificate | summary | duration
        public string Kind { get; set; } = string.Empty;
        // Kasa kaydının Id'si; yeniden yazımda bu madde kaynak olarak kullanılacak.
        public Guid? SourceId { get; set; }
        public string Text { get; set; } = string.Empty;
        // "Yazılım Uzmanı · Örnek Lojistik"
        public string? Where { get; set; }
        // Hangi kelimeyle bulundu: "GitHub Actions"
        public string? Term { get; set; }
        public string Strength { get; set; } = "strong";
    }
}
