using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

/// <summary>Raw entry, pasting, the bin save's backup, and Copy from B across firmware.</summary>
public sealed class EntrySaveAndCopyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-entry-" + Guid.NewGuid().ToString("N"));

    public EntrySaveAndCopyTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        foreach (var file in Directory.Exists(_folder) ? Directory.GetFiles(_folder) : [])
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static CalibrationDocument Car() => CalibrationDocument.Open(TestData.Find(TestData.CarBin)!, TestData.Find(TestData.Xdf)!);

    private static XdfTable Ignition(CalibrationDocument d) => (XdfTable)d.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf")!;

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void SetRawStoresTheRawCountAsTyped()
    {
        var d = Car();
        var map = Ignition(d);
        var plan = TableEdits.SetRaw(d, map, new CellRange(0, 0, 1, 1), 152);

        Assert.NotEmpty(plan.Changes);
        Assert.All(plan.Changes, change => Assert.Equal(152, change.NewRaw));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void SetRawRefusesACountTheElementCannotHold()
    {
        var d = Car();
        var map = Ignition(d);
        var error = Assert.Throws<EditRefusedException>(() => TableEdits.SetRaw(d, map, new CellRange(0, 0, 0, 0), 256));
        Assert.Contains("Nothing was changed", error.Message);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void AddRawAddsRawCounts()
    {
        var d = Car();
        var map = Ignition(d);
        var before = d.Reader.ReadTable(map).Raw[0, 0];
        var plan = TableEdits.AddRaw(d, map, new CellRange(0, 0, 0, 0), -3);
        Assert.Equal(before - 3, Assert.Single(plan.Changes).NewRaw);
    }

    [Theory]
    [InlineData("1,234.5\t1.234,5\t12,5", new[] { 1234.5, 1234.5, 12.5 })]
    [InlineData("1 234,5\t2", new[] { 1234.5, 2.0 })]
    [InlineData("1,234,567\t-3", new[] { 1234567.0, -3.0 })]
    [InlineData("1 2 3", new[] { 1.0, 2.0, 3.0 })]
    [InlineData("12,5;7", new[] { 12.5, 7.0 })]
    public void PastedNumbersReadThousandsGroups(string text, double[] expected)
    {
        var values = TableClipboard.Parse(text);
        Assert.Equal(expected, Enumerable.Range(0, values.GetLength(1)).Select(col => values[0, col]));
    }

    [Fact]
    public void ACopiedNotANumberPastesAsNaN()
    {
        var values = TableClipboard.Parse("n/a\t3");
        Assert.True(double.IsNaN(values[0, 0]));
        Assert.Equal(3, values[0, 1]);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void PastingNotANumberKeepsThatCell()
    {
        var d = Car();
        var map = Ignition(d);
        var before = d.Reader.ReadTable(map);
        var plan = TableEdits.Paste(d, map, 0, 0, TableClipboard.Parse($"n/a\t{(before.Physical[0, 1] + 3).ToString(System.Globalization.CultureInfo.InvariantCulture)}"));

        Assert.DoesNotContain(plan.Changes, change => change is { Row: 0, Col: 0 });
        Assert.Contains(plan.Changes, change => change is { Row: 0, Col: 1 });
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void AFailedBackupIsTriedAgainOnTheNextSave()
    {
        var path = Path.Combine(_folder, "CAR.bin");
        File.Copy(TestData.Find(TestData.CarBin)!, path);
        var original = File.ReadAllBytes(path);
        var bak = path + ".bak";
        File.WriteAllBytes(bak, [1, 2, 3]);
        File.SetAttributes(bak, FileAttributes.ReadOnly);

        var d = CalibrationDocument.Open(path, TestData.Find(TestData.Xdf)!);
        new EditHistory(d).Apply(TableEdits.Step(d, Ignition(d), new CellRange(0, 0, 0, 0), 1), "Step");
        Assert.Throws<UnauthorizedAccessException>(() => d.Save());
        Assert.Equal(original, File.ReadAllBytes(path));

        File.SetAttributes(bak, FileAttributes.Normal);
        d.Save();

        Assert.Equal(original, File.ReadAllBytes(bak));
        Assert.True(d.BackupWritten);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [RealFileFact(TestData.CarBin, TestData.ReleaseBin, TestData.StockBin, TestData.Xdf)]
    public void CopyFromBIsRefusedAcrossFirmware()
    {
        var d = Car();
        TableEdits.EnsureSameFirmware(d, BinImage.Load(TestData.Find(TestData.ReleaseBin)!));

        var error = Assert.Throws<EditRefusedException>(() => TableEdits.EnsureSameFirmware(d, BinImage.Load(TestData.Find(TestData.StockBin)!)));
        Assert.Contains("430069", error.Message);
        Assert.Contains("43X001", error.Message);
    }
}
