using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using MS43Logger.AvaloniaApp.Controls;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class RunChartTests
{
    private static readonly Color Ink1 = Color.Parse("#F3F5F7");
    private static readonly Color Ink2 = Color.Parse("#AEB4BD");
    private static readonly Color Ink3 = Color.Parse("#858C97");
    private static readonly Color Pane = Color.Parse("#0A0B0D");

    private static T Show<T>(T control, double width, double height) where T : Control
    {
        control.Width = width;
        control.Height = height;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = width + 40, Height = height + 40, RequestedThemeVariant = ThemeVariant.Dark, Background = new SolidColorBrush(Pane), Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    /// <summary>The rendered pixels of a control's window, read in the control's own coordinates.</summary>
    private sealed class Frame
    {
        private readonly byte[] _bytes;
        private readonly int _stride;
        private readonly bool _rgba;
        private readonly Point _origin;
        private readonly PixelSize _size;

        public Frame(Control control)
        {
            var window = (Window)TopLevel.GetTopLevel(control)!;
            using var frame = Screens.Capture(window)!;
            using var buffer = frame.Lock();
            _stride = buffer.RowBytes;
            _rgba = buffer.Format == PixelFormat.Rgba8888;
            _size = buffer.Size;
            _bytes = new byte[_stride * _size.Height];
            Marshal.Copy(buffer.Address, _bytes, 0, _bytes.Length);
            _origin = control.TranslatePoint(default, window)!.Value;
        }

        public Color this[double x, double y]
        {
            get
            {
                var (px, py) = ((int)Math.Floor(_origin.X + x), (int)Math.Floor(_origin.Y + y));
                if (px < 0 || py < 0 || px >= _size.Width || py >= _size.Height)
                {
                    return Colors.Transparent;
                }

                var at = py * _stride + px * 4;
                return _rgba ? Color.FromRgb(_bytes[at], _bytes[at + 1], _bytes[at + 2]) : Color.FromRgb(_bytes[at + 2], _bytes[at + 1], _bytes[at]);
            }
        }

        public int Count(Rect area, Func<Color, bool> test)
        {
            var count = 0;
            for (var y = Math.Floor(area.Top); y < area.Bottom; y++)
            {
                for (var x = Math.Floor(area.Left); x < area.Right; x++)
                {
                    count += test(this[x, y]) ? 1 : 0;
                }
            }

            return count;
        }
    }

    private static bool Near(Color expected, Color actual, int tolerance) =>
        Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance;

    // ReadoutBandTests.OrangePixels: close to Now.
    private static bool Orange(Color colour) => colour.R > 200 && colour.G is > 80 and < 150 && colour.B < 80;

    // Warn at Night is #FF6259; Now's blue channel stays under 30, so the two never mix up.
    private static bool WarnRed(Color colour) => colour.R > 200 && colour.G is > 60 and < 130 && colour.B is > 60 and < 120;

    // Caution at Night is #E8A33D; Now (#FF7A1A) has a green channel under 130.
    private static bool CautionAmber(Color colour) => colour.R is > 200 and < 250 && colour.G is > 140 and < 185 && colour.B is > 35 and < 90;

    private static double Brightness(Color colour) => colour.R + colour.G + colour.B;

    // A data point's pixel: the axes run from their first to their last tick across the plot.
    private static Point At(RunChart chart, double x, double y)
    {
        static double Read(string label) => double.Parse(label, CultureInfo.InvariantCulture);
        var plot = chart.PlotRect;
        var (x0, x1) = (Read(chart.XTickLabels[0]), Read(chart.XTickLabels[^1]));
        var (y0, y1) = (Read(chart.YTickLabels[0]), Read(chart.YTickLabels[^1]));
        return new Point(plot.Left + (x - x0) / (x1 - x0) * plot.Width, plot.Bottom - (y - y0) / (y1 - y0) * plot.Height);
    }

    private static List<Point> Line(double x0, double y0, double x1, double y1, int count = 101) =>
        Enumerable.Range(0, count).Select(i => new Point(x0 + (x1 - x0) * i / (count - 1), y0 + (y1 - y0) * i / (count - 1))).ToList();

    // Recorded from CurveChart's private Ticks and Format before they moved to ChartScale.
    [AvaloniaFact]
    public void CurveChartTicksAreUnchanged()
    {
        var cases = new (double Min, double Max, double Low, double High, double Step, string[] Labels)[]
        {
            (1500, 7000, 0, 8000, 2000, ["0", "2000", "4000", "6000", "8000"]),
            (0, 39, 0, 40, 10, ["0", "10", "20", "30", "40"]),
            (-23.625, 72, -25, 75, 25, ["-25", "0", "25", "50", "75"]),
            (0.2, 0.9, 0.2, 1, 0.2, ["0.2", "0.4", "0.6", "0.8", "1.0"]),
        };

        foreach (var (min, max, low, high, step, labels) in cases)
        {
            var ticks = ChartScale.Ticks(min, max);

            Assert.Equal((low, high, step), (ticks.Min, ticks.Max, ticks.Step));
            Assert.Equal(labels, ticks.Ticks.Select(tick => ChartScale.Format(tick, ticks.Step)));
        }
    }

    [AvaloniaFact]
    public void RunChartDrawsASolidAndADashedTrace()
    {
        var chart = Show(new RunChart { TraceA = Line(2000, 50, 7000, 150), TraceB = Line(2000, 20, 7000, 100), LabelA = "A", LabelB = "B" }, 400, 240);
        var frame = new Frame(chart);

        // A: Ink1 at every sampled rpm, within 2 px of the line.
        var missed = new List<double>();
        for (var rpm = 2100.0; rpm <= 6900; rpm += 100)
        {
            var at = At(chart, rpm, 50 + (rpm - 2000) / 50);
            if (!Enumerable.Range(-2, 5).Any(dy => Near(Ink1, frame[at.X, at.Y + dy], 24)))
            {
                missed.Add(rpm);
            }
        }

        Assert.Empty(missed);

        // B: its brightest pixel per column is Ink2 on a dash and Pane in a gap.
        var (dashes, gaps) = (0, 0);
        var plot = chart.PlotRect;
        for (var x = plot.Left + 10; x < plot.Right - 10; x++)
        {
            var rpm = 2000 + (x - plot.Left) / plot.Width * 5000;
            var at = At(chart, rpm, 20 + (rpm - 2000) / 5000 * 80);
            var brightest = Enumerable.Range(-1, 3).Select(dy => frame[x, at.Y + dy]).MaxBy(Brightness);
            dashes += Near(Ink2, brightest, 48) ? 1 : 0;
            gaps += Near(Pane, brightest, 12) ? 1 : 0;
        }

        Assert.True(dashes > 60, $"{dashes} dash columns");
        Assert.True(gaps > 15, $"{gaps} gap columns");
    }

    [AvaloniaFact]
    public void RunChartTicksCoverTheData()
    {
        var chart = Show(new RunChart { TraceA = Line(2000, 41.5, 7000, 118.2) }, 400, 240);

        Assert.Equal("2000", chart.XTickLabels[0]);
        Assert.Equal("7000", chart.XTickLabels[^1]);
        Assert.True(double.Parse(chart.YTickLabels[0], CultureInfo.InvariantCulture) <= 41.5);
        Assert.True(double.Parse(chart.YTickLabels[^1], CultureInfo.InvariantCulture) >= 118.2);
    }

    [AvaloniaFact]
    public void LiveRunChartEndsInTheNowDot()
    {
        var chart = Show(new RunChart { TraceA = Line(0, 2000, 4, 5000), TraceB = Line(0, 2000, 6, 6000), IsLive = true }, 400, 240);
        var last = At(chart, 4, 5000);
        var aroundLast = new Rect(last.X - 8, last.Y - 8, 16, 16);

        Assert.True(new Frame(chart).Count(aroundLast, Orange) > 10, "the live trace should end in the Now dot");

        chart.IsLive = false;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, new Frame(chart).Count(new Rect(chart.Bounds.Size), Orange));
    }

    // A run still being recorded keeps its look when samples stop: Ink2 trace, its end an Ink3 ring (as the Tune curve).
    [AvaloniaFact]
    public void StalledRunEndsInAnInk3Ring()
    {
        var chart = Show(new RunChart { TraceA = Line(0, 2000, 4, 5000), IsRunning = true }, 400, 240);
        var last = At(chart, 4, 5000);
        var frame = new Frame(chart);

        Assert.Equal(0, frame.Count(new Rect(chart.Bounds.Size), Orange));
        Assert.True(frame.Count(new Rect(last.X - 5, last.Y - 5, 10, 10), colour => Near(Ink3, colour, 16)) > 4, "an Ink3 ring at the last point");

        // Away from the end, the recorded trace is Ink2, not the stored run's Ink1.
        var middle = At(chart, 2, 3500);
        var column = Enumerable.Range(-2, 5).Select(dy => frame[middle.X, middle.Y + dy]).ToList();
        Assert.DoesNotContain(column, colour => Near(Ink1, colour, 24));
        Assert.Contains(column, colour => Near(Ink2, colour, 24));
    }

    [AvaloniaFact]
    public void EmptyRunChartDrawsOnlyItsFrame()
    {
        var chart = Show(new RunChart(), 400, 240);

        Assert.Empty(chart.XTickLabels);
        Assert.Equal(default, chart.PlotRect);
    }

    // The icon takes the knock flag's colour, Caution, as the run's and the stored result's knock line do:
    // one colour for one event.
    [AvaloniaFact]
    public void CylinderBarsCautionFromOneAndAHalf()
    {
        var bars = Show(new CylinderBars { Values = [0, 0, 0, 1.5, 0, 0], IsLive = true }, 240, 90);
        var frame = new Frame(bars);

        var amber = Enumerable.Range(0, 6).Select(i => frame.Count(new Rect(bars.BarRect(i).Left, 0, bars.BarRect(i).Width, bars.BarRect(i).Top), CautionAmber)).ToList();

        Assert.True(amber[3] > 5, $"{amber[3]} caution pixels above cylinder 4");
        Assert.Equal(new[] { 0, 0, 0, 0, 0 }, amber.Where((_, i) => i != 3));
        Assert.Equal(0, frame.Count(new Rect(bars.Bounds.Size), WarnRed));

        bars.Values = [0, 0, 0, 1.49, 0, 0];
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, new Frame(bars).Count(new Rect(bars.BarRect(3).Left, 0, bars.BarRect(3).Width, bars.BarRect(3).Top), CautionAmber));
    }

    [AvaloniaFact]
    public void IdleBarsHaveNoOrange()
    {
        var bars = Show(new CylinderBars { Values = [0.5, 0, 1.0, 0.75, 0, 1.2] }, 240, 90);

        Assert.Equal(0, new Frame(bars).Count(new Rect(bars.Bounds.Size), Orange));

        // The scan does find the fill while live, so the zero above is not blindness.
        bars.IsLive = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(new Frame(bars).Count(new Rect(bars.Bounds.Size), Orange) > 50);
    }

    [AvaloniaFact]
    public void MissingCylinderHasNoFillAndReadsNa()
    {
        var bars = Show(new CylinderBars { Values = [2, null, 2, 2, 2, 2], Maximum = 4 }, 240, 90);
        var frame = new Frame(bars);

        Assert.Equal(new[] { "1", "n/a", "3", "4", "5", "6" }, bars.Labels);
        Assert.Equal("Knock retard per cylinder", AutomationProperties.GetName(bars));

        // Near the bottom of each bar: Ink2 where there is a value, Track where there is none.
        var bottom = bars.BarRect(1).Bottom - 3;
        Assert.True(Near(Color.Parse("#1F2328"), frame[bars.BarRect(1).Center.X, bottom], 8), $"cylinder 2 is filled: {frame[bars.BarRect(1).Center.X, bottom]}");
        Assert.True(Near(Ink2, frame[bars.BarRect(0).Center.X, bottom], 8), $"cylinder 1 is not filled: {frame[bars.BarRect(0).Center.X, bottom]}");
    }
}
