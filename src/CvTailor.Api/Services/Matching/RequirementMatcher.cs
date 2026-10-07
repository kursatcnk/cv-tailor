using System.Text.RegularExpressions;
using CvTailor.Api.Dtos;

namespace CvTailor.Api.Services.Matching
{
    // İlandaki her gereksinim için kasada kanıt arıyor. AI kullanmıyor: aynı kasa ve ilan için her zaman aynı sonuç,
    // ve her kararın gerekçesi gösterilebiliyor. Kanıtın gücü nerede geçtiğine bağlı:
    // bir iş/proje maddesinde geçiyorsa güçlü (kullanmışsın), sadece beceri listesinde geçiyorsa zayıf (yazmışsın ama göstermemişsin).
    public class RequirementMatcher
    {
        private static readonly Regex Years = new(@"(?<n>\d{1,2})\s*\+?\s*(yıl|yil|sene|year)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // Virgül bilerek yok: "(C#, Java, Python)" gibi listeler genelde seçenek, hepsi istenmiyor.
        private static readonly Regex Conjunction = new(@"\s(ve|and|ile)\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly string[] StrictCategories = { "technical", "tool", "certification" };

        private readonly SkillDictionary _dictionary;

        public RequirementMatcher(SkillDictionary dictionary) => _dictionary = dictionary;

        public MatchResult Match(JobAnalysis analysis, ProfileDto profile, DateTime today)
        {
            var result = new MatchResult();
            foreach (var requirement in analysis.Requirements)
                result.Items.Add(MatchOne(requirement, profile, today));

            var must = result.Items.Where(i => i.Importance == "must" && i.Strength != "unknown").ToList();
            result.MustMeasurable = must.Count;
            result.MustStrong = must.Count(i => i.Strength == "strong");
            result.MustWeak = must.Count(i => i.Strength == "weak");
            result.MustMissing = must.Count(i => i.Strength == "missing");
            var nice = result.Items.Where(i => i.Importance == "nice").ToList();
            result.NiceTotal = nice.Count;
            result.NiceMet = nice.Count(i => i.Strength is "strong" or "weak");
            (result.Verdict, result.Advice) = Summarize(result);
            return result;
        }

        private MatchItem MatchOne(JobRequirement requirement, ProfileDto profile, DateTime today)
        {
            var item = new MatchItem
            {
                Key = requirement.Key ?? "",
                Text = requirement.Text ?? "",
                Importance = requirement.Importance ?? "must",
                Category = requirement.Category ?? "other"
            };
            var strict = StrictCategories.Contains(item.Category);
            var sources = Sources(profile, item.Category).ToList();
            var years = Years.Match(item.Text) is { Success: true } y ? int.Parse(y.Groups["n"].Value) : (int?)null;

            // Her gereksinim terimi için eşdeğerleriyle birlikte bütün kaynaklara bak.
            var matchedTerms = new HashSet<string>();
            var evidence = new List<MatchEvidence>();
            foreach (var term in requirement.Terms)
            {
                foreach (var candidate in _dictionary.EvidenceTerms(term))
                    foreach (var source in sources.Where(s => _dictionary.Contains(s.Text, candidate, strict)))
                    {
                        matchedTerms.Add(term);
                        evidence.Add(new MatchEvidence
                        {
                            Kind = source.Kind, SourceId = source.Id, Text = source.Text, Where = source.Where,
                            Term = string.Equals(candidate, term, StringComparison.OrdinalIgnoreCase) ? term : $"{candidate} → {term}",
                            Strength = source.Strength
                        });
                    }
            }

            item.Evidence = evidence
                .GroupBy(e => (e.Kind, e.SourceId, e.Text))
                .Select(g => g.First())
                .OrderBy(e => e.Strength == "strong" ? 0 : 1)
                .ThenBy(e => e.Kind is "achievement" or "projectAchievement" ? 0 : 1)
                .Take(3)
                .ToList();

            if (requirement.Terms.Count == 0 && years == null)
            {
                item.Strength = "unknown";
                item.Note = item.Category == "other"
                    ? "Bu bilgi (askerlik, ehliyet gibi) genelde CV'nin kişisel bilgiler kısmında olur; kasanda yok."
                    : "Bu nitelik anahtar kelimeyle ölçülemiyor. CV'de bunu gösteren somut bir örnek olması yeterli.";
                return item;
            }

            var termStrength = item.Evidence.Count == 0 ? "missing" : item.Evidence.Any(e => e.Strength == "strong") ? "strong" : "weak";
            item.Strength = requirement.Terms.Count == 0 ? "strong" : termStrength;
            item.Note = termStrength switch
            {
                "strong" => $"{Where(item.Evidence)} geçiyor.",
                "weak" => "Kasanda listelenmiş ama bir işte ya da projede kullandığın görünmüyor. Bunu gösteren bir madde kanıtı güçlendirir.",
                _ => MissingNote(item.Category)
            };

            // "Docker ve Kubernetes" gibi birden çok şey isteyen gereksinimde sadece bir kısmı varsa tam kanıt sayılmaz.
            // "ve" bulunan terimle eksik terimin arasında olmalı; "REST API tasarlama ve geliştirme"deki "ve" fiilleri bağlıyor.
            var missingTerms = requirement.Terms.Where(t => !matchedTerms.Contains(t)).ToList();
            var required = missingTerms.Where(m => matchedTerms.Any(t => JoinedByConjunction(item.Text, t, m))).ToList();
            if (item.Strength == "strong" && required.Count > 0)
            {
                item.Strength = "weak";
                item.MissingTerms = required;
                item.Note = $"{string.Join(", ", matchedTerms)} görünüyor; {string.Join(", ", required)} görünmüyor.";
            }

            // Terimsiz yıl şartında ("En az 3 yıl iş deneyimi") Strength yukarıda strong başladı; süre belirleyecek.
            if (years != null && item.Strength != "missing")
                ApplyDuration(item, profile, years.Value, requirement.Terms.Count > 0, today);

            return item;
        }

        // "En az 3 yıl" şartı: terimlerin geçtiği deneyimlerin (terim yoksa staj dışındaki bütün deneyimlerin) toplam süresi.
        // Çakışan işler bir kez sayılıyor.
        private void ApplyDuration(MatchItem item, ProfileDto profile, int years, bool byTerms, DateTime today)
        {
            var relevantIds = item.Evidence.Where(e => e.Kind is "achievement" or "experience").Select(e => e.SourceId).ToHashSet();
            var experiences = profile.Experiences
                .Where(e => byTerms
                    ? relevantIds.Contains(e.Id) || e.Achievements.Any(a => relevantIds.Contains(a.Id))
                    : e.EmploymentType != "internship")
                .ToList();

            if (byTerms && experiences.Count == 0)
            {
                item.Strength = "weak";
                item.Note = $"Biliyorsun ama bir işte ne kadar süre kullandığın görünmüyor; ilan en az {years} yıl istiyor.";
                return;
            }

            var months = TotalMonths(experiences, today);
            var duration = FormatMonths(months);
            item.Evidence.Insert(0, new MatchEvidence { Kind = "duration", Text = $"Toplam {duration}", Where = string.Join(", ", experiences.Select(e => e.Company ?? e.Title)), Strength = months >= years * 12 ? "strong" : "weak" });
            if (months >= years * 12)
            {
                item.Strength = item.Strength == "weak" ? "weak" : "strong";
                if (item.Strength == "strong") item.Note = $"İlgili deneyimin toplam {duration}; ilan en az {years} yıl istiyor.";
            }
            else
            {
                item.Strength = months >= years * 6 ? "weak" : "missing";
                item.Note = months == 0
                    ? $"Kasandaki deneyimlerin süresi hesaplanamadı (tarih eksik olabilir); ilan en az {years} yıl istiyor."
                    : $"İlgili deneyimin toplam {duration}; ilan en az {years} yıl istiyor.";
            }
        }

        private static int TotalMonths(IEnumerable<ExperienceDto> experiences, DateTime today)
        {
            var nowIndex = today.Year * 12 + today.Month - 1;
            var ranges = experiences
                .Select(e => (Start: MonthIndex(e.StartDate, false), End: e.IsCurrent ? nowIndex : MonthIndex(e.EndDate, true)))
                .Where(r => r.Start != null)
                .Select(r => (Start: r.Start!.Value, End: Math.Max(r.Start!.Value, r.End ?? r.Start!.Value)))
                .OrderBy(r => r.Start)
                .ToList();

            var total = 0;
            int? currentStart = null, currentEnd = null;
            foreach (var (start, end) in ranges)
            {
                if (currentEnd != null && start <= currentEnd + 1)
                {
                    currentEnd = Math.Max(currentEnd.Value, end);
                    continue;
                }
                if (currentStart != null) total += currentEnd!.Value - currentStart.Value + 1;
                (currentStart, currentEnd) = (start, end);
            }
            if (currentStart != null) total += currentEnd!.Value - currentStart.Value + 1;
            return total;
        }

        // "2023-04" → ay sırası. Sadece yıl varsa başlangıçta Ocak, bitişte Aralık.
        private static int? MonthIndex(string? date, bool isEnd)
        {
            if (string.IsNullOrEmpty(date) || !int.TryParse(date[..4], out var year)) return null;
            var month = date.Length >= 7 && int.TryParse(date[5..7], out var m) ? m : isEnd ? 12 : 1;
            return year * 12 + month - 1;
        }

        private static string FormatMonths(int months)
        {
            var (y, m) = (months / 12, months % 12);
            return y == 0 ? $"{m} ay" : m == 0 ? $"{y} yıl" : $"{y} yıl {m} ay";
        }

        private static string Where(List<MatchEvidence> evidence)
        {
            var first = evidence.First(e => e.Strength == "strong");
            return first.Kind switch
            {
                "achievement" or "experience" => $"{first.Where} deneyiminde",
                "project" or "projectAchievement" => $"{first.Where} projesinde",
                "education" => "Eğitiminde",
                "certificate" => "Sertifikalarında",
                "skill" => "Becerilerinde",
                _ => "Kasanda"
            };
        }

        // İki terim gereksinim cümlesinde "ve/ile/and" ile mi bağlanmış? "Docker ve CI/CD süreçleri" → evet.
        private static bool JoinedByConjunction(string text, string a, string b)
        {
            var normalized = SkillDictionary.Normalize(text);
            var (na, nb) = (SkillDictionary.Normalize(a), SkillDictionary.Normalize(b));
            var (ia, ib) = (normalized.IndexOf(na, StringComparison.Ordinal), normalized.IndexOf(nb, StringComparison.Ordinal));
            if (ia < 0 || ib < 0) return false;
            var (start, end) = ia < ib ? (ia + na.Length, ib) : (ib + nb.Length, ia);
            return end > start && Conjunction.IsMatch(" " + normalized[start..end] + " ");
        }

        // Eksik gereksinim nasıl kapanır? Teknik ve deneyim eksikleri sorularla, diğerleri kasaya eklenerek.
        private static string MissingNote(string category) => category switch
        {
            "education" => "Eğitim bölümünde bununla eşleşen bir okul ya da bölüm yok.",
            "language" => "Becerilerinde bu dil yok; biliyorsan seviyesiyle birlikte kasana ekle.",
            "certification" => "Sertifikalarında yok; varsa kasana ekle.",
            "other" => "Kasanda yok. Bu bilgi genelde CV'nin kişisel bilgiler kısmında olur.",
            _ => "Kasanda bununla ilgili bir bilgi yok."
        };

        private static (string Verdict, string Advice) Summarize(MatchResult r)
        {
            var verdict = r.MustMeasurable == 0
                ? "İlandaki zorunlu nitelikler anahtar kelimeyle ölçülemiyor."
                : r.MustStrong == 0
                    ? $"Ölçülebilen {r.MustMeasurable} zorunlu gereksinimin hiçbirini kasandaki bilgilerle kanıtlayamıyorsun"
                    : $"Ölçülebilen {r.MustMeasurable} zorunlu gereksinimin {TurkishNumbers.Accusative(r.MustStrong)} kasandaki bilgilerle kanıtlayabiliyorsun";
            if (r.MustMeasurable > 0)
                verdict += r.MustWeak > 0 ? $", {r.MustWeak} tanesi zayıf kalıyor." : ".";
            if (r.NiceTotal > 0) verdict += $" Tercih sebeplerinin {r.NiceMet}/{r.NiceTotal} tanesi var.";

            var advice = r.MustMissing == 0 && r.MustWeak == 0
                ? "İlana güçlü uyuyorsun. CV'yi bu gereksinimler öne çıkacak şekilde düzenlemek yeterli."
                : r.MustMissing <= 2
                    ? "Başvurmaya değer. Eksik ve zayıf görünenler için sana birkaç soru soracağız; belki kasana yazmadığın bir deneyimin vardır."
                    : "Zorunlu gereksinimlerin çoğu kasanda görünmüyor. Sorularla eksik bilgileri tamamlayabiliriz ama bu ilan için temel gereksinimleri karşılamıyor olabilirsin.";
            return (verdict, advice);
        }

        private record Source(string Kind, Guid? Id, string Text, string? Where, string Strength);

        // Kasadaki her metin parçası bir kaynak. Gücü, gereksinimin kategorisine göre değişiyor:
        // dil becerisi listede yazıyorsa dil gereksinimi için yeterli, ama "C#" sadece listede yazıyorsa zayıf.
        private static IEnumerable<Source> Sources(ProfileDto p, string category)
        {
            foreach (var e in p.Experiences)
            {
                // Pozisyon ve şirket adı birlikte: "Lojistik sektöründe deneyim" için "Örnek Lojistik A.Ş." kanıt.
                var where = string.Join(" · ", new[] { e.Title, e.Company }.Where(s => !string.IsNullOrEmpty(s)));
                if (where.Length > 0)
                    yield return new Source("experience", e.Id, where, where, category == "experience" ? "strong" : "weak");
                foreach (var a in e.Achievements.Where(a => !string.IsNullOrEmpty(a.Text)))
                    yield return new Source("achievement", a.Id, a.Text!, where, "strong");
            }
            foreach (var pr in p.Projects)
            {
                if (!string.IsNullOrEmpty(pr.Description))
                    yield return new Source("project", pr.Id, pr.Description, pr.Name, "strong");
                foreach (var a in pr.Achievements.Where(a => !string.IsNullOrEmpty(a.Text)))
                    yield return new Source("projectAchievement", a.Id, a.Text!, pr.Name, "strong");
            }
            foreach (var s in p.Skills.Where(s => !string.IsNullOrEmpty(s.Name)))
                yield return new Source("skill", s.Id, s.Level != null ? $"{s.Name} ({s.Level})" : s.Name!, null, category == "language" ? "strong" : "weak");
            foreach (var c in p.Certificates.Where(c => !string.IsNullOrEmpty(c.Name)))
                yield return new Source("certificate", c.Id, string.Join(" · ", new[] { c.Name, c.Issuer }.Where(x => !string.IsNullOrEmpty(x))), null, category == "certification" ? "strong" : "weak");
            foreach (var ed in p.Educations)
                yield return new Source("education", ed.Id, string.Join(" ", new[] { ed.Field, ed.Degree, ed.School }.Where(x => !string.IsNullOrEmpty(x))), ed.School, category == "education" ? "strong" : "weak");
            var about = string.Join(" ", new[] { p.Headline, p.Summary }.Where(x => !string.IsNullOrEmpty(x)));
            if (about.Length > 0)
                yield return new Source("summary", null, about, "Özet", "weak");
        }
    }
}
