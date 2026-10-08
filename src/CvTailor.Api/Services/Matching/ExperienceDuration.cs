using CvTailor.Api.Dtos;

namespace CvTailor.Api.Services.Matching
{
    // Deneyim süresi: eşleşme "en az 3 yıl" şartında, yeniden yazım CV özetinde kullanıyor.
    // Özette süreyi AI'a tahmin ettirmiyorum; kod hesaplıyor, AI sadece bu rakamı kullanabiliyor.
    public static class ExperienceDuration
    {
        // Çakışan işler bir kez sayılıyor (aralık birleştirme). Devam eden iş bugüne kadar sayılıyor.
        public static int TotalMonths(IEnumerable<ExperienceDto> experiences, DateTime today)
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
            if (string.IsNullOrEmpty(date) || date.Length < 4 || !int.TryParse(date[..4], out var year)) return null;
            var month = date.Length >= 7 && int.TryParse(date[5..7], out var m) ? m : isEnd ? 12 : 1;
            return year * 12 + month - 1;
        }

        public static string Format(int months)
        {
            var (y, m) = (months / 12, months % 12);
            return y == 0 ? $"{m} ay" : m == 0 ? $"{y} yıl" : $"{y} yıl {m} ay";
        }
    }
}
