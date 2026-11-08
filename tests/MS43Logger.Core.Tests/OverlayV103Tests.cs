using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>
/// Protocol 10.3 (header minor 3): V10.2 without the pops stabs. Group 139 stays in the catalogue as a reserved entry, so
/// the IDs after it and the catalogue check hold, every byte limited to 0; pops' opening reads as V9's.
/// </summary>
public class OverlayV103Tests
{
    [Fact]
    public void TheCatalogueKeepsAReservedEntryAt139()
    {
        var v102 = OverlayV10.MapsFor(OverlayV10.V102Minor);
        var v103 = OverlayV10.MapsFor(OverlayV10.V103Minor);
        Assert.Equal(v102.Select(m => (m.Index, m.Address, m.Bytes, m.Flags)), v103.Select(m => (m.Index, m.Address, m.Bytes, m.Flags)));
        var reserved = v103[OverlayV10.Stabs];
        Assert.True(reserved.Reserved);
        Assert.Null(reserved.ScalarGroup);
        Assert.Equal(2, reserved.Flags);
        Assert.Equal(1, v103.Count(m => m.Reserved));
    }

    [Fact]
    public void NoSettingMentionsTheStabs()
    {
        var fields = OverlayV10.MapsFor(OverlayV10.V103Minor).Where(m => m.ScalarGroup is not null).SelectMany(m => m.ScalarGroup!.Fields);
        Assert.DoesNotContain(fields, f => f.Label.Contains("stab", StringComparison.OrdinalIgnoreCase)
            || f.Definition?.Description.Contains("stab", StringComparison.OrdinalIgnoreCase) == true);
        var pops = OverlayV10.MapsFor(OverlayV10.V103Minor)[OverlayV8.Pops].ScalarGroup!;
        Assert.Equal("AFR target (petrol)", pops.Fields.Single(f => f.Offset == 3).Label);   // V10.2's real AFR stays
    }

    [Fact]
    public void Group139TakesOnlyZeros()
    {
        Assert.All(OverlayV10.LimitsFor(OverlayV10.V103Minor)[OverlayV10.Stabs], limit => Assert.Equal(((byte)0, (byte)0), limit));
        var zeros = new byte[8];
        Assert.Null(OverlayV10.ValidateCopy(OverlayV10.Stabs, zeros, zeros, OverlayV10.V103Minor));
        var stab = V10Groups.DefaultsV102.AsSpan(8, 8).ToArray();                // V10.2's flashed stabs
        Assert.Equal(OverlayReason.Range, OverlayV10.ValidateCopy(OverlayV10.Stabs, stab, zeros, OverlayV10.V103Minor));
        Assert.Null(OverlayV10.ValidateCopy(OverlayV10.Stabs, stab, zeros, OverlayV10.V102Minor));     // a V10.2 ECU still has them
    }

    [Fact]
    public void TheBlipKeepsV102sLimits()
    {
        foreach (var group in new[] { OverlayV10.Blip, OverlayV10.BlipGears, OverlayV10.BlipRatios })
            Assert.Equal(OverlayV10.LimitsFor(OverlayV10.V102Minor)[group], OverlayV10.LimitsFor(OverlayV10.V103Minor)[group]);
    }
}
