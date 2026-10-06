using CvTailor.Api.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CvTailor.Api.Data
{
    public class CvTailorDbContext : DbContext, IDataProtectionKeyContext
    {
        public CvTailorDbContext(DbContextOptions<CvTailorDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<UserToken> UserTokens { get; set; }
        public DbSet<UsageTracking> UsageTracking { get; set; }

        public DbSet<CareerProfile> CareerProfiles { get; set; }
        public DbSet<Experience> Experiences { get; set; }
        public DbSet<Education> Educations { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<Achievement> Achievements { get; set; }
        public DbSet<Skill> Skills { get; set; }
        public DbSet<Certificate> Certificates { get; set; }
        public DbSet<CvImport> CvImports { get; set; }
        public DbSet<JobTarget> JobTargets { get; set; }
        public DbSet<TailoredCv> TailoredCvs { get; set; }

        // Antiforgery anahtarları. DB'de olunca uygulama yeniden başlasa da açık oturumların CSRF token'ı bozulmuyor.
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(u => u.Email).IsRequired().HasMaxLength(255);
                entity.HasIndex(u => u.Email).IsUnique();
                entity.Property(u => u.PasswordHash).IsRequired();
                entity.Property(u => u.DisplayName).HasMaxLength(255);
                entity.Property(u => u.Plan).HasMaxLength(20).HasDefaultValue("free");
            });

            modelBuilder.Entity<UserToken>(entity =>
            {
                entity.Property(t => t.Purpose).IsRequired().HasMaxLength(30);
                entity.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
                entity.HasIndex(t => new { t.UserId, t.Purpose });
                entity.HasOne(t => t.User)
                    .WithMany()
                    .HasForeignKey(t => t.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UsageTracking>(entity =>
            {
                entity.Property(t => t.Provider).HasMaxLength(50);
                entity.Property(t => t.Operation).HasMaxLength(30);
                // Kota her istekte "bu kullanıcının bu ayki satırları" diye sayılıyor.
                entity.HasIndex(t => new { t.UserId, t.Date });
                entity.HasOne(t => t.User)
                    .WithMany()
                    .HasForeignKey(t => t.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            ConfigureCareerVault(modelBuilder);
            ConfigureTargets(modelBuilder);
        }

        // Kullanıcı silinince kasa baştan sona cascade gidiyor (KVKK; CV'nin her satırı kişisel veri).
        private static void ConfigureCareerVault(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CareerProfile>(entity =>
            {
                // Kullanıcı başına tek kasa.
                entity.HasIndex(p => p.UserId).IsUnique();
                entity.Property(p => p.FullName).HasMaxLength(150);
                entity.Property(p => p.Headline).HasMaxLength(150);
                entity.Property(p => p.Email).HasMaxLength(255);
                entity.Property(p => p.Phone).HasMaxLength(40);
                entity.Property(p => p.Location).HasMaxLength(150);
                entity.Property(p => p.LinkedInUrl).HasMaxLength(500);
                entity.Property(p => p.GitHubUrl).HasMaxLength(500);
                entity.Property(p => p.WebsiteUrl).HasMaxLength(500);
                entity.Property(p => p.Summary).HasMaxLength(2000);
                entity.HasOne(p => p.User)
                    .WithOne()
                    .HasForeignKey<CareerProfile>(p => p.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Experience>(entity =>
            {
                entity.Property(e => e.Company).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Location).HasMaxLength(150);
                entity.Property(e => e.EmploymentType).HasMaxLength(20);
                entity.Property(e => e.StartDate).HasMaxLength(7);
                entity.Property(e => e.EndDate).HasMaxLength(7);
                entity.HasOne(e => e.Profile)
                    .WithMany(p => p.Experiences)
                    .HasForeignKey(e => e.ProfileId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Education>(entity =>
            {
                entity.Property(e => e.School).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Degree).HasMaxLength(100);
                entity.Property(e => e.Field).HasMaxLength(200);
                entity.Property(e => e.StartDate).HasMaxLength(7);
                entity.Property(e => e.EndDate).HasMaxLength(7);
                entity.Property(e => e.Gpa).HasMaxLength(20);
                entity.HasOne(e => e.Profile)
                    .WithMany(p => p.Educations)
                    .HasForeignKey(e => e.ProfileId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Project>(entity =>
            {
                entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
                entity.Property(p => p.Url).HasMaxLength(500);
                entity.Property(p => p.Description).HasMaxLength(1000);
                entity.Property(p => p.StartDate).HasMaxLength(7);
                entity.Property(p => p.EndDate).HasMaxLength(7);
                entity.HasOne(p => p.Profile)
                    .WithMany(p => p.Projects)
                    .HasForeignKey(p => p.ProfileId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Achievement>(entity =>
            {
                entity.Property(a => a.Text).IsRequired().HasMaxLength(1000);
                entity.Property(a => a.Source).IsRequired().HasMaxLength(20);
                // Madde ya deneyime ya projeye ait; ikisi birden ya da hiçbiri olmaz.
                entity.ToTable(t => t.HasCheckConstraint("CK_Achievement_SingleParent",
                    "([ExperienceId] IS NOT NULL AND [ProjectId] IS NULL) OR ([ExperienceId] IS NULL AND [ProjectId] IS NOT NULL)"));
                entity.HasOne(a => a.Experience)
                    .WithMany(e => e.Achievements)
                    .HasForeignKey(a => a.ExperienceId)
                    .OnDelete(DeleteBehavior.Cascade);
                // SQL Server aynı tabloya iki cascade yolu kabul etmiyor (Profil → Deneyim → Madde ve Profil → Proje → Madde).
                // Bu yol ClientCascade: proje silinirken maddeleri EF siliyor, servis projeyi maddeleriyle birlikte yüklemeli.
                entity.HasOne(a => a.Project)
                    .WithMany(p => p.Achievements)
                    .HasForeignKey(a => a.ProjectId)
                    .OnDelete(DeleteBehavior.ClientCascade);
            });

            modelBuilder.Entity<Skill>(entity =>
            {
                entity.Property(s => s.Name).IsRequired().HasMaxLength(100);
                entity.Property(s => s.Category).IsRequired().HasMaxLength(20);
                entity.Property(s => s.Level).HasMaxLength(50);
                entity.HasOne(s => s.Profile)
                    .WithMany(p => p.Skills)
                    .HasForeignKey(s => s.ProfileId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Certificate>(entity =>
            {
                entity.Property(c => c.Name).IsRequired().HasMaxLength(200);
                entity.Property(c => c.Issuer).HasMaxLength(200);
                entity.Property(c => c.Date).HasMaxLength(7);
                entity.Property(c => c.Url).HasMaxLength(500);
                entity.HasOne(c => c.Profile)
                    .WithMany(p => p.Certificates)
                    .HasForeignKey(c => c.ProfileId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        private static void ConfigureTargets(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CvImport>(entity =>
            {
                entity.Property(i => i.FileName).IsRequired().HasMaxLength(255);
                entity.Property(i => i.ExtractedText).IsRequired();
                entity.HasIndex(i => new { i.UserId, i.ImportedAt });
                entity.HasOne(i => i.User)
                    .WithMany()
                    .HasForeignKey(i => i.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<JobTarget>(entity =>
            {
                entity.Property(t => t.Title).IsRequired().HasMaxLength(200);
                entity.Property(t => t.Company).HasMaxLength(200);
                entity.Property(t => t.ProfessionKey).HasMaxLength(50);
                entity.Property(t => t.Seniority).HasMaxLength(20);
                // "CV'lerim" listesi hep "bu kullanıcının hedefleri, en yeni önce" diye okunuyor.
                entity.HasIndex(t => new { t.UserId, t.CreatedAt });
                entity.HasOne(t => t.User)
                    .WithMany()
                    .HasForeignKey(t => t.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TailoredCv>(entity =>
            {
                entity.Property(c => c.Status).IsRequired().HasMaxLength(20);
                entity.Property(c => c.ContentJson).IsRequired();
                entity.Property(c => c.ChangesJson).IsRequired();
                // UserId bilerek yok: User → JobTarget → TailoredCv tek cascade yolu kalsın.
                entity.HasOne(c => c.JobTarget)
                    .WithMany(t => t.TailoredCvs)
                    .HasForeignKey(c => c.JobTargetId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
