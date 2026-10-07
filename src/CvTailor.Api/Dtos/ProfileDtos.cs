namespace CvTailor.Api.Dtos
{
    // Kariyer kasasının dışarıya bakan hâli. Hem AI'ın CV'den çıkardığı taslak hem de kontrol ekranından
    // kaydedilen profil bu şekilde gidip geliyor; alan adları JS tarafıyla aynı.
    public class ProfileDto
    {
        public string? FullName { get; set; }
        public string? Headline { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Location { get; set; }
        public string? LinkedInUrl { get; set; }
        public string? GitHubUrl { get; set; }
        public string? WebsiteUrl { get; set; }
        public string? Summary { get; set; }

        public List<ExperienceDto> Experiences { get; set; } = new();
        public List<EducationDto> Educations { get; set; } = new();
        public List<ProjectDto> Projects { get; set; } = new();
        public List<SkillDto> Skills { get; set; } = new();
        public List<CertificateDto> Certificates { get; set; } = new();
    }

    // Id boşsa yeni kayıt. Kaydederken Id'si gelmeyen eski kayıtlar siliniyor.
    public class ExperienceDto
    {
        public Guid? Id { get; set; }
        public string? Company { get; set; }
        public string? Title { get; set; }
        public string? Location { get; set; }
        public string? EmploymentType { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public bool IsCurrent { get; set; }
        public List<AchievementDto> Achievements { get; set; } = new();
    }

    public class EducationDto
    {
        public Guid? Id { get; set; }
        public string? School { get; set; }
        public string? Degree { get; set; }
        public string? Field { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? Gpa { get; set; }
    }

    public class ProjectDto
    {
        public Guid? Id { get; set; }
        public string? Name { get; set; }
        public string? Url { get; set; }
        public string? Description { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public List<AchievementDto> Achievements { get; set; } = new();
    }

    public class AchievementDto
    {
        public Guid? Id { get; set; }
        public string? Text { get; set; }
        public string? Source { get; set; }
    }

    public class SkillDto
    {
        public Guid? Id { get; set; }
        public string? Name { get; set; }
        public string? Category { get; set; }
        public string? Level { get; set; }
    }

    public class CertificateDto
    {
        public Guid? Id { get; set; }
        public string? Name { get; set; }
        public string? Issuer { get; set; }
        public string? Date { get; set; }
        public string? Url { get; set; }
    }

    public class ImportTextRequest
    {
        public string? Text { get; set; }
    }

    // POST /api/cv/import cevabı. Profil henüz kaydedilmedi; kullanıcı kontrol ekranında düzeltip kaydediyor.
    public class CvImportResponse
    {
        public Guid ImportId { get; set; }
        public ProfileDto Profile { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public bool UsedAi { get; set; }
        public string? Notice { get; set; }
        // Kontrol ekranında "okuyabildiğim metin" olarak yan yana gösteriliyor.
        public string ExtractedText { get; set; } = string.Empty;
    }
}
