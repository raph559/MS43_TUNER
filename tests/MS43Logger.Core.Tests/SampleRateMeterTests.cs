using System.Globalization;
using MS43Logger.Core;

namespace MS43Logger.Core.Tests;

public class SampleRateMeterTests
{
    [Fact]
    public void SteadyIntervalsGiveRateAndStats()
    {
        var meter = new SampleRateMeter();
        foreach (var ms in new[] { 0, 25, 50, 75, 100 })
        {
            meter.Record(TimeSpan.FromMilliseconds(ms));
        }

        var stats = meter.Current!;
        Assert.Equal(40, stats.SamplesPerSecond, 6);
        Assert.Equal(25, stats.AverageMs, 6);
        Assert.Equal(25, stats.MinMs, 6);
        Assert.Equal(25, stats.MaxMs, 6);
        Assert.Equal(4, stats.Intervals);
    }

    [Fact]
    public void WindowKeepsOnlyRecentIntervals()
    {
        var meter = new SampleRateMeter(windowSize: 2);
        foreach (var ms in new[] { 0, 100, 110, 120 })
        {
            meter.Record(TimeSpan.FromMilliseconds(ms));
        }

        Assert.Equal(10, meter.Current!.MaxMs, 6);
        Assert.Equal(2, meter.Current.Intervals);
    }

    [Fact]
    public void NoStatsBeforeTwoSamplesOrAfterReset()
    {
        var meter = new SampleRateMeter();
        meter.Record(TimeSpan.Zero);
        Assert.Null(meter.Current);

        meter.Record(TimeSpan.FromMilliseconds(30));
        meter.Reset();
        Assert.Null(meter.Current);
    }

    [Fact]
    public void CsvLogGivesBaselineRateUnderFrenchLocale()
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            const string csv = "timestamp_utc,sample_index,N\n" +
                "2026-03-10T12:00:00.0000000+00:00,0,800\n2026-03-10T12:00:00.0260000+00:00,1,805\n" +
                "not-a-time,2,810\n2026-03-10T12:00:00.0520000+00:00,3,812\n";
            var stats = LogRateAnalyzer.FromCsv(new StringReader(csv))!;
            Assert.Equal(26, stats.AverageMs, 6);
            Assert.Equal(1000.0 / 26, stats.SamplesPerSecond, 6);
            Assert.Equal(2, stats.Intervals);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
