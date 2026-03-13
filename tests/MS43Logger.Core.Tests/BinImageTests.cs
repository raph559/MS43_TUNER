using System.Text;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class BinImageTests
{
    private static XdfData Data(long address, int bits = 16, bool signed = false, bool lsbFirst = true, int rows = 1, int cols = 1) =>
        new(address, bits, signed, lsbFirst, rows, cols, null);

    private static BinImage Image(Action<byte[]> fill)
    {
        var bytes = new byte[BinImage.Size];
        fill(bytes);
        return BinImage.FromBytes(bytes);
    }

    [Fact]
    public void WrongSizeIsRefused()
    {
        var error = Assert.Throws<InvalidDataException>(() => BinImage.FromBytes(new byte[32768], "partial.bin"));

        Assert.Contains("524288", error.Message);
        Assert.Contains("32768", error.Message);
    }

    [Fact]
    public void ReadsBothByteOrdersSignsAndSizes()
    {
        var image = Image(bytes =>
        {
            bytes[0x77315] = 0x34;
            bytes[0x77316] = 0x12;
            bytes[0x70000] = 0xFF;
            bytes[0x70010] = 0x78;
            bytes[0x70011] = 0x56;
            bytes[0x70012] = 0x34;
            bytes[0x70013] = 0x12;
        });

        Assert.Equal(0x1234, image.ReadElement(Data(0x77315), 0));
        Assert.Equal(0x3412, image.ReadElement(Data(0x77315, lsbFirst: false), 0));
        Assert.Equal(-1, image.ReadElement(Data(0x70000, bits: 8, signed: true), 0));
        Assert.Equal(255, image.ReadElement(Data(0x70000, bits: 8), 0));
        Assert.Equal(0x12345678, image.ReadElement(Data(0x70010, bits: 32), 0));
    }

    [Fact]
    public void RowMajorIndex()
    {
        var image = Image(bytes => bytes[0x77315 + 6] = 0x2A);

        Assert.Equal(0x2A, image.ReadElement(Data(0x77315, rows: 2, cols: 2), 3));
    }

    [Fact]
    public void AddressIsMirrored()
    {
        var image = Image(bytes => bytes[0x77315] = 0x99);

        Assert.Equal(0x77315, BinImage.ToOffset(0xF7315));
        Assert.Equal(0x99, image.ReadElement(Data(0xF7315, bits: 8), 0));
    }

    [Fact]
    public void DataPastTheEndIsNotContained()
    {
        var image = Image(_ => { });
        var data = Data(0x7FFFF);

        Assert.False(image.Contains(data));
        Assert.True(image.Contains(Data(0x7FFFE)));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.ReadElement(data, 0));
    }

    [Fact]
    public void IdentifiesFirmware()
    {
        var withId = Image(bytes => Encoding.ASCII.GetBytes("ca43X001.DAT").CopyTo(bytes, 0x70040));
        var blank = Image(bytes => Array.Fill(bytes, (byte)0xFF));

        Assert.Equal(new FirmwareInfo("43X001", "ca43X001.DAT"), FirmwareIdentifier.Identify(withId));
        Assert.Equal(new FirmwareInfo(null, null), FirmwareIdentifier.Identify(blank));
    }
}
