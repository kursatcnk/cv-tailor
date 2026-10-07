namespace CvTailor.Api.Dtos
{
    // Eşleşmede açık kalan yerler için kullanıcıya sorulan sorular.
    public class InterviewQuestion
    {
        // req:r3 (gereksinim sorusu) ya da num:{maddeId} (rakam sorusu). Cevaplanan/atlanan anahtar bir daha sorulmuyor.
        public string Key { get; set; } = string.Empty;
        // requirement | soft | number
        public string Kind { get; set; } = "requirement";
        public string Prompt { get; set; } = string.Empty;
        // Nasıl cevaplanacağına dair kısa örnek
        public string Hint { get; set; } = string.Empty;
        public string? RequirementKey { get; set; }
        public Guid? AchievementId { get; set; }
        // Cevabın ekleneceği iş/proje için öneri; kullanıcı değiştirebiliyor.
        public Guid? SuggestedParentId { get; set; }
    }

    public class InterviewAnswer
    {
        public string? Key { get; set; }
        public string? Answer { get; set; }
        // Cevabın ekleneceği deneyim ya da projenin Id'si
        public Guid? ParentId { get; set; }
        // "Yok / kullanmadım": madde üretilmiyor, soru bir daha sorulmuyor.
        public bool Skip { get; set; }
    }

    public class InterviewAnswersRequest
    {
        public List<InterviewAnswer> Answers { get; set; } = new();
    }

    // JobTarget.InterviewJson
    public class InterviewState
    {
        public List<string> Answered { get; set; } = new();
        public List<string> Skipped { get; set; } = new();
    }
}
