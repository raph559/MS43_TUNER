using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public sealed class SaveTests : IDisposable
{
    private static readonly DateTime When = new(2026, 10, 19, 21, 14, 5);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-save-" + Guid.NewGuid().ToString("N"));

    public SaveTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private CalibrationDocument OpenCopy(string name = "CAR.bin")
    {
        var path = Path.Combine(_folder, name);
        File.Copy(TestData.Find(TestData.CarBin)!, path);
        return CalibrationDocument.Open(path, TestData.Find(TestData.Xdf)!);
    }

    private static XdfTable Ignition(CalibrationDocument d) => (XdfTable)d.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf")!;

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void SaveWithoutEditsIsByteIdentical()
    {
        var d = OpenCopy();
        var before = File.ReadAllBytes(d.Path!);

        d.Save(now: When);

        Assert.Equal(before, File.ReadAllBytes(d.Path!));
        Assert.False(File.Exists(Path.Combine(_folder, "CAR.log")));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void SaveChangesOnlyEditedBytes()
    {
        var d = OpenCopy();
        var before = File.ReadAllBytes(d.Path!);
        new EditHistory(d).Apply(TableEdits.Step(d, Ignition(d), new CellRange(0, 0, 0, 0), 1), "Step");

        d.Save(now: When);

        var after = File.ReadAllBytes(d.Path!);
        var differing = Enumerable.Range(0, after.Length).Where(i => after[i] != before[i]).ToList();
        Assert.Equal(new[] { 0x77315 }, differing);
        Assert.False(d.IsModified);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void FirstSaveWritesBakOnce()
    {
        var d = OpenCopy();
        var original = File.ReadAllBytes(d.Path!);
        var history = new EditHistory(d);
        var bak = d.Path + ".bak";

        history.Apply(TableEdits.Step(d, Ignition(d), new CellRange(0, 0, 0, 0), 1), "Step");
        d.Save(now: When);
        history.Apply(TableEdits.Step(d, Ignition(d), new CellRange(0, 1, 0, 1), 1), "Step");
        d.Save(now: When);

        Assert.True(d.BackupWritten);
        Assert.Equal(original, File.ReadAllBytes(bak));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void ReopeningInTheSameRunKeepsTheFirstBak()
    {
        var first = OpenCopy();
        var original = File.ReadAllBytes(first.Path!);
        new EditHistory(first).Apply(TableEdits.Step(first, Ignition(first), new CellRange(0, 0, 0, 0), 1), "Step");
        first.Save(now: When);

        var again = CalibrationDocument.Open(first.Path!, TestData.Find(TestData.Xdf)!);
        new EditHistory(again).Apply(TableEdits.Step(again, Ignition(again), new CellRange(0, 1, 0, 1), 1), "Step");
        again.Save(now: When);

        Assert.Equal(original, File.ReadAllBytes(first.Path + ".bak"));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void SaveAsLeavesOriginalAlone()
    {
        var d = OpenCopy();
        var original = File.ReadAllBytes(d.Path!);
        new EditHistory(d).Apply(TableEdits.Step(d, Ignition(d), new CellRange(0, 0, 0, 0), 1), "Step");
        var target = Path.Combine(_folder, "CAR_v2.bin");

        d.Save(target, When);

        Assert.Equal(original, File.ReadAllBytes(Path.Combine(_folder, "CAR.bin")));
        Assert.Equal(target, d.Path);
        Assert.Equal(81, File.ReadAllBytes(target)[0x77315]);
        Assert.True(File.Exists(Path.Combine(_folder, "CAR_v2.log")));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void FailedSaveKeepsOriginal()
    {
        var d = OpenCopy();
        new EditHistory(d).Apply(TableEdits.Step(d, Ignition(d), new CellRange(0, 0, 0, 0), 1), "Step");
        var missing = Path.Combine(_folder, "gone", "CAR.bin");

        Assert.ThrowsAny<IOException>(() => d.Save(missing, When));

        Assert.True(d.IsModified);
        Assert.Equal(File.ReadAllBytes(TestData.Find(TestData.CarBin)!), File.ReadAllBytes(Path.Combine(_folder, "CAR.bin")));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp", SearchOption.AllDirectories));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void EditLogMatchesTunerProFormat()
    {
        var d = OpenCopy();
        var history = new EditHistory(d);
        history.Apply(TableEdits.Step(d, Ignition(d), new CellRange(0, 0, 0, 0), 1), "Step");
        history.Apply(TableEdits.SetConstant(d, (XdfConstant)d.Xdf.FindByTitle("c_lc_n_max")!, 4000), "Launch");
        history.Apply(TableEdits.SetFlag(d, (XdfFlag)d.Xdf.FindByTitle("lv_mil_ind_knk_lv_1")!, true), "Knock MIL");

        d.Save(now: When);

        Assert.Equal(
            "Edit Log for CAR.bin created by MS43 Tuner.\r\n" +
            new string('*', 74) + "\r\n" +
            "10/19/2026 21:14:05  Table:     ip_iga_ron98_pl__n__maf changed.\r\n" +
            "10/19/2026 21:14:05  Scalar:    c_lc_n_max changed from 4512 - (0x8D) to 4000 - (0x7D).\r\n" +
            "10/19/2026 21:14:05  Flag:      lv_mil_ind_knk_lv_1 changed.\r\n",
            File.ReadAllText(Path.Combine(_folder, "CAR.log")));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void EntriesAppendToAnExistingTunerProLog()
    {
        var d = OpenCopy();
        var log = Path.Combine(_folder, "CAR.log");
        File.WriteAllText(log, "Edit Log for CAR.bin created by TunerPro.\r\n" + new string('*', 74) + "\r\n");
        new EditHistory(d).Apply(TableEdits.Step(d, Ignition(d), new CellRange(0, 0, 0, 0), 1), "Step");

        d.Save(now: When);

        Assert.EndsWith(
            "created by TunerPro.\r\n" + new string('*', 74) + "\r\n10/19/2026 21:14:05  Table:     ip_iga_ron98_pl__n__maf changed.\r\n",
            File.ReadAllText(log));
    }
}
