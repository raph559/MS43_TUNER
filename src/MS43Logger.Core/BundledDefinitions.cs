using System.Reflection;
using System.Security.Cryptography;

namespace MS43Logger.Core;

public static class BundledDefinitions
{
    private static readonly IReadOnlyDictionary<string, string> VariantFiles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["b22"] = "Siemens_MS43_Extended_Log_v2.9_M54B22.adx",
            ["b25"] = "Siemens_MS43_Extended_Log_v2.9_M54B25.adx",
            ["b30"] = "Siemens_MS43_Extended_Log_v2.9_M54B30.adx",
            ["ms43x"] = "Siemens_MS43_MS43X001_Logging.adx",
        };

    public static IReadOnlyCollection<string> Variants => VariantFiles.Keys.ToArray();

    /// <summary>
    /// The variant whose embedded file <paramref name="sourceName"/> names (a loaded definition's <c>SourceName</c>), so a
    /// result records the definition that decoded its samples; null for a file that is not bundled.
    /// </summary>
    public static string? VariantOf(string? sourceName) =>
        string.IsNullOrWhiteSpace(sourceName)
            ? null
            : VariantFiles.FirstOrDefault(pair => string.Equals(pair.Value, Path.GetFileName(sourceName.Trim()), StringComparison.OrdinalIgnoreCase)).Key;

    public static AdxDefinition Load(string variant)
    {
        using var stream = Open(variant, out var fileName);
        return AdxDefinition.Load(stream, fileName);
    }

    /// <summary>
    /// Lower-case hex SHA-256 of the embedded file, stored with a saved result so its raw payloads can be decoded again
    /// with the same definition (performance spec 8).
    /// </summary>
    public static string Sha256(string variant)
    {
        using var stream = Open(variant, out _);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static Stream Open(string variant, out string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        if (!VariantFiles.TryGetValue(variant.Trim(), out var found))
        {
            throw new InvalidOperationException($"Unknown bundled variant '{variant}'.");
        }

        fileName = found;
        var assembly = typeof(BundledDefinitions).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(found, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new FileNotFoundException($"Embedded ADX '{found}' was not found.");
        }

        return assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded ADX '{found}' could not be opened.");
    }
}
