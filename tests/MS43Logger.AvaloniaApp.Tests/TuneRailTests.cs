using Avalonia.Headless.XUnit;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class TuneRailTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-settings-" + Guid.NewGuid().ToString("N"));

    public TuneRailTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    internal static string? Local(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "testdata", "local", name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static readonly string? Car = Local("CAR_current_43X001_SP95_STAGE1.bin");
    private static readonly string? Stock = Local("MS43_430069_stock.bin");
    private static readonly string? Xdf = Local("Siemens_MS43_MS43X001_512K.xdf");

    private TuneViewModel NewTune() => new(AppSettings.Load(_folder));

    [AvaloniaFact]
    public async Task OpeningCarFileFillsRail()
    {
        if (Car is null || Xdf is null)
        {
            return;
        }

        var tune = NewTune();
        await tune.OpenAsync(Car, Xdf);

        Assert.NotNull(tune.Document);
        Assert.Null(tune.ErrorMessage);
        Assert.Null(tune.FirmwareWarning);
        Assert.Equal("CAR_current_43X001_SP95_STAGE1.bin", tune.PlateFile);
        Assert.Equal("MS43X001", tune.PlateFirmware);
        Assert.Contains(tune.Rail, entry => entry is RailCategory { Name: "Ignition", Count: 156 });
        Assert.DoesNotContain(tune.Rail, entry => entry is RailGroup { Title: "Pinned" });
    }

    [AvaloniaFact]
    public async Task ExpandingACategoryListsItsItems()
    {
        if (Car is null || Xdf is null)
        {
            return;
        }

        var tune = NewTune();
        await tune.OpenAsync(Car, Xdf);
        tune.ToggleCategory("Ignition");

        var items = tune.Rail.OfType<RailItem>().ToList();
        Assert.Equal(156, items.Count);
        Assert.Contains(items, item => item.Item.Title == "ip_iga_ron98_pl__n__maf" && item.Kind == ItemKind.Map);
    }

    [AvaloniaFact]
    public async Task SearchFiltersItems()
    {
        if (Car is null || Xdf is null)
        {
            return;
        }

        var tune = NewTune();
        await tune.OpenAsync(Car, Xdf);
        tune.SearchText = "ip_iga_ron98";

        Assert.Equal(new[] { "ip_iga_ron98_is__n__maf", "ip_iga_ron98_pl__n__maf" }, tune.Rail.OfType<RailItem>().Take(2).Select(item => item.Item.Title));
        Assert.DoesNotContain(tune.Rail, entry => entry is RailCategory);
    }

    [AvaloniaFact]
    public async Task PinAndRecentPersist()
    {
        if (Car is null || Xdf is null)
        {
            return;
        }

        var tune = NewTune();
        await tune.OpenAsync(Car, Xdf);
        var map = tune.Document!.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf")!;
        tune.TogglePin(map);
        tune.SelectedItem = map;

        var settings = AppSettings.Load(_folder);
        Assert.Equal(new[] { "ip_iga_ron98_pl__n__maf" }, settings.Pinned);
        Assert.Equal("ip_iga_ron98_pl__n__maf", settings.Recent[0]);
        Assert.Contains(tune.Rail, entry => entry is RailGroup { Title: "Pinned" });

        var again = NewTune();
        await again.OpenAsync(Car, xdfPath: null);
        Assert.NotNull(again.Document);
        Assert.Contains(again.Rail, entry => entry is RailItem { IsPinned: true } pinned && pinned.Item.Title == "ip_iga_ron98_pl__n__maf");
    }

    [AvaloniaFact]
    public async Task MismatchedXdfShowsWarning()
    {
        if (Stock is null || Xdf is null)
        {
            return;
        }

        var tune = NewTune();
        await tune.OpenAsync(Stock, Xdf);

        Assert.NotNull(tune.Document);
        Assert.Contains("430069", tune.FirmwareWarning);
    }

    [AvaloniaFact]
    public async Task WrongSizeBinShowsError()
    {
        var partial = Path.Combine(_folder, "partial.bin");
        File.WriteAllBytes(partial, new byte[32768]);
        var tune = NewTune();

        await tune.OpenAsync(partial, Xdf ?? partial);

        Assert.Null(tune.Document);
        Assert.Contains("524288", tune.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task MissingXdfAsksForOne()
    {
        if (Car is null)
        {
            return;
        }

        var tune = NewTune();
        await tune.OpenAsync(Car, xdfPath: null);

        Assert.Null(tune.Document);
        Assert.Contains("43X001", tune.ErrorMessage);
    }
}
