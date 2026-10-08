using CvTailor.Api.Dtos;
using CvTailor.Api.Services.Privacy;

namespace CvTailor.Tests
{
    public class PiiMaskerTests
    {
        // Kurala uyan ama 1-9 rakamlarından hesaplanmış bir numara: 12345678950
        private const string ValidTckn = "12345678950";

        private static ProfileDto Profile() => new()
        {
            FullName = "Deniz Aksoy",
            Email = "deniz.aksoy@ornek.test",
            Phone = "0555 000 00 00",
            GitHubUrl = "https://github.com/deniz-ornek"
        };

        [Fact]
        public void Known_personal_values_never_reach_the_ai_and_come_back_after()
        {
            var mask = PiiMask.ForProfile(Profile());
            var text = "Deniz Aksoy olarak deniz.aksoy@ornek.test adresinden ve 0555 000 00 00 numarasından ulaşılabilir; kodlar https://github.com/deniz-ornek altında.";

            var masked = mask.Mask(text);

            Assert.DoesNotContain("Deniz", masked);
            Assert.DoesNotContain("ornek.test", masked);
            Assert.DoesNotContain("0555", masked);
            Assert.DoesNotContain("deniz-ornek", masked);
            Assert.Equal(text, mask.Unmask(masked));
        }

        [Fact]
        public void Values_not_in_the_profile_are_found_by_pattern()
        {
            var mask = PiiMask.ForProfile(new ProfileDto());
            var masked = mask.Mask($"İletişim: ali@ornek.test, +90 532 111 22 33, TCKN {ValidTckn}, TR33 0006 1005 1978 6457 8413 26");

            Assert.Contains("[EPOSTA_1]", masked);
            Assert.Contains("[TELEFON_1]", masked);
            Assert.Contains("[TCKN_1]", masked);
            Assert.Contains("[IBAN_1]", masked);
        }

        [Fact]
        public void Ordinary_numbers_in_bullets_are_left_alone()
        {
            var mask = PiiMask.ForProfile(Profile());
            const string bullet = "Günde 1.500 siparişi işleyen servisi 2023'te 12345678951 satırlık veriyle test ettim.";
            // 12345678951 kurala uymuyor, kimlik numarası sayılmamalı.
            Assert.Equal(bullet, mask.Mask(bullet));
        }

        [Fact]
        public void Same_value_gets_the_same_placeholder()
        {
            var mask = PiiMask.ForProfile(new ProfileDto());
            var masked = mask.Mask("a@ornek.test ve yine a@ornek.test");
            Assert.Equal("[EPOSTA_1] ve yine [EPOSTA_1]", masked);
        }

        [Theory]
        [InlineData(ValidTckn, true)]
        [InlineData("12345678951", false)]
        [InlineData("02345678950", false)]
        [InlineData("1234567895", false)]
        public void Tckn_checksum(string value, bool expected) => Assert.Equal(expected, PiiScanner.IsTckn(value));

        [Fact]
        public void Warns_about_id_number_and_home_address()
        {
            var warnings = PiiScanner.Warnings($"T.C. Kimlik No: {ValidTckn}\nAdres: Caferağa Mah. Moda Cad. No: 12 D: 5 Kadıköy");

            Assert.Contains(warnings, w => w.Contains("TC kimlik"));
            Assert.Contains(warnings, w => w.Contains("ev adresi"));
        }

        [Fact]
        public void City_alone_is_not_an_address()
        {
            Assert.Empty(PiiScanner.Warnings("İstanbul, Kadıköy. Atatürk Caddesi'ndeki şubede çalıştım."));
        }
    }
}
