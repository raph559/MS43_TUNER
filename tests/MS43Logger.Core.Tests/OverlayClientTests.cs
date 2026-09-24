using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

public class OverlayClientTests
{

    private static bool Is6D(byte[] payload) => payload[0] == OverlayContract.Command;

    /// <summary>Plan 5's ignition block: +3 steps at rows 10–13 by load columns 12–15 of the RON98 ignition map.</summary>
    private static byte[] IgnitionBlock(byte[] flash)
    {
        var copy = (byte[])flash.Clone();
        foreach (var row in new[] { 10, 11, 12, 13 })
        {
            foreach (var column in new[] { 12, 13, 14, 15 })
            {
                copy[row * 16 + column] += 3;
            }
        }

        return copy;
    }
}
