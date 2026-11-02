using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

public class OverlayContractTests
{
    /// <summary>A DS2 frame built here, not by the code under test: 12h, the length, the bytes, their xor.</summary>
    private static byte[] Frame(params byte[] body)
    {
        var frame = new byte[body.Length + 3];
        frame[0] = 0x12;
        frame[1] = (byte)frame.Length;
        body.CopyTo(frame, 2);
        frame[^1] = frame[..^1].Aggregate((byte)0, (x, b) => (byte)(x ^ b));
        return frame;
    }

    [Fact]
    public void Requests_AreTheContractsPayloads()
    {
        Assert.Equal(new byte[] { 0x6D, 0x00 }, OverlayContract.Status());
        Assert.Equal(new byte[] { 0x6D, 0x01, 0x02, 0x01, 0x64 }, OverlayContract.Load(2, 356));
        Assert.Equal(new byte[] { 0x6D, 0x02, 0x01, 0x00, 0x24, 0xAA, 0xBB }, OverlayContract.Write(1, 36, new byte[] { 0xAA, 0xBB }));
        Assert.Equal(new byte[] { 0x6D, 0x03, 0x00, 0xCB, 0xF4, 0x39, 0x26 }, OverlayContract.On(0, 0xCBF43926));
        Assert.Equal(10, OverlayContract.Frame(OverlayContract.On(0, 0xCBF43926)).Length);
        Assert.Equal(new byte[] { 0x6D, 0x04, 0x03 }, OverlayContract.Off(3));
        Assert.Equal(new byte[] { 0x6D, 0x05 }, OverlayContract.AllOn());
        Assert.Equal(new byte[] { 0x6D, 0x06 }, OverlayContract.AllOff());
        Assert.Equal(new byte[] { 0x6D, 0x07, 0x04 }, OverlayContract.Free(4));
        Assert.Equal(new byte[] { 0x6D, 0x08 }, OverlayContract.Revert());
        Assert.Equal(new byte[] { 0x12, 0x05, 0x6D, 0x00, 0x7A }, OverlayContract.Frame(OverlayContract.Status()));
        Assert.Throws<ArgumentException>(() => OverlayContract.Write(0, 0, new byte[121]));
        Assert.Throws<ArgumentException>(() => OverlayContract.Write(0, 0, Array.Empty<byte>()));
    }

    [Fact]
    public void Maps_AreTheFiveInTheOverlaysOrder()
    {
        Assert.Equal(
            new[] { ("ign98_pl", "Timing", 0x77315, 320, false), ("ign98_is", "Timing", 0x775D4, 36, false), ("af98", "Fuel", 0x7D69C, 192, true),
                ("cam_in", "VANOS", 0x76390, 192, false), ("cam_ex", "VANOS", 0x7629C, 192, false) },
            OverlayContract.Maps.Select(m => (m.Key, m.Group, m.Address, m.Bytes, m.RicherOnly)));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, OverlayContract.Maps.Select(m => m.Index));
    }

    [Fact]
    public void Crc32_MatchesIndependentStandardVectors()
    {
        Assert.Equal(0u, OverlayContract.Crc32(Array.Empty<byte>()));
        Assert.Equal(0xCBF43926u, OverlayContract.Crc32("123456789"u8));
        Assert.Equal(0x29058C73u, OverlayContract.Crc32(Enumerable.Range(0, 256).Select(i => (byte)i).ToArray()));
        Assert.NotEqual(OverlayContract.Crc32(new byte[] { 100, 100 }), OverlayContract.Crc32(new byte[] { 103, 97 }));
        Assert.NotEqual(OverlayContract.Crc32(new byte[] { 101, 102 }), OverlayContract.Crc32(new byte[] { 102, 101 }));
    }

    [Fact]
    public void Refusal_ReadsTheReason()
    {
        Assert.Equal(OverlayReason.Integrity, OverlayContract.Refusal(Frame(0xB1, 0x09)));
        Assert.Equal(OverlayReason.EditLimit, OverlayContract.Refusal(Frame(0xB1, 0x0B)));
        Assert.Equal(OverlayReason.Length, OverlayContract.Refusal(Frame(0xB0, 0x01)));
        Assert.Null(OverlayContract.Refusal(new byte[] { 0x12, 0x04, 0xA0, 0xB6 }));
        Assert.Throws<InvalidDataException>(() => OverlayContract.Refusal(new byte[] { 0x12, 0x04, 0xFF, 0xE9 }));
    }

    [Fact]
    public void Header_PreservesTheVersionForAnExplicitUnsupportedMessage()
    {
        byte[] header = { (byte)'L', (byte)'T', (byte)'O', (byte)'V', 0x01, 0x00, 0x54, 0xB5, 0x18, 0xBB, 0x40, 0xBB, 0x05, 0x00, 0x00, 0x00 };
        Assert.Equal(new OverlayHeader(1, 0xB554, 0xBB18, 0xBB40, 5), OverlayHeader.Parse(header));
        Assert.Null(OverlayHeader.Parse(Enumerable.Repeat((byte)0xFF, 16).ToArray()));
        var version2 = (byte[])header.Clone();
        version2[4] = 2;
        Assert.Equal(2, OverlayHeader.Parse(version2)!.Version);
    }

    [Fact]
    public void TableList_GivesTheMapsAddresses()
    {
        byte[] list =
        {
            0x1D, 0x00, 0x15, 0x33, 0x40, 0x01, 0x00, 0x00,
            0x1D, 0x00, 0xD4, 0x35, 0x24, 0x00, 0x00, 0x00,
            0x1F, 0x00, 0x9C, 0x16, 0xC0, 0x00, 0x01, 0x00,
            0x1D, 0x00, 0x90, 0x23, 0xC0, 0x00, 0x00, 0x00,
            0x1D, 0x00, 0x9C, 0x22, 0xC0, 0x00, 0x00, 0x00,
        };
        Assert.Equal(
            new[] { (0x77315, 320, 0), (0x775D4, 36, 0), (0x7D69C, 192, 1), (0x76390, 192, 0), (0x7629C, 192, 0) },
            OverlayHeader.ParseTableList(list));
    }
}
