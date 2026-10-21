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
    public void PerformanceSettingsStartFromTheCarDefaults()
    {
        var settings = AppSettings.Load(_folder);

        Assert.Null(settings.FullPedal);
        Assert.Empty(settings.KmhPerRpm);
        Assert.Empty(settings.GpsChecked);
        Assert.Equal(1480, settings.TestMassKg);
        Assert.Equal("205/55 R16", settings.TyreSize);
        Assert.False(settings.Automatic);
        Assert.Null(settings.PerformanceRailWidth);
        Assert.False(settings.PerformanceRailCollapsed);
        Assert.Empty(settings.PerformanceBands);
    }

    [Fact]
    public void PerformanceSettingsSurviveARestart()
    {
        var settings = AppSettings.Load(_folder);
        settings.FullPedal = 96.5;
        settings.KmhPerRpm[3] = 0.02231;
        settings.KmhPerRpm[5] = 0.03712;
        settings.GpsChecked[3] = true;
        settings.GpsChecked[5] = false;
        settings.TestMassKg = 1550;
        settings.TyreSize = "225/45 R17";
        settings.Automatic = true;
        settings.PerformanceRailWidth = 312;
        settings.PerformanceRailCollapsed = true;
        settings.PerformanceBands["pull3"] = [2000, 6954];
        Assert.True(settings.Save());

        var again = AppSettings.Load(_folder);

        Assert.Equal(96.5, again.FullPedal);
        Assert.Equal(new[] { (3, 0.02231), (5, 0.03712) }, again.KmhPerRpm.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)));
        Assert.Equal(new[] { (3, true), (5, false) }, again.GpsChecked.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)));
        Assert.Equal(1550, again.TestMassKg);
        Assert.Equal("225/45 R17", again.TyreSize);
        Assert.True(again.Automatic);
        Assert.Equal(312, again.PerformanceRailWidth);
        Assert.True(again.PerformanceRailCollapsed);
        Assert.Equal(new[] { 2000.0, 6954.0 }, Assert.Single(again.PerformanceBands, pair => pair.Key == "pull3").Value);
    }

    [Fact]
    public void ImpossiblePerformanceValuesFallBackAndKeepTheRest()
    {
        File.WriteAllText(SettingsFile, """
            {"LastBin":"C:\\x.bin","TestMassKg":0,"FullPedal":"NaN","TyreSize":"junk",
             "KmhPerRpm":{"3":0.0223,"4":-1,"5":"NaN","9":0.03},
             "PerformanceBands":{"pull3":[6000,2000],"pull2":[1500,6400],"pull4":[1500]}}
            """);

        var settings = AppSettings.Load(_folder);

        Assert.Equal(1480, settings.TestMassKg);
        Assert.Null(settings.FullPedal);
        Assert.Equal("205/55 R16", settings.TyreSize);
        Assert.Equal(new[] { (3, 0.0223) }, settings.KmhPerRpm.Select(pair => (pair.Key, pair.Value)));
        Assert.Equal(new[] { "pull2" }, settings.PerformanceBands.Keys);
        Assert.Equal(@"C:\x.bin", settings.LastBin);
    }

    // A NaN in memory (a division by a zero reading) must neither stop the save nor come back.
    [Fact]
    public void NaNIsSavedAndReadBackAsTheDefault()
    {
        var settings = AppSettings.Load(_folder);
        settings.FullPedal = double.NaN;
        settings.TestMassKg = double.NaN;
        settings.KmhPerRpm[4] = double.PositiveInfinity;

        Assert.True(settings.Save());
        var again = AppSettings.Load(_folder);

        Assert.Null(again.FullPedal);
        Assert.Equal(1480, again.TestMassKg);
        Assert.Empty(again.KmhPerRpm);
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(131.0)]
    public void FullPedalOutsideTenTo130IsForgotten(double pedal)
    {
        File.WriteAllText(SettingsFile, $$"""{"FullPedal":{{pedal.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"TestMassKg":3600}""");

        var settings = AppSettings.Load(_folder);

        Assert.Null(settings.FullPedal);
        Assert.Equal(1480, settings.TestMassKg);
    }

    [Theory]
    [InlineData("205/55 R16", 1.926)]
    [InlineData("205/55R16", 1.926)]
    [InlineData(" 225/45 zr17 ", 1.933)]
    [InlineData("junk", null)]
    [InlineData("205/55", null)]
    public void TyreSizeGivesTheRollingCircumference(string size, double? metres)
    {
        var circumference = MS43Logger.Core.Performance.RunMath.TyreCircumference(size);

        if (metres is null)
        {
            Assert.Null(circumference);
        }
        else
        {
            Assert.Equal(metres.Value, circumference!.Value, 0.0005);
        }
    }

    // A file from before the GPS check was kept per gear (minor review, findings 9 and 23): its one flag holds for the gears it
    // had saved, and the old flag is not written again.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnOlderGpsFlagHoldsForTheSavedGears(bool gpsChecked)
    {
        File.WriteAllText(SettingsFile, $$"""{"KmhPerRpm":{"3":0.0223,"5":0.0371},"SpeedGpsChecked":{{(gpsChecked ? "true" : "false")}},"LastBin":"C:\\x.bin"}""");

        var settings = AppSettings.Load(_folder);
        Assert.True(settings.Save());

        Assert.Equal(new[] { (3, gpsChecked), (5, gpsChecked) }, settings.GpsChecked.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)));
        Assert.Equal(@"C:\x.bin", settings.LastBin);
        Assert.DoesNotContain("\"SpeedGpsChecked\"", File.ReadAllText(SettingsFile));
        Assert.Equal(gpsChecked, AppSettings.Load(_folder).GpsChecked[5]);
    }

    // A gear's GPS check goes with its calibration: one without a calibration is dropped.
    [Fact]
    public void AGpsCheckWithoutItsGearIsDropped()
    {
        File.WriteAllText(SettingsFile, """{"KmhPerRpm":{"3":0.0223},"GpsChecked":{"3":true,"4":true,"9":true}}""");

        var settings = AppSettings.Load(_folder);

        Assert.Equal(new[] { (3, true) }, settings.GpsChecked.Select(pair => (pair.Key, pair.Value)));
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
