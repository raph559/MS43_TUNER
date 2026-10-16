using System.Buffers.Binary;
using Avalonia.Headless.XUnit;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class LiveTuningV7Tests
{
    [AvaloniaFact]
    public async Task WordCurvesAndTablesEditSendAndCopyAsWholeLittleEndianCells()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell) = await TuneScreen.OpenAsync(time: new ManualTicker());
        try
        {
            shell.SelectedTab = ShellViewModel.LiveTuningTab;
            var before = shell.Tune.Document!.Image.ToArray(); var expected = before.ToArray();
            var ecu = new FakeOverlayV4Ecu(before, 7); var page = shell.LiveTuning;
            await page.AttachLine(ecu);
            Assert.Equal("135 supported maps + 3 settings groups · load while RAM fits", page.CatalogueText);
            Assert.Equal(138, page.Maps.Count);
            foreach (var map in OverlayV7.Maps.Skip(127))
            {
                page.Selected = page.Maps[map.Index];
                Assert.True(page.CanSendSelected, page.Selected.CapacityText); Assert.NotNull(page.Editor);
                Assert.Empty(page.ScalarFields); Assert.True(page.HasTableEditor);
                var editor = page.Editor!;
                double Units(int raw) => shell.Tune.Document.Reader.Codec.ToPhysical(editor.Table.Z.Math, raw);
                editor.Selection = new CellRange(0, 0, 0, 0);
                Assert.True(editor.Apply(new EditRequest(EditKind.Set, Units(12345))), editor.Message);
                Assert.Equal(expected, shell.Tune.Document.Image.ToArray());
                await page.SendSelectedAsync(); Assert.Null(page.Problem);
                Assert.Equal(12345, BinaryPrimitives.ReadUInt16LittleEndian(ecu.Copies.AsSpan(ecu.Entries[map.Index].Offset)));
                Assert.True(editor.Apply(new EditRequest(EditKind.Set, Units(23456))), editor.Message);
                await page.SendSelectedAsync(); Assert.Null(page.Problem);
                Assert.Equal(23456, BinaryPrimitives.ReadUInt16LittleEndian(ecu.Copies.AsSpan(ecu.Entries[map.Index].Offset)));
                Assert.True(editor.Apply(new EditRequest(EditKind.Set, Units(34567))), editor.Message);
                await page.CopyToTuneAsync([page.Maps[map.Index]]);
                BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(map.Address), 23456);
                Assert.Equal(expected, shell.Tune.Document.Image.ToArray());
                Assert.True(page.Maps[map.Index].HasUnsent);
            }
            Assert.Equal(680, page.MemoryUsed); Assert.Contains("68 bytes for extra tracking", page.AllocationText);
            page.Selected = page.Maps[127]; page.Editor!.ShowRaw = false; Screens.Save(window, "live-v7-idle");
            page.Selected = page.Maps[136]; page.Editor!.ShowRaw = false; Screens.Save(window, "live-v7-throttle");
            page.Selected = page.Maps[124]; Assert.Equal(8, page.ScalarFields.Count);
            await page.AttachLine(new FakeOverlayV4Ecu(before, 6));
            Assert.Null(page.Problem); Assert.Equal(127, page.Maps.Count);
            page.Selected = page.Maps[124]; Assert.Equal(8, page.ScalarFields.Count);
            Assert.Equal("124 supported maps + 3 settings groups · load while RAM fits", page.CatalogueText);
        }
        finally { shell.LiveTuning.Dispose(); window.Close(); }
    }

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
