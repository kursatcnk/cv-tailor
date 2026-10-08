using System.Text.RegularExpressions;
using CvTailor.Api.Dtos;

namespace CvTailor.Api.Services.Tailoring
{
    // Kasadan CV'ye neyin gireceğine ve tek sayfaya sığması için neyin çıkacağına karar veriyor. AI kullanmıyor;
    // her çıkarmanın nedeni kullanıcıya gösteriliyor ("Sayfaya sığmadı; ilanla ilgisi en az olan madde buydu").
    public static class CvLayoutPlanner
    {
        // A4, tek sütun, 10 punto, normal kenar boşluklarıyla ~58 satır sığıyor; biraz pay bırakıyorum.
        public const int LineBudget = 54;
        private const int CharsPerLine = 100;

        private static readonly Regex HasNumber = new(@"\d", RegexOptions.Compiled);

        public static CvSelection Select(ProfileDto profile, MatchResult match, DateTime today)
        {
            var selection = new CvSelection();

            // Her madde hangi gereksinimlere kanıt? Zorunluya kanıt olan madde CV'de en çok yeri hak ediyor.
            foreach (var item in match.Items.Where(i => i.Strength is "strong" or "weak"))
                foreach (var evidence in item.Evidence.Where(e => e.SourceId != null && e.Kind is "achievement" or "projectAchievement"))
                {
                    var id = evidence.SourceId!.Value;
                    selection.Requirements.TryAdd(id, new List<string>());
                    if (!selection.Requirements[id].Contains(item.Key)) selection.Requirements[id].Add(item.Key);
                    selection.Scores[id] = selection.Scores.GetValueOrDefault(id) + (item.Importance == "must" ? 3 : 1.5);
                }
            // Rakam içeren madde, aynı ilgilikteki rakamsız maddeden önce gelsin.
            foreach (var a in profile.Experiences.SelectMany(e => e.Achievements).Concat(profile.Projects.SelectMany(p => p.Achievements)))
                if (a.Id != null && HasNumber.IsMatch(a.Text ?? ""))
                    selection.Scores[a.Id.Value] = selection.Scores.GetValueOrDefault(a.Id.Value) + 1;

            var experiences = profile.Experiences
                .OrderByDescending(e => e.IsCurrent)
                .ThenByDescending(e => e.EndDate ?? e.StartDate ?? "")
                .ThenByDescending(e => e.StartDate ?? "")
                .ToList();
            var fullJobs = experiences.Count(e => e.EmploymentType != "internship");

            var index = 0;
            foreach (var e in experiences)
            {
                var score = e.Achievements.Sum(a => selection.Scores.GetValueOrDefault(a.Id ?? Guid.Empty));
                var label = $"{e.Title} · {e.Company}";
                if (e.EmploymentType == "internship" && fullJobs >= 2 && score == 0)
                {
                    selection.Dropped.Add(new CvDrop { Kind = "experience", SourceId = e.Id, Text = label, Reason = "Yeterli iş deneyimin olduğu ve ilanla ilgisi olmadığı için staj çıkarıldı." });
                    continue;
                }
                if (!e.IsCurrent && YearsAgo(e.EndDate, today) > 10 && score == 0)
                {
                    selection.Dropped.Add(new CvDrop { Kind = "experience", SourceId = e.Id, Text = label, Reason = "10 yıldan eski ve ilanla ilgisi yok." });
                    continue;
                }
                var max = e.EmploymentType == "internship" ? 2 : index == 0 ? 5 : index == 1 ? 4 : 3;
                selection.Entries.Add(Pick("experience", e.Id!.Value, e.Achievements, max, selection));
                index++;
            }

            // Az deneyimi olanda projeler CV'nin yarısı; deneyimlide sadece ilanla ilgili olanlar.
            var projects = profile.Projects
                .Select(p => (Project: p, Score: p.Achievements.Sum(a => selection.Scores.GetValueOrDefault(a.Id ?? Guid.Empty))))
                .OrderByDescending(x => x.Score)
                .ToList();
            var projectLimit = fullJobs >= 2 ? 2 : 3;
            foreach (var (project, score) in projects)
            {
                var included = selection.Entries.Count(x => x.Kind == "project");
                if (included >= projectLimit || (fullJobs >= 2 && score == 0))
                {
                    selection.Dropped.Add(new CvDrop { Kind = "project", SourceId = project.Id, Text = project.Name ?? "", Reason = "İlanla ilgisi daha az olduğu için CV'ye alınmadı." });
                    continue;
                }
                selection.Entries.Add(Pick("project", project.Id!.Value, project.Achievements, 3, selection));
            }
            return selection;
        }

