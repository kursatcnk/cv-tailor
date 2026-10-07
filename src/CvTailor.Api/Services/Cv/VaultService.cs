using CvTailor.Api.Data;
using CvTailor.Api.Dtos;
using CvTailor.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CvTailor.Api.Services.Cv
{
    // Kariyer kasası: okuma ve kontrol ekranından gelen profili kaydetme.
    // Kaydederken her şeyi silip baştan eklemiyorum; kayıtlar Id'ye göre eşleşiyor. Maddelerin Id'si
    // korunmalı, çünkü ilana özel CV'lerdeki her madde hangi kasa maddesinden türediğini bu Id ile tutuyor.
    public class VaultService
    {
        private readonly CvTailorDbContext _context;

        public VaultService(CvTailorDbContext context) => _context = context;

        public async Task<VaultResponse> GetAsync(Guid userId, CancellationToken ct)
        {
            var profile = await Load(userId).AsNoTracking().FirstOrDefaultAsync(ct);
            return profile == null
                ? new VaultResponse { Exists = false, Profile = new ProfileDto() }
                : new VaultResponse { Exists = true, Profile = ToDto(profile), UpdatedAt = DateTime.SpecifyKind(profile.UpdatedAt, DateTimeKind.Utc) };
        }

        public async Task<VaultResponse> SaveAsync(Guid userId, ProfileDto incoming, CancellationToken ct)
        {
            // Kontrol ekranında elle eklenen maddenin kaynağı "manual"; CV'den gelenler "cv" olarak geliyor.
            var dto = ProfileNormalizer.Normalize(incoming, "manual");

            var profile = await Load(userId).FirstOrDefaultAsync(ct);
            if (profile == null)
            {
                profile = new CareerProfile { Id = Guid.NewGuid(), UserId = userId, CreatedAt = DateTime.UtcNow };
                _context.CareerProfiles.Add(profile);
            }

            profile.FullName = dto.FullName;
            profile.Headline = dto.Headline;
            profile.Email = dto.Email;
            profile.Phone = dto.Phone;
            profile.Location = dto.Location;
            profile.LinkedInUrl = dto.LinkedInUrl;
            profile.GitHubUrl = dto.GitHubUrl;
            profile.WebsiteUrl = dto.WebsiteUrl;
            profile.Summary = dto.Summary;
            profile.UpdatedAt = DateTime.UtcNow;

            Sync(profile.Experiences, dto.Experiences, d => d.Id, () => new Experience { ProfileId = profile.Id }, (e, d, i) =>
            {
                e.Company = d.Company ?? "";
                e.Title = d.Title ?? "";
                e.Location = d.Location;
                e.EmploymentType = d.EmploymentType;
                e.StartDate = d.StartDate;
                e.EndDate = d.EndDate;
                e.IsCurrent = d.IsCurrent;
                e.SortOrder = i;
                SyncAchievements(e.Achievements, d.Achievements, a => a.ExperienceId = e.Id);
            });

            Sync(profile.Educations, dto.Educations, d => d.Id, () => new Education { ProfileId = profile.Id }, (e, d, i) =>
            {
                e.School = d.School!;
                e.Degree = d.Degree;
                e.Field = d.Field;
                e.StartDate = d.StartDate;
                e.EndDate = d.EndDate;
                e.Gpa = d.Gpa;
                e.SortOrder = i;
            });

            Sync(profile.Projects, dto.Projects, d => d.Id, () => new Project { ProfileId = profile.Id }, (p, d, i) =>
            {
                p.Name = d.Name!;
                p.Url = d.Url;
                p.Description = d.Description;
                p.StartDate = d.StartDate;
                p.EndDate = d.EndDate;
                p.SortOrder = i;
                SyncAchievements(p.Achievements, d.Achievements, a => a.ProjectId = p.Id);
            });

            Sync(profile.Skills, dto.Skills, d => d.Id, () => new Skill { ProfileId = profile.Id }, (s, d, i) =>
            {
                s.Name = d.Name!;
                s.Category = d.Category!;
                s.Level = d.Level;
                s.SortOrder = i;
            });

            Sync(profile.Certificates, dto.Certificates, d => d.Id, () => new Certificate { ProfileId = profile.Id }, (c, d, i) =>
            {
                c.Name = d.Name!;
                c.Issuer = d.Issuer;
                c.Date = d.Date;
                c.Url = d.Url;
                c.SortOrder = i;
            });

            await _context.SaveChangesAsync(ct);
            return await GetAsync(userId, ct);
        }

        private void SyncAchievements(List<Achievement> existing, List<AchievementDto> incoming, Action<Achievement> attach) =>
            Sync(existing, incoming, d => d.Id, () =>
            {
                var a = new Achievement { CreatedAt = DateTime.UtcNow };
                attach(a);
                return a;
            }, (a, d, i) =>
            {
                // Metni değişen maddenin kaynağı korunuyor; CV'den geldiyse kullanıcı düzeltse de "cv" kalıyor.
                a.Text = d.Text!;
                a.Source = d.Source!;
                a.SortOrder = i;
            });

        // Gelen listede Id'si olan ve bu kullanıcıya ait kayıt güncelleniyor, Id'siz ya da tanınmayan Id'li olan
        // yeni kayıt oluyor (başkasının kaydının Id'sini göndermek bir şey değiştirmiyor), listede olmayan siliniyor.
        private void Sync<TEntity, TDto>(List<TEntity> existing, List<TDto> incoming, Func<TDto, Guid?> idOf,
            Func<TEntity> create, Action<TEntity, TDto, int> apply) where TEntity : class, IVaultItem
        {
            var byId = existing.ToDictionary(e => e.Id);
            var kept = new HashSet<Guid>();

            for (var i = 0; i < incoming.Count; i++)
            {
                var dto = incoming[i];
                if (idOf(dto) is Guid id && byId.TryGetValue(id, out var entity) && kept.Add(id))
                {
                    apply(entity, dto, i);
                    continue;
                }

                entity = create();
                entity.Id = Guid.NewGuid();
                // Id'yi ben verdiğim için EF, koleksiyona eklenen kaydı mevcut sanıp UPDATE atıyor (0 satır → concurrency hatası).
                // Açıkça Added işaretle.
                _context.Add(entity);
                existing.Add(entity);
                kept.Add(entity.Id);
                apply(entity, dto, i);
            }

            foreach (var stale in byId.Where(pair => !kept.Contains(pair.Key)).Select(pair => pair.Value).ToList())
            {
                existing.Remove(stale);
                _context.Remove(stale);
            }
        }

        // Proje → madde ilişkisi ClientCascade; proje silinirken maddeleri EF'in silebilmesi için yüklü olmalı.
        private IQueryable<CareerProfile> Load(Guid userId) => _context.CareerProfiles
            .Where(p => p.UserId == userId)
            .Include(p => p.Experiences).ThenInclude(e => e.Achievements)
            .Include(p => p.Projects).ThenInclude(p => p.Achievements)
            .Include(p => p.Educations)
            .Include(p => p.Skills)
            .Include(p => p.Certificates)
            .AsSplitQuery();

        private static ProfileDto ToDto(CareerProfile p) => new()
        {
            FullName = p.FullName,
            Headline = p.Headline,
            Email = p.Email,
            Phone = p.Phone,
            Location = p.Location,
            LinkedInUrl = p.LinkedInUrl,
            GitHubUrl = p.GitHubUrl,
            WebsiteUrl = p.WebsiteUrl,
            Summary = p.Summary,
            Experiences = p.Experiences.OrderBy(e => e.SortOrder).Select(e => new ExperienceDto
            {
                Id = e.Id, Company = e.Company, Title = e.Title, Location = e.Location, EmploymentType = e.EmploymentType,
                StartDate = e.StartDate, EndDate = e.EndDate, IsCurrent = e.IsCurrent,
                Achievements = ToDto(e.Achievements)
            }).ToList(),
            Educations = p.Educations.OrderBy(e => e.SortOrder).Select(e => new EducationDto
            {
                Id = e.Id, School = e.School, Degree = e.Degree, Field = e.Field, StartDate = e.StartDate, EndDate = e.EndDate, Gpa = e.Gpa
            }).ToList(),
            Projects = p.Projects.OrderBy(x => x.SortOrder).Select(x => new ProjectDto
            {
                Id = x.Id, Name = x.Name, Url = x.Url, Description = x.Description, StartDate = x.StartDate, EndDate = x.EndDate,
                Achievements = ToDto(x.Achievements)
            }).ToList(),
            Skills = p.Skills.OrderBy(s => s.SortOrder).Select(s => new SkillDto { Id = s.Id, Name = s.Name, Category = s.Category, Level = s.Level }).ToList(),
            Certificates = p.Certificates.OrderBy(c => c.SortOrder).Select(c => new CertificateDto { Id = c.Id, Name = c.Name, Issuer = c.Issuer, Date = c.Date, Url = c.Url }).ToList()
        };

        private static List<AchievementDto> ToDto(IEnumerable<Achievement> items) =>
            items.OrderBy(a => a.SortOrder).Select(a => new AchievementDto { Id = a.Id, Text = a.Text, Source = a.Source }).ToList();
    }

    public class VaultResponse
    {
        public bool Exists { get; set; }
        public ProfileDto Profile { get; set; } = new();
        public DateTime? UpdatedAt { get; set; }
    }
}
