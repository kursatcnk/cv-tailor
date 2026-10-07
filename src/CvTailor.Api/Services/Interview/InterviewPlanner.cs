using System.Text.RegularExpressions;
using CvTailor.Api.Dtos;

namespace CvTailor.Api.Services.Interview
{
    // Eşleşmede eksik ya da zayıf kalan gereksinimler ve rakamsız maddeler için soru listesi çıkarıyor.
    // Hangi sorunun sorulacağına kod karar veriyor; AI kullanılmıyor. Cevaplar kasaya kullanıcının kendi
    // cümlesiyle yazılıyor, yeniden yazım bunları kaynak olarak kullanıyor. Böylece CV'ye giren her bilgi
    // ya CV'den ya da kullanıcının bu sorulara verdiği cevaptan geliyor.
    public static class InterviewPlanner
    {
        public const int MaxRequirementQuestions = 5;
        public const int MaxNumberQuestions = 3;

        private static readonly Regex HasNumber = new(@"\d", RegexOptions.Compiled);
        // Eğitim, dil, sertifika, ehliyet gibi eksikler soruyla değil kasanın ilgili bölümüne eklenerek kapanıyor;
        // eşleşme notu bunu söylüyor.
        private static readonly string[] AskableCategories = { "technical", "tool", "experience" };

        public static List<InterviewQuestion> Plan(JobAnalysis analysis, MatchResult match, ProfileDto profile, InterviewState state)
        {
            var done = state.Answered.Concat(state.Skipped).ToHashSet();
            var questions = new List<InterviewQuestion>();
            var fallbackParent = MostRecentParent(profile);
            var requirements = analysis.Requirements.ToDictionary(r => r.Key ?? "");

            // Önce zorunlular, sonra tercihler; her grupta önce "yok", sonra "zayıf".
            var gaps = match.Items
                .Where(i => i.Strength is "missing" or "weak" && AskableCategories.Contains(i.Category))
                .OrderBy(i => i.Importance == "must" ? 0 : 1)
                .ThenBy(i => i.Strength == "missing" ? 0 : 1);

            foreach (var item in gaps)
            {
                if (questions.Count >= MaxRequirementQuestions) break;
                var key = $"req:{item.Key}";
                if (done.Contains(key)) continue;
                // Kısmi eksikte eksik olanı, sadece listede yazıyorsa bulunanı, hiç yoksa ilk terimi soruyoruz.
                var term = item.MissingTerms.FirstOrDefault()
                    ?? item.Evidence.Select(e => e.Term?.Split(" → ")[^1]).FirstOrDefault(t => t != null)
                    ?? requirements.GetValueOrDefault(item.Key)?.Terms.FirstOrDefault();
                questions.Add(new InterviewQuestion
                {
                    Key = key,
                    Kind = "requirement",
                    RequirementKey = item.Key,
                    Prompt = RequirementPrompt(item, term),
                    Hint = item.Category == "experience"
                        ? "Örnek: \"Önceki işimde 2 yıl boyunca 120 bayinin cari hesaplarını takip ettim.\""
                        // Terimi şablona koymak her terimde düzgün Türkçe vermiyor ("servisini birim testi yazdım"); örnek sabit.
                        : "Örnek: \"Depo projesinde servisleri Docker ile paketledim; yeni ortam kurulumu 1 günden 1 saate indi.\" Kullanmadıysan \"Yok\" de, uydurmayalım.",
                    // Zayıf kanıt bir işte geçiyorsa cevap muhtemelen o işe ait.
                    SuggestedParentId = ParentOf(item, profile) ?? fallbackParent
                });
            }

            // Ölçülemeyen ama zorunlu yetkinlikler için tek bir örnek soru: "iletişimi güçlü" demek yerine göstermek.
            var soft = match.Items.FirstOrDefault(i => i.Strength == "unknown" && i.Category == "soft" && i.Importance == "must" && !done.Contains($"req:{i.Key}"));
            if (soft != null && questions.Count < MaxRequirementQuestions)
                questions.Add(new InterviewQuestion
                {
                    Key = $"req:{soft.Key}",
                    Kind = "soft",
                    RequirementKey = soft.Key,
                    Prompt = $"İlan \"{soft.Text}\" diyor. Bunu gösteren gerçek bir durum anlatır mısın?",
                    Hint = "Ne oldu, sen ne yaptın, sonuç ne oldu? Örnek: \"İki ekip arasındaki teslim tarihi anlaşmazlığında haftalık ortak toplantı başlattım; gecikmeler bitti.\"",
                    SuggestedParentId = fallbackParent
                });

            questions.AddRange(NumberQuestions(match, profile, done));
            return questions;
        }

