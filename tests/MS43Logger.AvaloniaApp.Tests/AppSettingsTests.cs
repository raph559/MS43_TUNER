using MS43Logger.AvaloniaApp;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-settings-" + Guid.NewGuid().ToString("N"));

    public AppSettingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string SettingsFile => Path.Combine(_folder, "settings.json");

    [Fact]
    public void FirstRunGetsTheDesignDefaults()
    {
        var settings = AppSettings.Load(_folder);

        Assert.Equal(ThemeMode.Night, settings.Theme);
        Assert.Equal(ReadoutsMode.Tiles, settings.Readouts);
        Assert.Equal(10, settings.TrendSeconds);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MS43 Logs"), settings.LogFolder);
        Assert.False(settings.StartCsvOnConnect);
        Assert.False(settings.OpenMapsRaw);
        Assert.Equal(TrailMode.Short, settings.CursorTrail);
        Assert.True(settings.ReopenLastBin);
        Assert.True(settings.KeepBackup);
        Assert.True(settings.AppendEditLog);
    }

    [Fact]
    public void EverySettingSurvivesARestart()
    {
        var settings = AppSettings.Load(_folder);
        settings.Theme = ThemeMode.FollowWindows;
        settings.Readouts = ReadoutsMode.Dials;
        settings.TrendSeconds = 60;
        settings.LogFolder = @"D:\logs";
        settings.StartCsvOnConnect = true;
        settings.OpenMapsRaw = true;
        settings.CursorTrail = TrailMode.Long;
        settings.ReopenLastBin = false;
        settings.KeepBackup = false;
        settings.AppendEditLog = false;
        Assert.True(settings.Save());

        var again = AppSettings.Load(_folder);

        Assert.Equal(ThemeMode.FollowWindows, again.Theme);
        Assert.Equal(ReadoutsMode.Dials, again.Readouts);
        Assert.Equal(60, again.TrendSeconds);
        Assert.Equal(@"D:\logs", again.LogFolder);
        Assert.True(again.StartCsvOnConnect);
        Assert.True(again.OpenMapsRaw);
        Assert.Equal(TrailMode.Long, again.CursorTrail);
        Assert.False(again.ReopenLastBin);
        Assert.False(again.KeepBackup);
        Assert.False(again.AppendEditLog);
    }

    [Fact]
    public void UnknownValuesFallBackToTheirDefaultsAndKeepTheRest()
    {
        File.WriteAllText(SettingsFile, """{"LastBin":"C:\\x.bin","Theme":"Purple","TrendSeconds":45,"CursorTrail":"Huge","Readouts":7}""");

        var settings = AppSettings.Load(_folder);

        Assert.Equal(ThemeMode.Night, settings.Theme);
        Assert.Equal(10, settings.TrendSeconds);
        Assert.Equal(TrailMode.Short, settings.CursorTrail);
        Assert.Equal(ReadoutsMode.Tiles, settings.Readouts);
        Assert.Equal(@"C:\x.bin", settings.LastBin);
    }

    [Fact]
    public void CorruptFileGivesDefaults()
    {
        File.WriteAllText(SettingsFile, "{not json");

        var settings = AppSettings.Load(_folder);

        Assert.Equal(ThemeMode.Night, settings.Theme);
        Assert.True(settings.KeepBackup);
    }

    [Fact]
    public void FailedSaveIsReported()
    {
        var blocker = Path.Combine(_folder, "file");
        File.WriteAllText(blocker, string.Empty);
        var settings = AppSettings.Load(Path.Combine(blocker, "settings"));

        Assert.False(settings.Save());
        Assert.False(string.IsNullOrWhiteSpace(settings.SaveError));
    }

    [Fact]
    public void SuccessfulSaveClearsTheError()
    {
        var blocker = Path.Combine(_folder, "file");
        File.WriteAllText(blocker, string.Empty);
        var settings = AppSettings.Load(Path.Combine(blocker, "settings"));
        Assert.False(settings.Save());

        File.Delete(blocker);

        Assert.True(settings.Save());
        Assert.Null(settings.SaveError);
    }

    [Fact]
    public void LegacyNightFalseMeansDayAndIsNotWrittenAgain()
    {
        File.WriteAllText(SettingsFile, """{"Night":false}""");

        var settings = AppSettings.Load(_folder);
        Assert.True(settings.Save());

        Assert.Equal(ThemeMode.Day, settings.Theme);
        Assert.DoesNotContain("\"Night\"", File.ReadAllText(SettingsFile));
    }
}
