using Avalonia.Media;
using MS43Logger.AvaloniaApp.Controls;

namespace MS43Logger.AvaloniaApp.Tests;

public class MapRampTests
{
    private static void Close(string expected, Color actual)
    {
        var want = Color.Parse(expected);
        Assert.True(Math.Abs(want.R - actual.R) <= 1 && Math.Abs(want.G - actual.G) <= 1 && Math.Abs(want.B - actual.B) <= 1,
            $"expected {expected}, got {actual}");
    }

    [Theory]
    [InlineData(0, "#CCF1EA", "#02423A")]
    [InlineData(1, "#9EDFE0", "#005C5E")]
    [InlineData(2, "#6DC7DB", "#077688")]
    [InlineData(3, "#4FAAD3", "#368EB3")]
    [InlineData(4, "#3D89C6", "#68A4D9")]
    [InlineData(5, "#3568B2", "#97BDF4")]
    [InlineData(6, "#324496", "#C8D6FF")]
    public void StopsMatchTheSystem(int stop, string day, string night)
    {
        Assert.Equal(Color.Parse(day), MapRamp.Color(stop / 6.0, night: false));
        Assert.Equal(Color.Parse(night), MapRamp.Color(stop / 6.0, night: true));
    }

    [Fact]
    public void BetweenStopsTheColoursMixLinearly()
    {
        // Halfway between Day Map1 #CCF1EA and Map2 #9EDFE0.
        Close("#B5E8E5", MapRamp.Color(1.0 / 12, night: false));
    }

    [Fact]
    public void ValuesOutsideTheRangeClampToItsEnds()
    {
        Assert.Equal(Color.Parse("#CCF1EA"), MapRamp.Color(-3, night: false));
        Assert.Equal(Color.Parse("#C8D6FF"), MapRamp.Color(9, night: true));
    }

    [Theory]
    [InlineData("#3D89C6", "#000000")] // Day Map5
    [InlineData("#3568B2", "#FFFFFF")] // Day Map6
    [InlineData("#077688", "#FFFFFF")] // Night Map3
    [InlineData("#368EB3", "#000000")] // Night Map4
    public void TextRuleSwitchesAtTheThreshold(string fill, string text)
    {
        Assert.Equal(Color.Parse(text), MapRamp.TextOn(Color.Parse(fill)));
    }
}
