using System.Text.Json;
using CvTailor.Api.Dtos;

namespace CvTailor.Api.Services.Targets
{
    // Knowledge/professions/*.json: ilanı olmayan kullanıcı için elle yazılmış meslek profilleri.
    // Gereksinimler seviyeye göre süzülüyor; AI ve kota harcanmıyor.
    public class ProfessionCatalog
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        public static readonly Dictionary<string, string> SeniorityLabels = new() { ["junior"] = "Junior", ["mid"] = "Mid-level", ["senior"] = "Senior" };

        private readonly Dictionary<string, ProfessionProfile> _profiles;

        public ProfessionCatalog(IEnumerable<ProfessionProfile> profiles) =>
            _profiles = profiles.ToDictionary(p => p.Key);

        public static ProfessionCatalog Load(string directory) => new(
            Directory.GetFiles(directory, "*.json")
                .Select(f => JsonSerializer.Deserialize<ProfessionProfile>(File.ReadAllText(f), Json)
                    ?? throw new InvalidOperationException($"Meslek profili okunamadı: {Path.GetFileName(f)}")));

        public List<ProfessionSummaryDto> List() => _profiles.Values
            .OrderBy(p => p.Title)
            .Select(p => new ProfessionSummaryDto { Key = p.Key, Title = p.Title, Summary = p.Summary })
            .ToList();

        public ProfessionProfile? Find(string? key) => key != null && _profiles.TryGetValue(key, out var p) ? p : null;

        // Profilden, ilan çözümleyicinin ürettiğiyle aynı biçimde bir analiz kuruyor; eşleşme ve sonraki adımlar farkı bilmiyor.
        public static JobAnalysis BuildAnalysis(ProfessionProfile profile, string seniority) => JobAnalyzer.Normalize(new JobAnalysis
        {
            Title = $"{profile.Title} ({SeniorityLabels[seniority]})",
            Seniority = seniority,
            Summary = profile.Summary,
            Requirements = profile.Requirements
                .Where(r => r.Levels == null || r.Levels.Contains(seniority))
                .Select(r => new JobRequirement { Text = r.Text, Importance = r.Importance, Category = r.Category, Terms = r.Terms.ToList() })
                .ToList(),
            Keywords = profile.Keywords.ToList()
        });
    }

    public class ProfessionProfile
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        // CV'de bölümlerin önerilen sırası; yeniden yazım bunu kullanacak.
        public List<string> Sections { get; set; } = new();
        public List<ProfessionRequirement> Requirements { get; set; } = new();
        public List<string> Keywords { get; set; } = new();
        public List<string> Tips { get; set; } = new();
        public List<BulletExample> Examples { get; set; } = new();
    }

    public class ProfessionRequirement
    {
        public string Text { get; set; } = string.Empty;
        public string Importance { get; set; } = "must";
        public string Category { get; set; } = "other";
        public List<string> Terms { get; set; } = new();
        // Boşsa her seviyede geçerli.
        public List<string>? Levels { get; set; }
    }
}
