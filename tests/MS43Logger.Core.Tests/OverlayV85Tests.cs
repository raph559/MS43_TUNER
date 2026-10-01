using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>V8.5: a V8 whose header's version word carries minor 5, and takes the pops throttle opening up to 20.0 °TPS.</summary>
public sealed class OverlayV85Tests
{
    private static readonly byte[] PopsDefaults = [0x00, 0x14, 0x17, 0xB8, 0x32, 0x9E, 0x00, 0x14];

    private static FakeOverlayV4Ecu Ecu(int minor)
    {
        var image = FakeOverlayEcu.Image();
        Array.Fill(image, (byte)219, 0x75D47, 6); image[0x70BFF] = 2;
        PopsDefaults.CopyTo(image, 0x4FF80);
        return new(image, 8) { Minor = minor };
    }

    private static byte[] Throttle(byte value)
    {
        var copy = PopsDefaults.ToArray();
        copy[0] = 1;
        copy[6] = value;
        return copy;
    }

    [Fact]
    public void TheHeaderReadsAsV8WithMinor5()
    {
        byte[] bytes = [.. "LTOV"u8, 0x08, 0x05, 0x54, 0xB5, 0x32, 0xDE, 0x88, 0xFF, 0x8B, 0x00, 0xD8, 0xC0];
        var header = OverlayHeader.Parse(bytes)!;
        Assert.Equal((8, 5, "8.5"), (header.Version, header.Minor, header.Label));
    }

    [Fact]
    public void AV8HeaderHasNoMinor()
    {
        byte[] bytes = [.. "LTOV"u8, 0x08, 0x00, 0x54, 0xB5, 0x86, 0xDE, 0x88, 0xFF, 0x8B, 0x00, 0xD8, 0xC0];
        var header = OverlayHeader.Parse(bytes)!;
        Assert.Equal((8, 0, "8"), (header.Version, header.Minor, header.Label));
    }

    [Fact]
    public void ABinsMinorIsReadFromItsHeader()
    {
        var image = new byte[0x80000];
        "LTOV"u8.CopyTo(image.AsSpan(0x4B544));
        image[0x4B548] = 8; image[0x4B549] = 5;
        Assert.Equal((8, 5), (OverlayHeader.VersionIn(image), OverlayHeader.MinorIn(image)));
    }

    [Fact]
    public void DetectionUsesTheWidePopsDefinition()
    {
        var detection = new OverlayClient(Ecu(5)).Detect();
        Assert.Null(detection.Problem);
        Assert.Equal(OverlayV9.Maps, detection.Catalogue);
        Assert.Equal(OverlayV8.Maps, new OverlayClient(Ecu(0)).Detect().Catalogue);
    }

    [Theory]
    [InlineData(200, true)] [InlineData(201, false)]
    public void V85TakesAThrottleOpeningUpTo200(byte value, bool accepted) =>
        Assert.Equal(accepted, OverlayV9.ValidateFor(8, OverlayV8.Pops, Throttle(value), PopsDefaults, minor: 5) is null);

    [Fact]
    public void V8StillStopsAtFifty() =>
        Assert.Equal(OverlayReason.Range, OverlayV9.ValidateFor(8, OverlayV8.Pops, Throttle(51), PopsDefaults));

    [Fact]
    public void V85RefusesAnLcTriggerAsV8Does()
    {
        var flash = FakeOverlayEcu.Image().AsSpan(0x7132C, 8).ToArray();
        var copy = flash.ToArray(); copy[0] = OverlayV8.AirConOn;
        Assert.NotNull(OverlayV9.ValidateFor(8, OverlayV6.LaunchControl, copy, flash, minor: 5));
    }

    [Fact]
    public void TheClientSendsWhatV85Allows()
    {
        var ecu = Ecu(5);
        var client = new OverlayClient(ecu);
        var detection = client.Detect();
        var flash = detection.Flash[OverlayV8.Pops];
        Assert.Null(client.Send(OverlayV8.Pops, Throttle(200), flash, client.Status()).Refused);
        Assert.Equal(OverlayReason.Range, client.Send(OverlayV8.Pops, Throttle(201), flash, client.Status()).Refused);
    }
}