        // Bir iş/projeden en ilgili maddeler; ilgisi aynıysa CV'deki sırası korunuyor.
        private static CvSelectedEntry Pick(string kind, Guid id, List<AchievementDto> achievements, int max, CvSelection selection)
        {
            var ordered = achievements
                .Where(a => a.Id != null)
                .Select((a, i) => (Achievement: a, Order: i))
                .OrderByDescending(x => selection.Scores.GetValueOrDefault(x.Achievement.Id!.Value))
                .ThenBy(x => x.Order)
                .ToList();
            foreach (var extra in ordered.Skip(max))
                selection.Dropped.Add(new CvDrop { Kind = "bullet", SourceId = extra.Achievement.Id, Text = extra.Achievement.Text ?? "", Reason = "Bu iş için ilana en uygun maddeler seçildi; bu madde ilanla daha az ilgili." });
            return new CvSelectedEntry
            {
                Kind = kind,
                SourceId = id,
                Facts = ordered.Take(max).OrderBy(x => x.Order).Select(x => x.Achievement).ToList()
            };
        }

        // Tek sayfaya sığana kadar en düşük puanlı maddeyi çıkarıyor. Her işte en az bir madde kalıyor;
        // yetmezse en az ilgili proje, o da yetmezse en eski iş çıkıyor.
        public static void Fit(CvDocument document, List<CvChange> changes, IReadOnlyDictionary<string, double> scores)
        {
            var byId = changes.ToDictionary(c => c.Id);
            while (EstimateLines(document, byId) > LineBudget)
            {
                var candidate = document.Experiences.Concat(document.Projects)
                    .SelectMany((entry, entryIndex) => entry.BulletChangeIds.Count > 1
                        ? entry.BulletChangeIds.Select(id => (Entry: entry, Id: id, Score: scores.GetValueOrDefault(id) - entryIndex * 0.1))
                        : Enumerable.Empty<(CvEntry Entry, string Id, double Score)>())
                    .OrderBy(x => x.Score)
                    .FirstOrDefault();

                if (candidate.Entry != null)
                {
                    candidate.Entry.BulletChangeIds.Remove(candidate.Id);
                    document.Dropped.Add(new CvDrop { Kind = "bullet", SourceId = byId[candidate.Id].SourceIds.FirstOrDefault(), Text = byId[candidate.Id].FinalText, Reason = "Tek sayfaya sığması için çıkarıldı; ilanla ilgisi en az olan madde buydu." });
                    continue;
                }

                var isProject = document.Projects.Count > 0;
                var list = isProject ? document.Projects : document.Experiences;
                if (!isProject && list.Count <= 1) break;
                var removable = list[^1];
                list.RemoveAt(list.Count - 1);
                document.Dropped.Add(new CvDrop { Kind = isProject ? "project" : "experience", SourceId = removable.SourceId, Text = removable.Title, Reason = "Tek sayfaya sığması için çıkarıldı." });
            }
        }

        // Kaba bir satır tahmini; amaç PDF'in ikinci sayfaya taşmasını önlemek, birebir ölçmek değil.
        public static int EstimateLines(CvDocument d, IReadOnlyDictionary<string, CvChange> changes)
        {
            int Lines(string? text) => string.IsNullOrEmpty(text) ? 0 : (text.Length + CharsPerLine - 1) / CharsPerLine;
            var lines = 4; // ad, unvan, iletişim satırı ve boşluk
            if (d.SummaryChangeId != null && changes.TryGetValue(d.SummaryChangeId, out var summary)) lines += Lines(summary.FinalText) + 2;
            foreach (var entry in d.Experiences.Concat(d.Projects))
                lines += 2 + entry.BulletChangeIds.Sum(id => changes.TryGetValue(id, out var c) ? Lines(c.FinalText) : 0);
            lines += d.Educations.Count * 2;
            lines += d.Skills.Count;
            lines += (d.Certificates.Count + 1) / 2;
            lines += 2 * new[] { d.Experiences.Count, d.Projects.Count, d.Educations.Count, d.Skills.Count, d.Certificates.Count }.Count(n => n > 0);
            return lines;
        }

        private static int YearsAgo(string? date, DateTime today) =>
            date != null && int.TryParse(date[..Math.Min(4, date.Length)], out var year) ? today.Year - year : 0;
    }

    public class CvSelection
    {
        public List<CvSelectedEntry> Entries { get; } = new();
        public List<CvDrop> Dropped { get; } = new();
        // Madde Id → puan ve kanıt olduğu gereksinimler
        public Dictionary<Guid, double> Scores { get; } = new();
        public Dictionary<Guid, List<string>> Requirements { get; } = new();
    }

    public class CvSelectedEntry
    {
        // experience | project
        public string Kind { get; set; } = "experience";
        public Guid SourceId { get; set; }
        public List<AchievementDto> Facts { get; set; } = new();
    }
}
