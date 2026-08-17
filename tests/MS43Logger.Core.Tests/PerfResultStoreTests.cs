using System.Text.Json.Nodes;
using MS43Logger.Core.Performance;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

/// <summary>A clock standing still, reading its time as local (UTC as the local zone), for file names from the time.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

// The saved result and its library are performance spec 8; the shapes are the plan's.
public sealed class PerfResultStoreTests : IDisposable
{
    private static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");
    private static readonly DateTimeOffset Created = new(2026, 10, 21, 14, 32, 12, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-results-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
        else if (File.Exists(_folder))
        {
            File.Delete(_folder);
        }
    }

    internal static AnalysisInputs Inputs(double massKg = 1480) =>
        new(new CarValues(massKg, "205/55 R16", false), new Dictionary<int, double> { [3] = 0.0223, [4] = 0.0298 }, 100);
}
