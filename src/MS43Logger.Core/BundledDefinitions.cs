using System.Reflection;

namespace MS43Logger.Core;

public static class BundledDefinitions
{
    private static readonly IReadOnlyDictionary<string, string> VariantFiles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["b22"] = "Siemens_MS43_Extended_Log_v2.9_M54B22.adx",
            ["b25"] = "Siemens_MS43_Extended_Log_v2.9_M54B25.adx",
            ["b30"] = "Siemens_MS43_Extended_Log_v2.9_M54B30.adx",
        };

    public static IReadOnlyCollection<string> Variants => VariantFiles.Keys.ToArray();

    public static AdxDefinition Load(string variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        if (!VariantFiles.TryGetValue(variant.Trim(), out var fileName))
        {
            throw new InvalidOperationException($"Unknown bundled variant '{variant}'.");
        }

        var assembly = typeof(BundledDefinitions).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new FileNotFoundException($"Embedded ADX '{fileName}' was not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded ADX '{fileName}' could not be opened.");

        return AdxDefinition.Load(stream, fileName);
    }
}
