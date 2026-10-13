using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Cv;
using CvTailor.Api.Services.Tailoring;

namespace CvTailor.Tests
{
    // Ürettiğimiz PDF'i, CV yüklerken kullandığımız okuyucuyla geri okuyoruz: bir işe alım sistemi de böyle okuyacak.
    public class CvPdfRendererTests
    {
        static CvPdfRendererTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        internal static TailoredCvDto Cv()
        {
            var changes = new List<CvChange>
            {
                new() { Id = "summary", Kind = "summary", Before = "", After = "ASP.NET Core ile backend geliştiren yazılım uzmanı." },
                new() { Id = "c1", Before = "Fiyatlandırma servisini geliştirdim.", After = "Fiyatlandırma servisini ASP.NET Core ile geliştirdim; yanıt süresini kısalttım." },
                new() { Id = "c2", Before = "Orijinal madde.", After = "Uydurma %90 iyileşme.", Decision = "pending" },
                new() { Id = "c3", Before = "Eski hâli korunan madde.", After = "Reddedilen yeni hâl.", Decision = "rejected" }
            };
            return new TailoredCvDto
            {
                TargetTitle = "Backend Developer",
                Changes = changes,
                Document = new CvDocument
                {
                    Header = new CvHeader { FullName = "Deniz Aksoy", Headline = "Backend Geliştirici", Email = "deniz@ornek.test", Location = "İzmir" },
                    SummaryChangeId = "summary",
                    SectionOrder = { "experience", "education", "skills" },
                    Experiences = { new CvEntry { Title = "Yazılım Uzmanı", Subtitle = "Örnek Lojistik A.Ş.", Period = "Mart 2022 – Devam ediyor", BulletChangeIds = { "c1", "c2", "c3" } } },
                    Educations = { new CvEducation { School = "Ege Örnek Üniversitesi", Detail = "Bilgisayar Mühendisliği, Lisans" } },
                    Skills = { new CvSkillGroup { Label = "Teknik", Items = { "C#", "SQL Server" } } }
                }
            };
        }

        private static string RoundTrip(TailoredCvDto cv)
        {
            using var stream = new MemoryStream(CvPdfRenderer.Render(cv));
            return new CvTextExtractor().Extract(stream, "cv.pdf").Text;
        }

        [Fact]
        public void Turkish_words_with_ligature_letters_survive_text_extraction()
        {
            var result = new CvTextExtractor().Extract(new MemoryStream(CvPdfRenderer.Render(Cv())), "cv.pdf");

            Assert.False(result.HasUnreadableLetters);
            Assert.Contains("Fiyatlandırma servisini ASP.NET Core ile geliştirdim; yanıt süresini kısalttım.", result.Text.Replace("\n", " "));
            Assert.Contains("Backend Geliştirici", result.Text);
        }

        [Fact]
        public void Pending_and_rejected_bullets_print_the_original()
        {
            var text = RoundTrip(Cv());

            Assert.Contains("Orijinal madde.", text);
            Assert.Contains("Eski hâli korunan madde.", text);
            Assert.DoesNotContain("%90", text);
            Assert.DoesNotContain("Reddedilen", text);
        }

        [Fact]
        public void Sections_come_in_the_planned_order()
        {
            var text = RoundTrip(Cv());

            var order = new[] { "Deniz Aksoy", "DENEYİM", "EĞİTİM", "BECERİLER" }.Select(s => text.IndexOf(s, StringComparison.Ordinal)).ToList();
            Assert.All(order, i => Assert.True(i >= 0));
            Assert.Equal(order.OrderBy(i => i), order);
        }

        [Fact]
        public void Contact_details_are_clickable_and_file_has_a_title()
        {
            var cv = Cv();
            cv.Document.Header.Phone = "0555 000 00 00";
            cv.Document.Header.Links.Add("github.com/deniz-ornek");

            using var pdf = UglyToad.PdfPig.PdfDocument.Open(CvPdfRenderer.Render(cv));
            var links = pdf.GetPage(1).GetHyperlinks().Select(l => l.Uri).ToList();

            Assert.Contains("mailto:deniz@ornek.test", links);
            Assert.Contains("tel:05550000000", links);
            Assert.Contains("https://github.com/deniz-ornek", links);
            Assert.Equal("Deniz Aksoy - Backend Developer CV", pdf.Information.Title);
            Assert.Equal("Deniz Aksoy", pdf.Information.Author);
            // Link olması metni bozmamalı; ATS aynı satırı okuyor.
            Assert.Contains("deniz@ornek.test · 0555 000 00 00 · İzmir · github.com/deniz-ornek", System.Text.RegularExpressions.Regex.Replace(RoundTrip(cv), @"\s+", " "));
        }
    }
}
