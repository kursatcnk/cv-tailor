using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace CvTailor.Api.Services.Cv
{
    // Yüklenen CV'den düz metin çıkarıyor. Dosya diske yazılmıyor, sadece bellekte okunuyor.
    public class CvTextExtractor
    {
        public const long MaxFileBytes = 5 * 1024 * 1024;
        // Uzun bir CV bile 15 bin karakteri zor geçiyor; fazlası büyük ihtimalle CV değil.
        public const int MaxTextLength = 30_000;
        private const int MaxPdfPages = 10;

        public CvTextResult Extract(Stream stream, string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var text = extension switch
            {
                ".pdf" => ReadPdf(stream),
                ".docx" => ReadDocx(stream),
                ".txt" or ".md" => new StreamReader(stream, Encoding.UTF8).ReadToEnd(),
                ".doc" => throw new CvImportException("Eski .doc biçimi okunamıyor. Word'de \"Farklı kaydet → .docx\" ile kaydedip tekrar yükle."),
                _ => throw new CvImportException("Sadece PDF, DOCX ya da TXT dosyası yüklenebilir.")
            };
            return FromText(text, fromFile: true);
        }

        public CvTextResult FromText(string text, bool fromFile = false)
        {
            var cleaned = Clean(FixLigatures(text));
            if (cleaned.Length < 80)
                throw new CvImportException(fromFile
                    ? "Dosyadan neredeyse hiç metin çıkmadı. CV taranmış bir görüntü olabilir; metin olarak kaydedilmiş bir PDF ya da DOCX yükle veya metni yapıştır."
                    : "Metin bir CV için çok kısa. CV'nin tamamını yapıştır.");
            if (cleaned.Length > MaxTextLength)
                throw new CvImportException("Metin bir CV için fazla uzun. Sadece CV'ni yükle.");
            return new CvTextResult(cleaned, cleaned.Contains(Unreadable));
        }

        // Okunamayan harflerin yerine konan işaret. AI ayrıştırırken kelimeyi tamamlıyor, kullanıcıya da uyarı çıkıyor.
        public const char Unreadable = '�';

        // Bazı fontlar "fi", "ti", "tt" gibi bitişik harfleri tek glif olarak basıyor. Unicode karşılığı olanlar (ﬁ, ﬂ)
        // düz harflere çevriliyor. Karşılığı hiç verilmemiş olanlar PdfPig'den \0 olarak geliyor; "Geliştirici"
        // "Geliş\0rici" oluyor. Hangi harfler olduğu dosyadan anlaşılamıyor, o yüzden sadece işaretliyorum.
        private static string FixLigatures(string text) => text
            .Replace("ﬀ", "ff").Replace("ﬁ", "fi").Replace("ﬂ", "fl")
            .Replace("ﬃ", "ffi").Replace("ﬄ", "ffl").Replace("ﬅ", "st").Replace("ﬆ", "st")
            .Replace('\0', Unreadable);

        // PdfPig'in düz GetText'i iki sütunlu CV'lerde iki sütunu satır satır birbirine karıştırıyor.
        // Önce kelimeleri bloklara ayırıp (Docstrum) okuma sırasına diziyorum; sol sütun bitmeden sağa geçmiyor.
        private static string ReadPdf(Stream stream)
        {
            try
            {
                using var document = PdfDocument.Open(stream);
                if (document.NumberOfPages > MaxPdfPages)
                    throw new CvImportException($"PDF {document.NumberOfPages} sayfa. CV için en fazla {MaxPdfPages} sayfa okunuyor.");

                var sb = new StringBuilder();
                foreach (var page in document.GetPages())
                {
                    var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters);
                    var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
                    foreach (var block in UnsupervisedReadingOrderDetector.Instance.Get(blocks))
                    {
                        foreach (var line in block.TextLines)
                            sb.AppendLine(line.Text);
                        sb.AppendLine();
                    }
                }
                return sb.ToString();
            }
            catch (CvImportException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Şifreli ya da bozuk PDF'te PdfPig farklı farklı exception'lar atıyor.
                throw new CvImportException("PDF okunamadı. Dosya şifreli ya da bozuk olabilir.", ex);
            }
        }

        // Madde işaretli listeler Word'de ayrı paragraf; her paragrafı ayrı satıra yazınca maddeler korunuyor.
        // Tablolarla yapılmış CV şablonları da paragraf paragraf okunuyor.
        private static string ReadDocx(Stream stream)
        {
            try
            {
                using var document = WordprocessingDocument.Open(stream, false);
                var body = document.MainDocumentPart?.Document?.Body;
                if (body == null) return string.Empty;

                var sb = new StringBuilder();
                foreach (var paragraph in body.Descendants<W.Paragraph>())
                {
                    var line = string.Concat(paragraph.Descendants<W.Text>().Select(t => t.Text));
                    var isListItem = paragraph.ParagraphProperties?.NumberingProperties != null;
                    sb.AppendLine(isListItem && line.Length > 0 ? "• " + line : line);
                }
                return sb.ToString();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new CvImportException("DOCX okunamadı. Dosya bozuk ya da şifreli olabilir.", ex);
            }
        }

        // Fazla boş satırları ve satır sonu boşluklarını at; AI'a giden metin gereksiz uzamasın.
        private static string Clean(string text)
        {
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd());
            var sb = new StringBuilder();
            var blank = 0;
            foreach (var line in lines)
            {
                if (line.Length == 0)
                {
                    if (++blank > 1) continue;
                }
                else blank = 0;
                sb.Append(line).Append('\n');
            }
            return sb.ToString().Trim();
        }
    }

    public record CvTextResult(string Text, bool HasUnreadableLetters);

    // Mesajı doğrudan kullanıcıya gösteriliyor.
    public class CvImportException : Exception
    {
        public CvImportException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
