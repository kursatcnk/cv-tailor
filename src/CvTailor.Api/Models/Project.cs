namespace CvTailor.Api.Models
{
    public class Project
    {
        public Guid Id { get; set; }
        public Guid ProfileId { get; set; }

        public string Name { get; set; } = string.Empty;
        public string? Url { get; set; }
        // Tek cümlelik tanım; ne yapıldığı maddelerde (Achievements).
        public string? Description { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }

        public int SortOrder { get; set; }

        public CareerProfile? Profile { get; set; }
        public List<Achievement> Achievements { get; set; } = new();
    }
}
