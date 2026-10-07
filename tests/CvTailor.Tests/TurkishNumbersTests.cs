using CvTailor.Api.Services.Matching;

namespace CvTailor.Tests
{
    public class TurkishNumbersTests
    {
        [Theory]
        [InlineData(0, "0'ını")]
        [InlineData(1, "1'ini")]
        [InlineData(2, "2'sini")]
        [InlineData(3, "3'ünü")]
        [InlineData(4, "4'ünü")]
        [InlineData(5, "5'ini")]
        [InlineData(6, "6'sını")]
        [InlineData(7, "7'sini")]
        [InlineData(8, "8'ini")]
        [InlineData(9, "9'unu")]
        [InlineData(10, "10'unu")]
        [InlineData(12, "12'sini")]
        [InlineData(20, "20'sini")]
        [InlineData(30, "30'unu")]
        [InlineData(40, "40'ını")]
        [InlineData(60, "60'ını")]
        [InlineData(100, "100'ünü")]
        public void Accusative_follows_how_the_number_is_read(int number, string expected) =>
            Assert.Equal(expected, TurkishNumbers.Accusative(number));
    }
}
