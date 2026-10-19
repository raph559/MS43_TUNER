namespace MS43Logger.Core.Tests;

/// <summary>
/// Real bins and XDFs live in the git-ignored <c>testdata/local/</c> folder (or the folder named by
/// <c>MS43_TESTDATA</c>). Tests that need them are skipped, never silently passed, when they are absent.
/// </summary>
internal static class TestData
{
    public const string CarBin = "CAR_current_43X001_SP95_STAGE1.bin";
    public const string ReleaseBin = "MS43X001_release_E46_M54B25.bin";
    public const string StockBin = "MS43_430069_stock.bin";
    public const string Xdf = "Siemens_MS43_MS43X001_512K.xdf";

    private static readonly Lazy<string?> Folder = new(FindFolder);

    public static string? Find(string fileName)
    {
        if (Folder.Value is not string folder)
        {
            return null;
        }

        var path = Path.Combine(folder, fileName);
        return File.Exists(path) ? path : null;
    }

    private static string? FindFolder()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("MS43_TESTDATA");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MS43Logger.sln")))
            {
                return Path.Combine(directory.FullName, "testdata", "local");
            }
        }

        return null;
    }
}

public sealed class RealFileFactAttribute : FactAttribute
{
    public RealFileFactAttribute(params string[] files)
    {
        var missing = files.Where(file => TestData.Find(file) is null).ToList();
        if (missing.Count > 0)
        {
            Skip = $"Missing test file(s) in testdata/local: {string.Join(", ", missing)}";
        }
    }
}
