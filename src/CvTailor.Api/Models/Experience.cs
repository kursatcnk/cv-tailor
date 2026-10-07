namespace CvTailor.Api.Models
{
    public class Experience : IVaultItem
    {
        public Guid Id { get; set; }
        public Guid ProfileId { get; set; }

        public string Company { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Location { get; set; }
        // full-time | part-time | internship | freelance; staj ile tam zamanlı iş CV'de farklı ağırlıkta.
        public string? EmploymentType { get; set; }

        // "2023-04" ya da sadece "2023". CV'lerde gün yazılmıyor, ISO biçimi sıralamayı da kolaylaştırıyor.
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public bool IsCurrent { get; set; }

        public int SortOrder { get; set; }

        public CareerProfile? Profile { get; set; }
        public List<Achievement> Achievements { get; set; } = new();
    }
}
