using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS43Logger.AvaloniaApp;

/// <summary>Per-user preferences in <c>%AppData%/MS43Tuner/settings.json</c>: last files, the XDF for each firmware, pins, recents.</summary>
public sealed class AppSettings
{
    private const int MaxRecent = 10;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MS43Tuner");

    [JsonIgnore]
    public string Folder { get; private set; } = DefaultFolder;

    public string? LastBin { get; set; }

    /// <summary>XDF path to open for each firmware software id, e.g. "43X001".</summary>
    public Dictionary<string, string> XdfByFirmware { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Pinned { get; set; } = new();

    public List<string> Recent { get; set; } = new();

    public string? Variant { get; set; }

    public bool? Night { get; set; }

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
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, "settings.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, Json));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience; failing to store them must not interrupt tuning.
        }
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
