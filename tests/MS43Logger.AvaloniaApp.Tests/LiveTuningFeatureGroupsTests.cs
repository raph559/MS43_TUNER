using Avalonia.Headless.XUnit;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// The current overlay's groups (V10.4) on its ECU and in its bin: "Pops & bangs" alone (139 is reserved), the three blip
/// groups together, their limits refused before sending, the Fun tab's pages and the bin write. An older overlay, on the
/// ECU or in the bin, shows none of them.
/// </summary>
public sealed class LiveTuningFeatureGroupsTests
{
    private static void Put(ShellViewModel shell, int offset, ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
            shell.Tune.Document!.Image.WriteElement(new XdfData(offset + i, 8, false, true, 1, 1, null), 0, bytes[i]);
    }

    private static void Close(Avalonia.Controls.Window window, ShellViewModel shell)
    {
        shell.LiveTuning.Dispose(); shell.Fun.Dispose(); window.Close();
    }

    private static LiveMapRow Row(LiveTuningViewModel page, int index) => page.Maps.Single(row => row.Map.Index == index);

    private static ValueEditorViewModel Field(IEnumerable<LiveScalarRow> rows, string label) => rows.Single(row => row.Label == label).Editor;

    private static void Choose(FunViewModel fun, string key) => fun.Selected = fun.Features.Single(f => f.Key == key);

    private static (double Low, double High) Range(ValueEditorViewModel field) =>
        (field.Limits.XdfLow ?? field.Limits.StorableMin, field.Limits.XdfHigh ?? field.Limits.StorableMax);

    private static readonly string[] PopsLabels = ["Switch", "Duration", "Ignition angle", "AFR target (petrol)", "Minimum engine speed",
        "Minimum coolant temperature", "Throttle opening", "Minimum vehicle speed"];

    private static readonly string[] BlipLabels = ["Switch", "Signs", "Minimum vehicle speed", "Maximum engine speed", "Maximum time",
        "Blip opening", "Hold opening", "Spark angle"];

    private static readonly string[] GearLabels = ["5th → 4th", "4th → 3rd", "3rd → 2nd", "2nd → 1st", "Tap threshold", "Tap length",
        "Blip lead", "Spark above the target"];

    private static readonly string[] RatioLabels = ["rpm per km/h, 1st", "rpm per km/h, 2nd", "rpm per km/h, 3rd", "rpm per km/h, 4th"];

    private static readonly string[] StabLabels = ["Load time", "Stab size", "Stab length", "Gap", "Rise allowed during stabs"];

    public static TheoryData<int, int> OlderOverlays => new() { { 9, 0 }, { 10, 2 } };

    private sealed class ManualTicker : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
