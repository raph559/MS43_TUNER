using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

public sealed class OverlayV3Tests
{

    [Fact]
    public void PatchWindowsPreserveEverythingOutsideTheEightCells()
    {
        foreach (var map in OverlayContract.Maps)
        {
            var current = new byte[map.Bytes];
            var target = Enumerable.Range(0, map.Bytes).Select(i => (byte)(i * 79 + 17)).ToArray();
            foreach (var at in OverlayV3.ChangedWindows(current, target).ToArray())
            {
                Assert.Equal(0, at % 2);
                var next = current.ToArray(); target.AsSpan(at, 8).CopyTo(next.AsSpan(at));
                var packet = OverlayV3.Patch(map.Index, at, 12, 3, current, next);
                Assert.Equal(34, packet.Length); current = next;
            }
            Assert.Equal(target, current);
        }
    }
}
