using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        Converters = { new LenientEnum<ThemeMode>(), new LenientEnum<ReadoutsMode>(), new LenientEnum<TrailMode>() },
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
