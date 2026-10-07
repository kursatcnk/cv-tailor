namespace CvTailor.Api.Dtos
{
    // İlan çözümleyicinin çıktısı. JobTarget.AnalysisJson'da bu şekilde duruyor.
    public class JobAnalysis
    {
        public string? Title { get; set; }
        public string? Company { get; set; }
        // junior | mid | senior; ilan belirtmiyorsa null
        public string? Seniority { get; set; }
        public int? YearsOfExperience { get; set; }
        // İlanın 1-2 cümlelik Türkçe özeti
        public string? Summary { get; set; }
        public List<JobRequirement> Requirements { get; set; } = new();
        public List<string> Responsibilities { get; set; } = new();
        // İşe alım sistemlerinin arayacağı terimler, ilandaki yazılışıyla
        public List<string> Keywords { get; set; } = new();
    }

    public class JobRequirement
    {
        // r1, r2... Eşleşme ve sorular bu anahtarla gereksinime bağlanıyor.
        public string? Key { get; set; }
        // İlandaki ifadesine yakın, kısa: "En az 2 yıl ASP.NET Core deneyimi"
        public string? Text { get; set; }
        // must | nice
        public string? Importance { get; set; }
        // technical | tool | experience | education | language | certification | soft | other
        public string? Category { get; set; }
        // Eşleştirmede aranacak terimler: ["ASP.NET Core", ".NET"]
        public List<string> Terms { get; set; } = new();
    }

    public class CreateTargetRequest
    {
        public string? PostingText { get; set; }
        public string? Title { get; set; }
        public string? Company { get; set; }
    }

    // İlan yoksa: meslek + seviye
    public class CreateProfessionTargetRequest
    {
        public string? ProfessionKey { get; set; }
        public string? Seniority { get; set; }
    }

    public class ProfessionSummaryDto
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }

    // Meslek profillerindeki öğretici kısım: o meslekte CV yazarken dikkat edilecekler.
    public class ProfessionGuideDto
    {
        public List<string> Tips { get; set; } = new();
        public List<BulletExample> Examples { get; set; } = new();
    }

    public class BulletExample
    {
        public string Bad { get; set; } = string.Empty;
        public string Good { get; set; } = string.Empty;
        public string Why { get; set; } = string.Empty;
    }

    public class TargetSummaryDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Company { get; set; }
        public string? Seniority { get; set; }
        public int MustCount { get; set; }
        public int NiceCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TargetDto : TargetSummaryDto
    {
        public string? PostingText { get; set; }
        public string? ProfessionKey { get; set; }
        public JobAnalysis Analysis { get; set; } = new();
        // Hedef meslek modunda oluşturulduysa o mesleğin ipuçları ve örnek maddeleri.
        public ProfessionGuideDto? Guide { get; set; }
        // Sadece oluşturma cevabında dolu: AI kullanılmadıysa nedenini söylüyor.
        public bool UsedAi { get; set; }
        public string? Notice { get; set; }
    }
}
