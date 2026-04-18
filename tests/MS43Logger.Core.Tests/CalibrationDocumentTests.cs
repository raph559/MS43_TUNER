using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class CalibrationDocumentTests
{
    private static XdfData Data(long address, int bits, bool signed = false, bool lsbFirst = true) => new(address, bits, signed, lsbFirst, 1, 1, null);

    private static BinImage Blank() => BinImage.FromBytes(new byte[BinImage.Size]);

    [Fact]
    public void WriteThenReadEachElementKind()
    {
        var image = Blank();
        var cases = new (XdfData Data, long Raw)[]
        {
            (Data(0x70000, 8), 200),
            (Data(0x70001, 8, signed: true), -5),
            (Data(0x70010, 16), 0x1234),
            (Data(0x70020, 16, lsbFirst: false), 0x1234),
            (Data(0x70030, 32, signed: true), -123456),
        };

        foreach (var (data, raw) in cases)
        {
            image.WriteElement(data, 0, raw);
            Assert.Equal(raw, image.ReadElement(data, 0));
        }

        Assert.Equal(0x34, image.Bytes[0x70010]);
        Assert.Equal(0x12, image.Bytes[0x70020]);
    }

    [Fact]
    public void WriteOutsideRangeThrows()
    {
        var image = Blank();

        Assert.Throws<ArgumentOutOfRangeException>(() => image.WriteElement(Data(0x70000, 8), 0, 300));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.WriteElement(Data(0x70000, 8), 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.WriteElement(Data(0x70000, 8, signed: true), 0, 128));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.WriteElement(Data(0x7FFFF, 16), 0, 1));
        Assert.All(image.Bytes.ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void CloneIsIndependentAndDiffRangesMergeAdjacentBytes()
    {
        var image = Blank();
        var copy = image.Clone();
        copy.WriteElement(Data(0x70010, 16), 0, 0xFFFF);
        copy.WriteElement(Data(0x70012, 8), 0, 1);
        copy.WriteElement(Data(0x70100, 8), 0, 9);

        Assert.Equal(0, image.Bytes[0x70010]);
        Assert.Equal(new[] { (0x70010, 3), (0x70100, 1) }, copy.DiffRanges(image));
        Assert.Equal(BinImage.Size, copy.ToArray().Length);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void OpenCarFile()
    {
        var document = CalibrationDocument.Open(TestData.Find(TestData.CarBin)!, TestData.Find(TestData.Xdf)!);

        Assert.Equal("43X001", document.Firmware.SoftwareId);
        Assert.True(document.XdfMatchesFirmware);
        Assert.False(document.IsModified);
        Assert.Empty(document.ModifiedItems());
        Assert.Equal(TestData.Find(TestData.CarBin), document.Path);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void ModifiedItemsListsEditedTableOnly()
    {
        var document = CalibrationDocument.Open(TestData.Find(TestData.CarBin)!, TestData.Find(TestData.Xdf)!);
        var map = (XdfTable)document.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf")!;
        var raised = 0;
        document.Changed += (_, _) => raised++;
        var before = document.Image.ReadElement(map.Z.Data, 5);

        document.Image.WriteElement(map.Z.Data, 5, before + 1);
        document.RaiseChanged();

        Assert.True(document.IsModified);
        Assert.Equal(new XdfItem[] { map }, document.ModifiedItems());
        Assert.Equal(1, raised);
        Assert.Equal(before + 1, document.Reader.ReadTable(map).Raw[0, 5]);

        document.Image.WriteElement(map.Z.Data, 5, before);
        Assert.False(document.IsModified);
    }
}
