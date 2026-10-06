namespace CvTailor.Api.Models
{
    public class Certificate
    {
        public Guid Id { get; set; }
        public Guid ProfileId { get; set; }

        public string Name { get; set; } = string.Empty;
        public string? Issuer { get; set; }
        public string? Date { get; set; }
        public string? Url { get; set; }

        public int SortOrder { get; set; }

        public CareerProfile? Profile { get; set; }
    }
}
