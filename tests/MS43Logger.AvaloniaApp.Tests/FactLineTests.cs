using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using MS43Logger.AvaloniaApp.Shell;

namespace MS43Logger.AvaloniaApp.Tests;

public class FactLineTests
{
    private static (FactLine Line, TextBlock Last) Line()
    {
        var last = new TextBlock { Text = "Siemens_MS43_MS43X001_512K.xdf", MinWidth = 64, TextTrimming = TextTrimming.PrefixCharacterEllipsis };
        var line = new FactLine { Spacing = 6 };
        line.Children.Add(new TextBlock { Text = "Firmware" });
        line.Children.Add(new TextBlock { Text = "MS43X001" });
        line.Children.Add(new TextBlock { Text = "XDF" });
        line.Children.Add(last);
        line.Measure(Size.Infinity);
        return (line, last);
    }

    [AvaloniaFact]
    public void FactsSitInOneRowWhenTheyFit()
    {
        var (line, last) = Line();
        var natural = line.DesiredSize.Width;

        line.Measure(new Size(natural + 50, 40));
        line.Arrange(new Rect(0, 0, natural + 50, line.DesiredSize.Height));

        Assert.True(line.Fits);
        Assert.Equal(natural, line.DesiredSize.Width, 1);
        Assert.Equal(natural, last.Bounds.Right, 1);
        Assert.Equal(0, line.Children[1].Bounds.Top);
    }

    [AvaloniaFact]
    public void LastFactTrimsFirst()
    {
        var (line, last) = Line();
        var width = line.DesiredSize.Width - 40;

        line.Measure(new Size(width, 40));
        line.Arrange(new Rect(0, 0, width, line.DesiredSize.Height));

        Assert.True(line.Fits);
        Assert.True(last.Bounds.Right <= width + 0.5, $"last fact ends at {last.Bounds.Right}, the line is {width} wide");
        Assert.True(last.TextLayout.Width <= last.Bounds.Width + 0.5, "the last fact's text is drawn past its box");
        Assert.Equal(line.Children[0].DesiredSize.Width, line.Children[0].Bounds.Width, 1);
    }

    [AvaloniaFact]
    public void LineStepsAsideWhenTheLastFactCannotKeepItsMinimum()
    {
        var (line, last) = Line();
        var prefix = line.DesiredSize.Width - last.DesiredSize.Width;

        line.Measure(new Size(prefix + 40, 40));
        line.Arrange(new Rect(0, 0, prefix + 40, 20));

        Assert.False(line.Fits);
        Assert.Equal(default, line.DesiredSize);
        Assert.True(line.ClipToBounds);
        Assert.All(line.Children, child => Assert.False(child.Bounds.Intersects(new Rect(line.Bounds.Size)), $"{child.Bounds} is still inside the line"));
    }
}
