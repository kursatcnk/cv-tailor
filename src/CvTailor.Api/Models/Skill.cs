namespace CvTailor.Api.Models
{
    public class Skill
    {
        public Guid Id { get; set; }
        public Guid ProfileId { get; set; }

        public string Name { get; set; } = string.Empty;
        // technical | tool | language | soft
        public string Category { get; set; } = "technical";
        // Diller için seviye: "B2", "İleri", "Ana dil". Diğerlerinde genelde boş.
        public string? Level { get; set; }

        public int SortOrder { get; set; }

        public CareerProfile? Profile { get; set; }
    }
}
