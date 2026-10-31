using Avalonia.Headless.XUnit;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>Live tuning page behaviours that were once missing.</summary>
public sealed class LiveTuningEdgeCaseTests : IDisposable
{
    private readonly string _folder = TestShell.NewFolder();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private LiveEditStore Store => new(Path.Combine(_folder, "live-edits.json"));

    private async Task<TuneViewModel> Tune()
    {
        var tune = new TuneViewModel(AppSettings.Load(_folder));
        await tune.OpenAsync(TuneScreen.Car!, TuneScreen.Xdf);
        return tune;
    }

    private static void Raise(LiveTuningViewModel page, int map, int steps)
    {
        page.Selected = page.Maps[map];
        var editor = page.Editor!;
        editor.Selection = new CellRange(0, 0, editor.Values.Rows - 1, editor.Values.Cols - 1);
        Assert.True(editor.Apply(new EditRequest(EditKind.Step, steps)), editor.Message);
    }

    private static byte[] EcuMap(int index) => File.ReadAllBytes(TuneScreen.Car!).AsSpan(OverlayContract.Maps[index].Address, OverlayContract.Maps[index].Bytes).ToArray();

    /// <summary>A line whose answers wait for the test: detection can be caught half-way.</summary>
    private sealed class GatedLine(IDs2Line inner, SemaphoreSlim gate) : IDs2Line
    {
        public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400)
        {
            gate.Wait();
            return inner.Request(payload, listenMilliseconds);
        }
    }
}
