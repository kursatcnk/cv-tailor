using CvTailor.Api.Dtos;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CvTailor.Api.Services.Tailoring
{
    // CV'nin Word sürümü. Bazı firmalar ve iş ilanı siteleri hâlâ DOCX istiyor. PDF'le aynı içerik ve sıra;
    // tablo ve metin kutusu yok, maddeler gerçek Word listesi, tarihler sağa hizalı sekmeyle. ATS'ler tabloları ve
    // metin kutularını sık sık atlıyor.
    public static class CvDocxRenderer
    {
        // A4: 11906 twip genişlik, 1,6 cm kenar boşluğu ≈ 907 twip. Sağa hizalı tarih sekmesi metin alanının sonunda.
        private const int Margin = 907;
        private const int RightTab = 11906 - 2 * Margin;
        private const string Muted = "5B5B66";

        public static byte[] Render(TailoredCvDto cv)
        {
            var doc = cv.Document;
            var changes = cv.Changes.ToDictionary(c => c.Id);

            using var stream = new MemoryStream();
            using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = word.AddMainDocumentPart();
                AddStyles(main);
                AddBulletNumbering(main);
                var body = new Body();

                var h = doc.Header;
                body.Append(Paragraph(new[] { Run(h.FullName ?? "", bold: true, size: 36) }, after: 40));
                if (!string.IsNullOrEmpty(h.Headline)) body.Append(Paragraph(new[] { Run(h.Headline, size: 22, color: Muted) }, after: 40));
                var contact = new[] { h.Email, h.Phone, h.Location }.Concat(h.Links).Where(x => !string.IsNullOrEmpty(x));
                body.Append(Paragraph(new[] { Run(string.Join("  ·  ", contact), size: 18, color: Muted) }, after: 160));

                if (doc.SummaryChangeId != null && changes.TryGetValue(doc.SummaryChangeId, out var summary) && !string.IsNullOrWhiteSpace(summary.FinalText))
                    body.Append(Paragraph(new[] { Run(summary.FinalText) }, after: 160));

                foreach (var section in doc.SectionOrder)
                {
                    switch (section)
                    {
                        case "experience" when doc.Experiences.Count > 0:
                            body.Append(Heading(section));
                            foreach (var e in doc.Experiences) AppendEntry(body, e, changes);
                            break;
                        case "projects" when doc.Projects.Count > 0:
                            body.Append(Heading(section));
                            foreach (var p in doc.Projects) AppendEntry(body, p, changes);
                            break;
                        case "education" when doc.Educations.Count > 0:
                            body.Append(Heading(section));
                            foreach (var e in doc.Educations)
                                body.Append(WithDate(new[] { Run(e.School, bold: true), Run(string.IsNullOrEmpty(e.Detail) ? "" : $" · {e.Detail}") }, e.Period));
                            break;
                        case "skills" when doc.Skills.Count > 0:
                            body.Append(Heading(section));
                            foreach (var g in doc.Skills)
                                body.Append(Paragraph(new[] { Run($"{g.Label}: ", bold: true), Run(string.Join(", ", g.Items)) }, after: 40));
                            break;
                        case "certificates" when doc.Certificates.Count > 0:
                            body.Append(Heading(section));
                            foreach (var c in doc.Certificates) body.Append(Paragraph(new[] { Run(c) }, after: 40));
                            break;
                    }
                }

                body.Append(new SectionProperties(
                    new PageSize { Width = 11906, Height = 16838 },
                    new PageMargin { Top = Margin, Bottom = Margin, Left = (uint)Margin, Right = (uint)Margin }));
                main.Document = new Document(body);
            }
            return stream.ToArray();
        }

        private static void AppendEntry(Body body, CvEntry entry, IReadOnlyDictionary<string, CvChange> changes)
        {
            var title = new List<Run> { Run(entry.Title, bold: true) };
            if (!string.IsNullOrEmpty(entry.Subtitle)) title.Add(Run($" · {entry.Subtitle}"));
            if (!string.IsNullOrEmpty(entry.Location)) title.Add(Run($" · {entry.Location}", color: Muted));
            body.Append(WithDate(title, entry.Period));
            foreach (var id in entry.BulletChangeIds)
                if (changes.TryGetValue(id, out var change) && !string.IsNullOrWhiteSpace(change.FinalText))
                    body.Append(Bullet(change.FinalText));
        }

        private static Paragraph Heading(string section)
        {
            var p = Paragraph(new[] { Run(CvPdfRenderer.SectionTitles[section], bold: true, size: 21) }, before: 200, after: 80);
            // OOXML eleman sırası katı: kenarlık, sekme ve liste tanımı satır aralığından önce gelmeli.
            p.ParagraphProperties!.InsertAt(new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "D4D4D8", Space = 1 }), 0);
            return p;
        }

        // Başlık solda, tarih sağa hizalı sekmeyle; tablo kullanmadan iki ucu hizalamanın yolu bu.
        private static Paragraph WithDate(IEnumerable<Run> left, string? period)
        {
            var runs = left.ToList();
            if (!string.IsNullOrEmpty(period))
            {
                runs.Add(new Run(new TabChar()));
                runs.Add(Run(period, color: Muted));
            }
            var p = Paragraph(runs, before: 80, after: 20);
            p.ParagraphProperties!.InsertAt(new Tabs(new TabStop { Val = TabStopValues.Right, Position = RightTab }), 0);
            return p;
        }

        private static Paragraph Bullet(string text)
        {
            var p = Paragraph(new[] { Run(text) }, after: 20);
            p.ParagraphProperties!.InsertAt(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 }), 0);
            return p;
        }

        private static Paragraph Paragraph(IEnumerable<Run> runs, int before = 0, int after = 60)
        {
            var p = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = before.ToString(), After = after.ToString() }));
            p.Append(runs);
            return p;
        }

        // size: yarım punto (20 = 10 pt)
        private static Run Run(string text, bool bold = false, int size = 20, string? color = null)
        {
            var props = new RunProperties();
            if (bold) props.Append(new Bold());
            if (color != null) props.Append(new Color { Val = color });
            props.Append(new FontSize { Val = size.ToString() });
            return new Run(props, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        }

        // Varsayılan yazı tipi; Word'ün kendi stili yerine sade ve her makinede bulunan Calibri.
        private static void AddStyles(MainDocumentPart main)
        {
            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new Styles(new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Calibri" },
                    new Color { Val = "1B1B1F" },
                    new FontSize { Val = "20" },
                    new Languages { Val = "tr-TR" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(new SpacingBetweenLines { Line = "264", LineRule = LineSpacingRuleValues.Auto }))));
        }

        // Tek seviyeli "•" listesi; maddeler Word'de gerçek liste olarak görünüyor.
        private static void AddBulletNumbering(MainDocumentPart main)
        {
            var numbering = main.AddNewPart<NumberingDefinitionsPart>();
            numbering.Numbering = new Numbering(
                new AbstractNum(
                    new Level(
                        new NumberingFormat { Val = NumberFormatValues.Bullet },
                        new LevelText { Val = "•" },
                        new LevelJustification { Val = LevelJustificationValues.Left },
                        new PreviousParagraphProperties(new Indentation { Left = "360", Hanging = "240" }))
                    { LevelIndex = 0 })
                { AbstractNumberId = 1 },
                new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });
        }
    }
}
