using System.Text.Json;
using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Matching;
using CvTailor.Api.Services.Tailoring;

// Kurgusal CV/ilan çiftleriyle kodun ölçümü: gereksinim eşleştirmesi ne kadar doğru, uydurma koruması
// uydurmaların ne kadarını yakalıyor ve temiz metinde ne kadar yanlış alarm veriyor.
// AI çağrısı yok, her çalıştırmada aynı sonuç. Sözlükte ya da kurallarda değişiklik yapınca önce/sonra buna bakıyorum.
//   dotnet run --project tools/CvTailor.Eval            → rapor
//   dotnet run --project tools/CvTailor.Eval -- --check → eşiklerin altındaysa hata koduyla çık (CI)

const double MinMatchAccuracy = 0.85;
const double MinGuardRecall = 0.85;
const double MaxFalseAlarms = 0.10;
// Süre hesabı bugüne göre; vakalar sabit bir güne göre yazıldı.
var today = new DateTime(2026, 10, 1);

var dir = FindEvalDir();
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var cases = Directory.GetFiles(dir, "*.json").OrderBy(f => f)
    .Select(f => JsonSerializer.Deserialize<EvalCase>(File.ReadAllText(f), json)!)
    .ToList();

var dictionary = SkillDictionary.Load(Path.Combine(AppContext.BaseDirectory, "Knowledge", "synonyms.json"));
var matcher = new RequirementMatcher(dictionary);
var guard = new FabricationGuard(dictionary);

int matchTotal = 0, matchOk = 0, fabricated = 0, caught = 0, clean = 0, falseAlarms = 0;
var misses = new List<string>();

foreach (var c in cases)
{
    AssignIds(c.Profile);
    var analysis = new JobAnalysis { Requirements = c.Requirements.Cast<JobRequirement>().ToList() };
    var result = matcher.Match(analysis, c.Profile, today);
    foreach (var (requirement, item) in c.Requirements.Zip(result.Items))
    {
        matchTotal++;
        if (item.Strength == requirement.Expect) matchOk++;
        else misses.Add($"  eşleşme  {c.Name} / {requirement.Text}: beklenen {requirement.Expect}, çıkan {item.Strength}");
    }

    var terms = c.Requirements.SelectMany(r => r.Terms).ToList();
    foreach (var r in c.Rewrites)
    {
        var flagged = guard.Check(r.Text, r.Source, terms).Status == "flagged";
        if (r.Fabricated)
        {
            fabricated++;
            if (flagged) caught++;
            else misses.Add($"  kaçan    {c.Name}: \"{r.Text}\" ({r.Why})");
        }
        else
        {
            clean++;
            if (flagged) { falseAlarms++; misses.Add($"  alarm    {c.Name}: \"{r.Text}\""); }
        }
    }
}

var accuracy = (double)matchOk / matchTotal;
var recall = (double)caught / fabricated;
var alarmRate = (double)falseAlarms / clean;
Console.WriteLine($"{cases.Count} vaka");
Console.WriteLine($"Eşleştirme doğruluğu      {matchOk}/{matchTotal}  {accuracy:P0}  (eşik {MinMatchAccuracy:P0})");
Console.WriteLine($"Uydurma yakalama          {caught}/{fabricated}  {recall:P0}  (eşik {MinGuardRecall:P0})");
Console.WriteLine($"Temiz metinde yanlış alarm {falseAlarms}/{clean}  {alarmRate:P0}  (en fazla {MaxFalseAlarms:P0})");
if (misses.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Tutmayanlar:");
    misses.ForEach(Console.WriteLine);
}

var passed = accuracy >= MinMatchAccuracy && recall >= MinGuardRecall && alarmRate <= MaxFalseAlarms;
return args.Contains("--check") && !passed ? 1 : 0;

// Repo kökündeki eval/ klasörü; araç hangi klasörden çalıştırılırsa çalıştırılsın bulunsun.
static string FindEvalDir()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, "eval")) && File.Exists(Path.Combine(d.FullName, "CvTailor.sln")))
            return Path.Combine(d.FullName, "eval");
    throw new DirectoryNotFoundException("eval klasörü bulunamadı.");
}

// Eşleştirici kanıtı kayıt Id'siyle takip ediyor; vakalarda Id yazmamak için burada veriliyor.
static void AssignIds(ProfileDto p)
{
    foreach (var e in p.Experiences) { e.Id ??= Guid.NewGuid(); e.Achievements.ForEach(a => a.Id ??= Guid.NewGuid()); }
    foreach (var x in p.Projects) { x.Id ??= Guid.NewGuid(); x.Achievements.ForEach(a => a.Id ??= Guid.NewGuid()); }
    p.Educations.ForEach(e => e.Id ??= Guid.NewGuid());
    p.Skills.ForEach(s => s.Id ??= Guid.NewGuid());
    p.Certificates.ForEach(c => c.Id ??= Guid.NewGuid());
}

class EvalCase
{
    public string Name { get; set; } = "";
    public ProfileDto Profile { get; set; } = new();
    public List<EvalRequirement> Requirements { get; set; } = new();
    public List<EvalRewrite> Rewrites { get; set; } = new();
}

// Beklenen: strong | weak | missing | unknown
class EvalRequirement : JobRequirement
{
    public string Expect { get; set; } = "";
}

// Fabricated: yeni metin kaynakta olmayan bir iddia içeriyor mu (elle etiketlendi). Why: neden uydurma.
class EvalRewrite
{
    public string Source { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Fabricated { get; set; }
    public string? Why { get; set; }
}
