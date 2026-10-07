using System.Text.Json;
using CvTailor.Api.Data;
using CvTailor.Api.Dtos;
using CvTailor.Api.Models;
using CvTailor.Api.Services.Cv;
using CvTailor.Api.Services.Matching;
using CvTailor.Api.Services.Targets;
using Microsoft.EntityFrameworkCore;

namespace CvTailor.Api.Services.Interview
{
    // Soruları getiriyor ve cevapları kasaya yazıyor. Cevap olduğu gibi (kullanıcının cümlesiyle) yeni bir madde oluyor;
    // CV'ye nasıl yazılacağına yeniden yazım adımı karar veriyor. Böylece "kullanıcı ne dedi" kaydı hiç bozulmuyor.
    public class InterviewService
    {
        private const int MinAnswerLength = 10;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly CvTailorDbContext _context;
        private readonly TargetService _targets;
        private readonly VaultService _vault;
        private readonly RequirementMatcher _matcher;

        public InterviewService(CvTailorDbContext context, TargetService targets, VaultService vault, RequirementMatcher matcher)
        {
            _context = context;
            _targets = targets;
            _vault = vault;
            _matcher = matcher;
        }

        public async Task<InterviewResponse?> GetAsync(Guid userId, Guid targetId, CancellationToken ct)
        {
            var target = await _context.JobTargets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == targetId && t.UserId == userId, ct);
            return target == null ? null : await BuildAsync(userId, targetId, ReadState(target), ct);
        }

        public async Task<InterviewOutcome> AnswerAsync(Guid userId, Guid targetId, List<InterviewAnswer> answers, CancellationToken ct)
        {
            var target = await _context.JobTargets.FirstOrDefaultAsync(t => t.Id == targetId && t.UserId == userId, ct);
            if (target == null) return InterviewOutcome.Fail("Hedef bulunamadı.");
            var state = ReadState(target);

            // Sadece şu an sorulan soruların cevabı kabul ediliyor; uydurma bir anahtarla başka bir kayda yazılamasın.
            var current = (await BuildAsync(userId, targetId, state, ct))!.Questions.ToDictionary(q => q.Key);

            foreach (var answer in answers)
            {
                if (answer.Key == null || !current.ContainsKey(answer.Key)) continue;
                if (answer.Skip)
                {
                    state.Skipped.Add(answer.Key);
                    continue;
                }

                var text = answer.Answer?.Trim() ?? "";
                if (text.Length < MinAnswerLength)
                    return InterviewOutcome.Fail("Cevap çok kısa. Bir iki cümleyle ne yaptığını anlat ya da \"Yok\" seç.");

                var parent = await FindParentAsync(userId, answer.ParentId, ct);
                if (parent == null)
                    return InterviewOutcome.Fail("Cevabın ekleneceği iş ya da proje bulunamadı.");

                var nextOrder = await _context.Achievements
                    .Where(a => a.ExperienceId == parent.Value.ExperienceId && a.ProjectId == parent.Value.ProjectId)
                    .Select(a => (int?)a.SortOrder).MaxAsync(ct) ?? -1;
                _context.Achievements.Add(new Achievement
                {
                    Id = Guid.NewGuid(),
                    ExperienceId = parent.Value.ExperienceId,
                    ProjectId = parent.Value.ProjectId,
                    Text = text.Length <= 1000 ? text : text[..1000],
                    Source = "interview",
                    SortOrder = nextOrder + 1,
                    CreatedAt = DateTime.UtcNow
                });
                state.Answered.Add(answer.Key);
            }

            target.InterviewJson = JsonSerializer.Serialize(state, Json);
            await _context.SaveChangesAsync(ct);
            return InterviewOutcome.Ok((await BuildAsync(userId, targetId, state, ct))!);
        }

        private async Task<InterviewResponse?> BuildAsync(Guid userId, Guid targetId, InterviewState state, CancellationToken ct)
        {
            var target = await _targets.GetAsync(userId, targetId, ct);
            if (target == null) return null;
            var profile = (await _vault.GetAsync(userId, ct)).Profile;
            var match = _matcher.Match(target.Analysis, profile, DateTime.UtcNow);
            return new InterviewResponse
            {
                Questions = InterviewPlanner.Plan(target.Analysis, match, profile, state),
                Match = match,
                Parents = profile.Experiences.Select(e => new InterviewParent { Id = e.Id!.Value, Label = $"{e.Title} · {e.Company}" })
                    .Concat(profile.Projects.Select(p => new InterviewParent { Id = p.Id!.Value, Label = $"Proje: {p.Name}" }))
                    .ToList(),
                AnsweredCount = state.Answered.Count
            };
        }

        // Cevabın ekleneceği yer bu kullanıcının bir deneyimi ya da projesi olmalı.
        private async Task<(Guid? ExperienceId, Guid? ProjectId)?> FindParentAsync(Guid userId, Guid? parentId, CancellationToken ct)
        {
            if (parentId == null) return null;
            if (await _context.Experiences.AnyAsync(e => e.Id == parentId && e.Profile!.UserId == userId, ct))
                return (parentId, null);
            if (await _context.Projects.AnyAsync(p => p.Id == parentId && p.Profile!.UserId == userId, ct))
                return (null, parentId);
            return null;
        }

        private static InterviewState ReadState(JobTarget target) =>
            string.IsNullOrEmpty(target.InterviewJson) ? new InterviewState() : JsonSerializer.Deserialize<InterviewState>(target.InterviewJson, Json) ?? new InterviewState();
    }

    public class InterviewResponse
    {
        public List<InterviewQuestion> Questions { get; set; } = new();
        // Cevaplardan sonra eşleşme değişiyor; ekran ikisini birlikte tazeliyor.
        public MatchResult Match { get; set; } = new();
        public List<InterviewParent> Parents { get; set; } = new();
        public int AnsweredCount { get; set; }
    }

    public class InterviewParent
    {
        public Guid Id { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    public record InterviewOutcome(InterviewResponse? Response, string? Error)
    {
        public static InterviewOutcome Ok(InterviewResponse response) => new(response, null);
        public static InterviewOutcome Fail(string error) => new(null, error);
    }
}
