namespace CvTailor.Api.Services.Matching
{
    // Sayıdan sonra gelen ek, sayının okunuşuna göre değişiyor: 1'ini (bir), 2'sini (iki), 3'ünü (üç), 6'sını (altı),
    // 10'unu (on), 40'ını (kırk). Değerlendirme cümlelerinde "5 gereksinimin 3'ünü" gibi kullanılıyor.
    public static class TurkishNumbers
    {
        private static readonly string[] Ones = { "", "ini", "sini", "ünü", "ünü", "ini", "sını", "sini", "ini", "unu" };
        private static readonly string[] Tens = { "ını", "unu", "sini", "unu", "ını", "sini", "ını", "ini", "ini", "ını" };

        // Belirtme durumunda iyelik eki: "3'ünü"
        public static string Accusative(int number)
        {
            var n = Math.Abs(number);
            string suffix;
            if (n == 0) suffix = "ını";
            else if (n % 1000 == 0) suffix = "ini";           // bin
            else if (n % 100 == 0) suffix = "ünü";            // yüz
            else if (n % 10 != 0) suffix = Ones[n % 10];
            else suffix = Tens[n / 10 % 10];
            return $"{number}'{suffix}";
        }
    }
}
