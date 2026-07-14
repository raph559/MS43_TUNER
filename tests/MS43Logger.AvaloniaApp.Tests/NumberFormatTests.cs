namespace MS43Logger.AvaloniaApp.Tests;

public class NumberFormatTests
{
    [Theory]
    [InlineData(2480.4, 0, "2480")]
    [InlineData(112.41, 1, "112.4")]
    [InlineData(31.13, 1, "31.1")]
    [InlineData(-40, 1, "-40.0")]
    [InlineData(12345.6, 0, "12346")]
    [InlineData(-0.04, 1, "0.0")]
    [InlineData(-0.4, 0, "0")]
    public void FixedDecimalsAsciiMinusNoGrouping(double value, int decimals, string expected) =>
        Assert.Equal(expected, NumberFormat.Format(value, decimals));

    [Fact]
    public void MissingIsNa()
    {
        Assert.Equal("n/a", NumberFormat.Format(null, 1));
        Assert.Equal("n/a", NumberFormat.Format(double.NaN, 1));
    }
}
