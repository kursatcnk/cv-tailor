using CvTailor.Api.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CvTailor.Api.Services.Tailoring
{
    // Onaylanan CV'nin PDF'i. Tek sütun, gerçek metin, tablo/ikon/görsel yok: işe alım sistemleri (ATS)
    // PDF'i metne çevirirken bilgi kaybolmasın. Her maddenin basılan metni CvChange.FinalText; reddedilen ya da
    // onay bekleyen maddenin orijinali basılıyor.
    public static class CvPdfRenderer
    {
        private const string Text = "#1b1b1f";
        private const string Muted = "#5b5b66";
        private const string Rule = "#d4d4d8";

        // Word çıktısı ve ATS testi de aynı başlıkları kullanıyor.
        public static readonly IReadOnlyDictionary<string, string> SectionTitles = new Dictionary<string, string>
        {
            ["experience"] = "DENEYİM",
            ["projects"] = "PROJELER",
            ["education"] = "EĞİTİM",
            ["skills"] = "BECERİLER",
            ["certificates"] = "SERTİFİKALAR"
        };

        public static byte[] Render(TailoredCvDto cv) => Build(cv).GeneratePdf();

        // Provadaki önizleme için sayfa resimleri. 110 DPI ekranda okunaklı ve bir A4 sayfası 100 KB civarı.
        public static IReadOnlyList<byte[]> Preview(TailoredCvDto cv) =>
            Build(cv).GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 110 }).ToList();

        // PDF'e çevirmeden önceki belge; önizleme görüntüsü üretmek için de kullanılıyor.
        public static IDocument Build(TailoredCvDto cv)
        {
            var doc = cv.Document;
            var changes = cv.Changes.ToDictionary(c => c.Id);
            var metadata = new DocumentMetadata
            {
                Title = Title(cv),
                Author = doc.Header.FullName ?? "",
                Subject = cv.TargetTitle,
                Keywords = string.Join(", ", doc.Skills.SelectMany(g => g.Items).Take(20)),
                Creator = "CV Tailor",
                Producer = "CV Tailor"
            };

            return Document.Create(container => container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.6f, Unit.Centimetre);
                // Bitişik harfler (fi, ti, tt) kapalı. Açıkken bazı okuyucular "Geliştirici"yi "Geliş rici" diye çıkarıyor;
                // CV yükleme tarafında bu yüzden ayrıca uğraşıyoruz, kendi ürettiğimiz PDF'te aynı sorunu yaratmayalım.
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Text).LineHeight(1.3f)
                    .DisableFontFeature(FontFeatures.StandardLigatures)
                    .DisableFontFeature("clig")
                    .DisableFontFeature("dlig"));

                page.Content().Column(column =>
                {
                    column.Spacing(10);
                    Header(column, doc.Header);

                    if (doc.SummaryChangeId != null && changes.TryGetValue(doc.SummaryChangeId, out var summary) && !string.IsNullOrWhiteSpace(summary.FinalText))
                        column.Item().Text(summary.FinalText);

                    foreach (var section in doc.SectionOrder)
                    {
                        switch (section)
                        {
                            case "experience" when doc.Experiences.Count > 0:
                                Section(column, section, c => { foreach (var e in doc.Experiences) Entry(c, e, changes); });
                                break;
                            case "projects" when doc.Projects.Count > 0:
                                Section(column, section, c => { foreach (var p in doc.Projects) Entry(c, p, changes); });
                                break;
                            case "education" when doc.Educations.Count > 0:
                                Section(column, section, c =>
                                {
                                    foreach (var e in doc.Educations)
                                        c.Item().Row(row =>
                                        {
                                            row.RelativeItem().Text(t =>
                                            {
                                                t.Span(e.School).Bold();
                                                if (!string.IsNullOrEmpty(e.Detail)) t.Span($" · {e.Detail}");
                                            });
                                            if (e.Period != null) row.AutoItem().Text(e.Period).FontColor(Muted);
                                        });
                                });
                                break;
                            case "skills" when doc.Skills.Count > 0:
                                Section(column, section, c =>
                                {
                                    foreach (var group in doc.Skills)
                                        c.Item().Text(t =>
                                        {
                                            t.Span($"{group.Label}: ").Bold();
                                            t.Span(string.Join(", ", group.Items));
                                        });
                                });
                                break;
                            case "certificates" when doc.Certificates.Count > 0:
                                Section(column, section, c => { foreach (var cert in doc.Certificates) c.Item().Text(cert); });
                                break;
                        }
                    }
                });
            })).WithMetadata(metadata);
        }

        private static void Header(ColumnDescriptor column, CvHeader h)
        {
            column.Item().Column(c =>
            {
                c.Spacing(2);
                c.Item().Text(h.FullName ?? "").FontSize(18).Bold();
                if (!string.IsNullOrEmpty(h.Headline)) c.Item().Text(h.Headline).FontSize(11).FontColor(Muted);
                // E-posta, telefon ve linkler tıklanabilir; işe alımcı PDF'ten doğrudan yazabilsin.
                c.Item().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(9).FontColor(Muted));
                    var first = true;
                    foreach (var (label, url) in ContactParts(h))
                    {
                        if (!first) t.Span("  ·  ");
                        first = false;
                        if (url == null) t.Span(label); else t.Hyperlink(label, url);
                    }
                });
                if (h.Personal.Count > 0) c.Item().Text(string.Join("  ·  ", h.Personal)).FontSize(9).FontColor(Muted);
            });
        }

        // Başlıktaki iletişim satırı: yazılan metin ve (varsa) gittiği adres. Word çıktısı da aynısını kullanıyor.
        internal static IEnumerable<(string Label, string? Url)> ContactParts(CvHeader h)
        {
            if (!string.IsNullOrWhiteSpace(h.Email)) yield return (h.Email, $"mailto:{h.Email}");
            if (!string.IsNullOrWhiteSpace(h.Phone)) yield return (h.Phone, $"tel:{new string(h.Phone.Where(ch => char.IsDigit(ch) || ch == '+').ToArray())}");
            if (!string.IsNullOrWhiteSpace(h.Location)) yield return (h.Location, null);
            // Linkler CV'de şemasız yazılıyor ("github.com/ali"), adres https ile.
            foreach (var link in h.Links.Where(l => !string.IsNullOrWhiteSpace(l)))
                yield return (link, link.Contains("://") ? link : $"https://{link}");
        }

        // Dosya bilgisindeki başlık: okuyucunun sekme adında ve aramada görünen ad.
        internal static string Title(TailoredCvDto cv) =>
            string.Join(" - ", new[] { cv.Document.Header.FullName, cv.TargetTitle }.Where(s => !string.IsNullOrWhiteSpace(s))) + " CV";

        private static void Section(ColumnDescriptor column, string key, Action<ColumnDescriptor> body)
        {
            column.Item().Column(c =>
            {
                c.Spacing(6);
                c.Item().BorderBottom(0.75f).BorderColor(Rule).PaddingBottom(2).Text(SectionTitles[key]).FontSize(10.5f).Bold().LetterSpacing(0.04f);
                body(c);
            });
        }

        private static void Entry(ColumnDescriptor column, CvEntry entry, IReadOnlyDictionary<string, CvChange> changes)
        {
            column.Item().Column(c =>
            {
                c.Spacing(2);
                c.Item().Row(row =>
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.Span(entry.Title).Bold();
                        if (!string.IsNullOrEmpty(entry.Subtitle)) t.Span($" · {entry.Subtitle}");
                        if (!string.IsNullOrEmpty(entry.Location)) t.Span($" · {entry.Location}").FontColor(Muted);
                    });
                    if (entry.Period != null) row.AutoItem().Text(entry.Period).FontColor(Muted);
                });
                foreach (var id in entry.BulletChangeIds)
                {
                    if (!changes.TryGetValue(id, out var change) || string.IsNullOrWhiteSpace(change.FinalText)) continue;
                    c.Item().PaddingLeft(8).Row(row =>
                    {
                        row.ConstantItem(10).Text("•");
                        row.RelativeItem().Text(change.FinalText);
                    });
                }
            });
        }
    }
}
