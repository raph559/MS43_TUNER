using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MS43Logger.Core.Performance;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

/// <summary>
/// Per-user preferences in <c>%AppData%/MS43Tuner/settings.json</c>: last files, the XDF for each firmware, pins,
/// recents, and the choices of the Settings dialog.
/// </summary>
public enum ThemeMode
{
    Night,
    Day,
    FollowWindows,
}

public enum ReadoutsMode
{
    Tiles,
    Dials,
}

public enum TrailMode
{
    Off,
    Short,
    Long,
}

public sealed class AppSettings
{
    private const int MaxRecent = 10;
    private static readonly AppSettings Defaults = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        // A NaN or an infinity (a division by a zero reading) is written as such and replaced on load, never failing the save.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new LenientEnum<ThemeMode>(), new LenientEnum<ReadoutsMode>(), new LenientEnum<TrailMode>(), new LenientEnum<ItemSort>(), new LenientEnum<DeltaMode>(), new LenientEnum<EditAxis>() },
    };

    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MS43Tuner");

    /// <summary>Where new, automatically named CSV logs go unless the Settings dialog names another folder.</summary>
    public static string DefaultLogFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MS43 Logs");

    [JsonIgnore]
    public string Folder { get; private set; } = DefaultFolder;

    public string? LastBin { get; set; }

    /// <summary>The bin last opened as B on the Compare tab.</summary>
    public string? LastCompareBin { get; set; }

    /// <summary>XDF path to open for each firmware software id, e.g. "43X001".</summary>
    public Dictionary<string, string> XdfByFirmware { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Pinned { get; set; } = new();

    public List<string> Recent { get; set; } = new();

    public string? Variant { get; set; }

    public string? LastPort { get; set; }

    /// <summary>Read once from older files, where false meant the Day theme; never written again.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Night { get; set; }

    public ThemeMode Theme { get; set; } = ThemeMode.Night;

    public ReadoutsMode Readouts { get; set; } = ReadoutsMode.Tiles;

    /// <summary>Seconds of history each Live channel keeps for its trend line: 10, 30 or 60.</summary>
    public int TrendSeconds { get; set; } = 10;

    public string LogFolder { get; set; } = DefaultLogFolder;

    public bool StartCsvOnConnect { get; set; }

    public bool OpenMapsRaw { get; set; }

    public TrailMode CursorTrail { get; set; } = TrailMode.Short;

    public bool ReopenLastBin { get; set; } = true;

    public bool KeepBackup { get; set; } = true;

    public bool AppendEditLog { get; set; } = true;

    /// <summary>The Tune rail's width as dragged; null for the default.</summary>
    public double? RailWidth { get; set; }

    public bool RailCollapsed { get; set; }

    /// <summary>How the Tune rail orders categories and items.</summary>
    public ItemSort RailSort { get; set; } = ItemSort.Name;

    public bool RailSortDescending { get; set; }

    /// <summary>The Compare list's width as dragged (null for the default) and whether it is folded to its strip.</summary>
    public double? CompareRailWidth { get; set; }

    public bool CompareRailCollapsed { get; set; }

    /// <summary>How Compare's B − A reads: in the map's units, in stored counts, or as a percentage of A.</summary>
    public DeltaMode CompareDeltaMode { get; set; } = DeltaMode.Units;

    /// <summary>Which way Tune's Interpolate and Smooth work on a map: X, Y or both (TunerPro's choice).</summary>
    public EditAxis EditAxis { get; set; } = EditAxis.XY;

    /// <summary>
    /// The pedal reading (°PVS) with the pedal floored, from the Pedal step; null until calibrated. Full throttle and
    /// every pedal percentage are relative to it (performance spec 4.4).
    /// </summary>
    public double? FullPedal { get; set; }

    /// <summary>Speed per engine speed in each gear (km/h per rpm), from the speed calibration (performance spec 6.5).</summary>
    public Dictionary<int, double> KmhPerRpm { get; set; } = new();

    /// <summary>
    /// What the speedometer (VS) read per rpm in each gear as captured, before a GPS correction: the slip check compares VS
    /// with it, since <see cref="KmhPerRpm"/> holds true speed once corrected (performance spec 4.5, 6.5).
    /// </summary>
    public Dictionary<int, double> SpeedometerKmhPerRpm { get; set; } = new();

    /// <summary>
    /// Whether each calibrated gear was corrected against a GPS speed, not only the speedometer; a gear is saved with its own
    /// capture, so each keeps its own (performance spec 6.5).
    /// </summary>
    public Dictionary<int, bool> GpsChecked { get; set; } = new();

    /// <summary>Read once from older files, where one flag stood for every saved gear; never written again.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SpeedGpsChecked { get; set; }

    /// <summary>The car with its driver, for the power estimate (performance spec 7).</summary>
    public double TestMassKg { get; set; } = 1480;

    /// <summary>A size such as "205/55 R16": its rolling circumference turns rpm into speed.</summary>
    public string TyreSize { get; set; } = "205/55 R16";

    /// <summary>An automatic gearbox (a converter), not the manual.</summary>
    public bool Automatic { get; set; }

    /// <summary>The Performance rail's width as dragged (null for the default) and whether it is folded to its strip.</summary>
    public double? PerformanceRailWidth { get; set; }

    public bool PerformanceRailCollapsed { get; set; }

    /// <summary>The last rpm band (start, end) of each pull preset, so before and after runs compare (performance spec 9.2).</summary>
    public Dictionary<string, double[]> PerformanceBands { get; set; } = new();

    /// <summary>Why the last <see cref="Save"/> failed, or null after a successful one.</summary>
    [JsonIgnore]
    public string? SaveError { get; private set; }

    /// <summary>Reads the settings in <paramref name="folder"/>; a missing or unreadable file gives defaults.</summary>
    public static AppSettings Load(string folder)
    {
        AppSettings settings;
        try
        {
            var path = Path.Combine(folder, "settings.json");
            settings = File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings() : new AppSettings();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            settings = new AppSettings();
        }

        settings.Folder = folder;
        settings.XdfByFirmware = new Dictionary<string, string>(settings.XdfByFirmware, StringComparer.OrdinalIgnoreCase);
        settings.Normalize();
        return settings;
    }

    /// <summary>
    /// Writes the settings. A failure must not interrupt tuning: the choices stay in memory, <see cref="SaveError"/>
    /// says why, and the result is false.
    /// </summary>
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, "settings.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, Json));
            File.Move(path + ".tmp", path, overwrite: true);
            SaveError = null;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SaveError = error.Message;
            return false;
        }
    }

    // A value this version does not know (a hand edit, a later version's choice) falls back to its own default only.
    private void Normalize()
    {
        if (Night == false && Theme == ThemeMode.Night)
        {
            Theme = ThemeMode.Day;
        }

        Night = null;
        Theme = Enum.IsDefined(Theme) ? Theme : Defaults.Theme;
        Readouts = Enum.IsDefined(Readouts) ? Readouts : Defaults.Readouts;
        CursorTrail = Enum.IsDefined(CursorTrail) ? CursorTrail : Defaults.CursorTrail;
        TrendSeconds = TrendSeconds is 10 or 30 or 60 ? TrendSeconds : Defaults.TrendSeconds;
        LogFolder = string.IsNullOrWhiteSpace(LogFolder) ? Defaults.LogFolder : LogFolder;

        // Performance values (performance spec 4.4, 6.5, 7): each impossible one goes alone, so a bad entry never costs the rest.
        FullPedal = FullPedal is >= 10 and <= 130 ? FullPedal : null;
        TestMassKg = TestMassKg is >= 500 and <= 3500 ? TestMassKg : Defaults.TestMassKg;
        // The power estimate reads the size; one it cannot read would leave every run without power.
        TyreSize = RunMath.TyreCircumference(TyreSize ?? "") is null ? Defaults.TyreSize : TyreSize!;
        KmhPerRpm = (KmhPerRpm ?? new()).Where(pair => pair.Key is >= 1 and <= 6 && double.IsFinite(pair.Value) && pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        SpeedometerKmhPerRpm = (SpeedometerKmhPerRpm ?? new()).Where(pair => pair.Key is >= 1 and <= 6 && double.IsFinite(pair.Value) && pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        // An older file's one flag holds for the gears it had saved; a gear's check goes with its calibration.
        if (SpeedGpsChecked is { } legacy && (GpsChecked is null || GpsChecked.Count == 0))
        {
            GpsChecked = KmhPerRpm.Keys.ToDictionary(gear => gear, _ => legacy);
        }

        SpeedGpsChecked = null;
        GpsChecked = (GpsChecked ?? new()).Where(pair => KmhPerRpm.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        PerformanceBands = (PerformanceBands ?? new()).Where(pair => pair.Value is [var start, var end] && double.IsFinite(start) && double.IsFinite(end) && start < end)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>Reads an enum by name; anything else reads as an undefined value, which <see cref="Normalize"/> replaces.</summary>
    private sealed class LenientEnum<T> : JsonConverter<T>
        where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var value) && Enum.IsDefined(value))
            {
                return value;
            }

            reader.Skip();
            return (T)Enum.ToObject(typeof(T), -1);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }

    public void AddRecent(string title)
    {
        Recent.Remove(title);
        Recent.Insert(0, title);
        if (Recent.Count > MaxRecent)
        {
            Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
        }

        Save();
    }

    public void TogglePin(string title)
    {
        if (!Pinned.Remove(title))
        {
            Pinned.Add(title);
        }

        Save();
    }

    public bool IsPinned(string title) => Pinned.Contains(title);

    public IEnumerable<string> PinnedInOrder() => Pinned.ToList();
}
