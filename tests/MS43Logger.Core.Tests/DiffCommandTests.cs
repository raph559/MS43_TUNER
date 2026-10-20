using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class DiffCommandTests
{
    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = DiffCommand.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void DiffWithoutXdfPrintsUsage()
    {
        var (code, output, error) = Run("a.bin", "b.bin");

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.Contains("Usage: ms43-logger diff A.bin B.bin --xdf PATH", error);
    }

    [RealFileFact(TestData.CarBin, TestData.ReleaseBin, TestData.Xdf)]
    public void DiffCarAgainstReleasePrintsTheList()
    {
        var (code, output, error) = Run(TestData.Find(TestData.CarBin)!, TestData.Find(TestData.ReleaseBin)!, "--xdf", TestData.Find(TestData.Xdf)!);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("A: " + TestData.CarBin, output);
        Assert.Contains("c_inj_flow", output);
        Assert.Contains("Bytes outside the XDF", output);
    }

    [RealFileFact(TestData.CarBin, TestData.StockBin, TestData.Xdf)]
    public void DiffAgainstStockWarnsAboutFirmware()
    {
        var (code, _, error) = Run(TestData.Find(TestData.CarBin)!, TestData.Find(TestData.StockBin)!, "--xdf", TestData.Find(TestData.Xdf)!);

        Assert.Equal(0, code);
        Assert.Contains("B is firmware 430069, A is 43X001", error);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void DiffWrongSizeFileFails()
    {
        var small = Path.Combine(Path.GetTempPath(), "ms43-small-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(small, new byte[1024]);
        try
        {
            var (code, _, error) = Run(TestData.Find(TestData.CarBin)!, small, "--xdf", TestData.Find(TestData.Xdf)!);

            Assert.Equal(1, code);
            Assert.Contains("1024 bytes", error);
        }
        finally
        {
            File.Delete(small);
        }
    }
}