        private static string RequirementPrompt(MatchItem item, string? term)
        {
            var subject = term ?? item.Text;
            if (item.Category == "experience")
                return item.Strength == "weak"
                    ? $"İlan \"{item.Text}\" istiyor ve kasandakiler tam karşılamıyor. Kasana yazmadığın, bununla ilgili bir deneyimin var mı?"
                    : $"İlan \"{item.Text}\" istiyor. Bununla ilgili bir deneyimin var mı? Nerede, ne kadar süre, ne yaptın?";
            if (item.MissingTerms.Count > 0)
                return $"İlan \"{item.Text}\" istiyor; kasanda {subject} görünmüyor. {subject} ile bir işte, projede ya da okulda çalıştın mı?";
            return item.Strength == "weak"
                ? $"{subject} kasanda yazıyor ama nerede kullandığın görünmüyor. Hangi işte ya da projede, ne yapmak için kullandın?"
                : $"İlan \"{item.Text}\" istiyor. {subject} ile bir işte, projede ya da okulda çalıştın mı? Çalıştıysan ne yaptığını bir iki cümleyle anlat.";
        }

        // "Raporları hazırladım" gibi rakamsız maddeler. Önce ilanın zorunlu gereksinimlerine kanıt olan maddeler, sonra en yeni işler.
        private static IEnumerable<InterviewQuestion> NumberQuestions(MatchResult match, ProfileDto profile, HashSet<string> done)
        {
            var evidenceOrder = match.Items
                .Where(i => i.Importance == "must")
                .SelectMany(i => i.Evidence)
                .Where(e => e.Kind is "achievement" or "projectAchievement" && e.SourceId != null)
                .Select(e => e.SourceId!.Value)
                .Distinct()
                .ToList();

            var bullets = profile.Experiences.SelectMany(e => e.Achievements.Select(a => (Achievement: a, ParentId: e.Id)))
                .Concat(profile.Projects.SelectMany(p => p.Achievements.Select(a => (Achievement: a, ParentId: p.Id))))
                .Where(x => x.Achievement.Id != null && x.Achievement.Source != "interview" && !HasNumber.IsMatch(x.Achievement.Text ?? ""))
                .ToList();

            return bullets
                .OrderBy(x => evidenceOrder.IndexOf(x.Achievement.Id!.Value) is var i && i >= 0 ? i : int.MaxValue)
                .Where(x => !done.Contains($"num:{x.Achievement.Id}"))
                .Take(MaxNumberQuestions)
                .Select(x => new InterviewQuestion
                {
                    Key = $"num:{x.Achievement.Id}",
                    Kind = "number",
                    AchievementId = x.Achievement.Id,
                    Prompt = $"\"{x.Achievement.Text}\" Bunu bir rakamla anlatabilir misin?",
                    Hint = "Kaç kişi, kaç müşteri/kayıt, ne kadar süre, yüzde kaç iyileşme? Yaklaşık bir değer de olur; bilmiyorsan \"Yok\" de.",
                    SuggestedParentId = x.ParentId
                });
        }

        private static Guid? ParentOf(MatchItem item, ProfileDto profile)
        {
            var evidence = item.Evidence.FirstOrDefault(e => e.Kind is "achievement" or "experience" or "projectAchievement" or "project");
            if (evidence?.SourceId == null) return null;
            var id = evidence.SourceId.Value;
            return profile.Experiences.FirstOrDefault(e => e.Id == id || e.Achievements.Any(a => a.Id == id))?.Id
                ?? profile.Projects.FirstOrDefault(p => p.Id == id || p.Achievements.Any(a => a.Id == id))?.Id;
        }

        // Devam eden ya da en son başlayan iş; iş yoksa ilk proje.
        private static Guid? MostRecentParent(ProfileDto profile) =>
            profile.Experiences
                .OrderByDescending(e => e.IsCurrent)
                .ThenByDescending(e => e.StartDate ?? "")
                .FirstOrDefault()?.Id
            ?? profile.Projects.FirstOrDefault()?.Id;
    }
}
