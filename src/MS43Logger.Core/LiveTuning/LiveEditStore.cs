using System.Security.Cryptography;
using System.Text.Json;

namespace MS43Logger.Core.LiveTuning;

/// <summary>
/// The live page's edits, kept between sessions (live tuning spec 5.6) in one JSON file: per set of flashed maps (keyed by
/// their hash), per map, the edited copy. Edits made against other flashed bytes are kept apart, to be offered, never
/// applied. A file that cannot be read loads as empty and is left as it is until the next save.
/// </summary>
public sealed class LiveEditStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The SHA-256, in hex, of the five maps' flashed bytes in the overlay's order.</summary>
    public static string FlashKey(IReadOnlyList<byte[]> flash)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var map in flash)
        {
            sha.AppendData(map);
        }

        return Convert.ToHexString(sha.GetHashAndReset());
    }

    /// <summary>The edits kept for these flashed bytes, by map key; empty when there are none.</summary>
    public IReadOnlyDictionary<string, byte[]> Load(string flashKey) =>
        Read().TryGetValue(flashKey, out var edits)
            ? edits.ToDictionary(e => e.Key, e => Convert.FromBase64String(e.Value))
            : new Dictionary<string, byte[]>();

    /// <summary>The flash keys under which an edit of <paramref name="mapKey"/> is kept.</summary>
    public IReadOnlyList<string> KeysHolding(string mapKey) => Read().Where(entry => entry.Value.ContainsKey(mapKey)).Select(entry => entry.Key).ToList();

    /// <summary>The flash keys that hold edits, other than <paramref name="flashKey"/>.</summary>
    public IReadOnlyList<string> OtherKeys(string flashKey) => Read().Keys.Where(k => k != flashKey).ToList();

    /// <summary>Replaces the edits kept for these flashed bytes (none: the key goes); the other keys are kept.</summary>
    public void Save(string flashKey, IReadOnlyDictionary<string, byte[]> edits)
    {
        var all = Read();
        if (edits.Count == 0)
        {
            all.Remove(flashKey);
        }
        else
        {
            all[flashKey] = edits.ToDictionary(e => e.Key, e => Convert.ToBase64String(e.Value));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(all, Options));
        File.Move(temporary, path, overwrite: true);
    }

    private Dictionary<string, Dictionary<string, string>> Read()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch (Exception error) when (error is JsonException or IOException or FormatException)
        {
            return new();
        }
    }
}
