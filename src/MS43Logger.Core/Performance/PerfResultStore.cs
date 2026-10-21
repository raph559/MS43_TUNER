using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS43Logger.Core.Performance;

/// <summary>
/// The results folder (performance spec 8): one JSON file per result, named from the clock ("perf-20261021-143212.json",
/// "-2" on a clash), written atomically. The owner's name lives inside the file, so renaming never renames it. A file that is
/// not a result never breaks the list; a newer schema is listed read-only and never rewritten.
/// </summary>
public sealed class PerfResultStore
{
    private const int CurrentSchema = 1;
    private const string Prefix = "perf-";
    private const string Extension = ".json";
    private const string NotAResult = "not a result file";

    /// <summary>
    /// Indented, NaN kept as "NaN", enums by name read in any case. An unknown flag severity reads as suspect, an unknown test
    /// as no test this version knows (the file is then read-only and never opened), any other unknown name as the enum's first
    /// value.
    /// </summary>
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        PropertyNameCaseInsensitive = true,
        Converters = { new LenientEnumConverter() },
    };

    private readonly TimeProvider _time;
    // What List read of each file, by name, with the write time and length it had: only a new or changed file is read again.
    private readonly Dictionary<string, Listed> _listed = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _skipped = [];

    /// <param name="folder">The results folder, derived from the settings folder so tests never touch the owner's data.</param>
    public PerfResultStore(string folder, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = folder;
        _time = time ?? TimeProvider.System;
    }

    public string Folder { get; }

    /// <summary>"perf-…json: not a result file" for each file the last <see cref="List"/> left out.</summary>
    public IReadOnlyList<string> Skipped => _skipped;

    /// <summary>"Not saved: {reason}" and the like after an operation failed; null after one succeeded.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Every result in the folder, newest first. Temporary files are ignored; unreadable files are skipped and named in
    /// <see cref="Skipped"/>. A file is read only when it is new or its write time or length changed since the last listing.
    /// </summary>
    public IReadOnlyList<PerfResultHeader> List()
    {
        var headers = new List<PerfResultHeader>();
        var skipped = new List<string>();
        string[] files;
        try
        {
            files = Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*" + Extension) : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            files = [];
        }

        lock (_listed)
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in files)
            {
                var fileName = Path.GetFileName(path);
                present.Add(fileName);
                try
                {
                    var info = new FileInfo(path);
                    var (written, length) = (info.LastWriteTimeUtc, info.Length);
                    if (!_listed.TryGetValue(fileName, out var listed) || listed.WrittenUtc != written || listed.Length != length)
                    {
                        listed = new Listed(written, length, Header(fileName, File.ReadAllBytes(path)));
                        _listed[fileName] = listed;
                    }

                    if (listed.Header is { } header)
                    {
                        headers.Add(header);
                    }
                    else
                    {
                        skipped.Add($"{fileName}: {NotAResult}");
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Not kept: the next listing tries again.
                    _listed.Remove(fileName);
                    skipped.Add($"{fileName}: could not be read ({Reason(error)})");
                }
            }

            foreach (var gone in _listed.Keys.Where(name => !present.Contains(name)).ToArray())
            {
                _listed.Remove(gone);
            }
        }

        _skipped = skipped;
        return headers
            .OrderByDescending(header => header.Created)
            .ThenByDescending(header => header.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>The result in <paramref name="fileName"/>, a newer schema read as far as this app can; null when it cannot be read.</summary>
    public PerfResult? Load(string fileName)
    {
        LastError = null;
        if (Resolve(fileName) is not { } path)
        {
            LastError = $"Not opened: {fileName} is not a file name";
            return null;
        }

        try
        {
            var result = Read(File.ReadAllBytes(path));
            if (result is null)
            {
                LastError = $"Not opened: {fileName} is {NotAResult}";
            }
            else if (!Enum.IsDefined(result.Spec.Kind))
            {
                // A newer app's test: read as another one, its figures would be recomputed as that test's.
                LastError = $"Not opened: {fileName} holds a test this version does not know";
                return null;
            }

            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LastError = $"Not opened: {Reason(error)}";
            return null;
        }
    }

    /// <summary>Writes a new file named from the clock; returns its name, or null with <see cref="LastError"/> set.</summary>
    public string? Save(PerfResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        LastError = null;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
            Directory.CreateDirectory(Folder);
            var stem = Prefix + _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var fileName = stem + Extension;
            for (var n = 2; File.Exists(Path.Combine(Folder, fileName)); n++)
            {
                fileName = $"{stem}-{n.ToString(CultureInfo.InvariantCulture)}{Extension}";
            }

            Write(Path.Combine(Folder, fileName), bytes, overwrite: false);
            Forget(fileName);
            return fileName;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            LastError = $"Not saved: {Reason(error)}";
            return null;
        }
    }

    /// <summary>Changes the owner's name, description and direction ("A", "B", anything else none), rewriting the file in place.</summary>
    public bool Update(string fileName, string name, string description, string? direction)
    {
        LastError = null;
        if (Resolve(fileName) is not { } path || !File.Exists(path))
        {
            LastError = $"Not updated: {fileName} is not in the results folder";
            return false;
        }

        try
        {
            if (Read(File.ReadAllBytes(path)) is not { } result)
            {
                LastError = $"Not updated: {fileName} is {NotAResult}";
                return false;
            }

            // A newer schema, or a test this version does not know, would lose what this app cannot read back.
            if (result.Schema > CurrentSchema || !Enum.IsDefined(result.Spec.Kind))
            {
                LastError = "Not updated: a newer version of the app made this result";
                return false;
            }

            result.Name = name ?? "";
            result.Description = description ?? "";
            var side = direction?.Trim().ToUpperInvariant();
            result.Direction = side is "A" or "B" ? side : null;
            Write(path, JsonSerializer.SerializeToUtf8Bytes(result, Json), overwrite: true);
            Forget(fileName);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            LastError = $"Not updated: {Reason(error)}";
            return false;
        }
    }

    public bool Delete(string fileName)
    {
        LastError = null;
        if (Resolve(fileName) is not { } path || !File.Exists(path))
        {
            LastError = $"Not deleted: {fileName} is not in the results folder";
            return false;
        }

        try
        {
            File.Delete(path);
            Forget(fileName);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LastError = $"Not deleted: {Reason(error)}";
            return false;
        }
    }

    // A bare file name in the folder; anything with a directory part is refused.
    private string? Resolve(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && Path.GetFileName(fileName) == fileName && fileName is not ("." or "..")
            ? Path.Combine(Folder, fileName)
            : null;

    // A file written here is read again by the next listing, even within the clock's resolution.
    private void Forget(string fileName)
    {
        lock (_listed)
        {
            _listed.Remove(fileName);
        }
    }

    // The temp file is the only thing a failure leaves behind, and it is removed (spec 8). It reaches the disk before the move,
    // so a crash after the move cannot leave an empty result in place of the old one.
    private static void Write(string path, byte[] bytes, bool overwrite)
    {
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The write's own failure, if any, is the one to report.
            }
        }
    }

    /// <summary>A result of a known schema with every part present; null for anything else.</summary>
    private static PerfResult? Read(byte[] bytes)
    {
        try
        {
            var result = JsonSerializer.Deserialize<PerfResult>(bytes, Json);
            return result is { Schema: >= CurrentSchema } && Whole(result) ? result : null;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    // JSON fills a missing or null text with null, so the text and the entries the app reads are checked too: a file with a
    // null name, preset or flag code, a null entry, a figure twice or a broken trace would throw in the list or on opening.
    private static bool Whole(PerfResult result) =>
        result is { Id: not null, Name: not null, Description: not null }
        && result.Spec is { Preset: not null } && result.Figures is { Figures: not null, Traces: not null, Flags: not null, Headline: not null }
        && result.Start is not null && result.End is not null && result.Inputs is { Car.TyreSize: not null, KmhPerRpm: not null }
        && result.SampleAt is not null && result.Payloads is not null && result.SampleAt.Length == result.Payloads.Length
        && result.Payloads.All(payload => payload is not null)
        && (result.SampleRetries is null || result.SampleRetries.Length == result.SampleAt.Length)
        && result.Figures.Flags.All(flag => flag is { Code: not null, Message: not null })
        && result.Figures.Figures.All(figure => figure is { Key: not null, Label: not null, Unit: not null })
        && result.Figures.Figures.Select(figure => figure.Key).Distinct(StringComparer.Ordinal).Count() == result.Figures.Figures.Count
        && result.Figures.Traces.All(trace => trace is { Key: not null, Label: not null, XUnit: not null, YUnit: not null, X: not null, Y: not null }
            && trace.X.Count == trace.Y.Count)
        && result.Tune is null or { ModifiedItems: not null };

    // A file of this schema is parsed once; only a file that is not (a newer schema, or no result) is looked at again as a
    // document, for its schema and what a newer one shares with this.
    private static PerfResultHeader? Header(string fileName, byte[] bytes)
    {
        PerfResult? result = null;
        try
        {
            result = JsonSerializer.Deserialize<PerfResult>(bytes, Json);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // A newer schema may have changed the shape of a part; its header is read below as far as it can be.
        }

        if (result is { Schema: CurrentSchema })
        {
            if (!Whole(result))
            {
                return null;
            }

            // A test this version does not know is listed, never opened or rewritten.
            var known = Enum.IsDefined(result.Spec.Kind);
            return new PerfResultHeader(fileName, result.Id, result.Name, result.Description, result.Created,
                known ? result.Spec.Kind : default, result.Spec.Preset, result.Tune?.BinName, result.Tune?.SavedSha256,
                result.Figures.Headline, result.Figures.Flags, result.Complete, ReadOnly: !known);
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            // A newer schema may have moved anything; read what it shares with this one, as far as it does.
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(nameof(PerfResult.Schema), out var value)
                && value.TryGetInt32(out var schema) && schema > CurrentSchema
                ? NewerHeader(fileName, root)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PerfResultHeader NewerHeader(string fileName, JsonElement root)
    {
        var spec = Child(root, nameof(PerfResult.Spec));
        var tune = Child(root, nameof(PerfResult.Tune));
        var figures = Child(root, nameof(PerfResult.Figures));
        var flags = new List<RunFlag>();
        if (Child(figures, nameof(RunFigures.Flags)) is { ValueKind: JsonValueKind.Array } array)
        {
            foreach (var flag in array.EnumerateArray())
            {
                flags.Add(new RunFlag(Text(flag, nameof(RunFlag.Code)) ?? "",
                    LenientEnumConverter.Parse<FlagSeverity>(Text(flag, nameof(RunFlag.Severity))) ?? FlagSeverity.Suspect,
                    Text(flag, nameof(RunFlag.Message)) ?? ""));
            }
        }

        var created = Child(root, nameof(PerfResult.Created)) is { ValueKind: JsonValueKind.String } stamp && stamp.TryGetDateTimeOffset(out var at)
            ? at
            : default;
        return new PerfResultHeader(fileName, Text(root, nameof(PerfResult.Id)) ?? "", Text(root, nameof(PerfResult.Name)) ?? "",
            Text(root, nameof(PerfResult.Description)) ?? "", created,
            LenientEnumConverter.Parse<PerfTestKind>(Text(spec, nameof(PerfTestSpec.Kind))) ?? default, Text(spec, nameof(PerfTestSpec.Preset)) ?? "",
            Text(tune, nameof(TuneIdentity.BinName)), Text(tune, nameof(TuneIdentity.SavedSha256)),
            Text(figures, nameof(RunFigures.Headline)) ?? "n/a", flags,
            Child(root, nameof(PerfResult.Complete)) is { ValueKind: JsonValueKind.True }, ReadOnly: true);
    }

    private static JsonElement? Child(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(name, out var child) ? child : null;

    private static string? Text(JsonElement? parent, string name) =>
        Child(parent, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static string Reason(Exception error) => error.Message.Trim().TrimEnd('.');

    /// <summary>A listed file as it was read: its header, or null when it is not a result.</summary>
    private sealed record Listed(DateTime WrittenUtc, long Length, PerfResultHeader? Header);

    /// <summary>
    /// Enums as their names; reading ignores case. An unknown name or number reads as suspect for a flag severity (a newer
    /// app's severe flag must not turn into a note), as a value outside the enum for a test (so the file is known to hold a
    /// test this version cannot analyse), and as the first value otherwise.
    /// </summary>
    private sealed class LenientEnumConverter : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

        /// <summary>The value named, null when the name or number is none of the enum's.</summary>
        public static T? Parse<T>(string? text) where T : struct, Enum =>
            text is not null && Enum.TryParse<T>(text, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : null;

        private static T Unknown<T>() where T : struct, Enum =>
            typeof(T) == typeof(FlagSeverity) ? (T)(object)FlagSeverity.Suspect
            : typeof(T) == typeof(PerfTestKind) ? (T)Enum.ToObject(typeof(T), -1)
            : default;

        private sealed class Converter<T> : JsonConverter<T> where T : struct, Enum
        {
            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => (reader.TokenType switch
            {
                JsonTokenType.String => Parse<T>(reader.GetString()),
                JsonTokenType.Number when reader.TryGetInt32(out var number) => Parse<T>(number.ToString(CultureInfo.InvariantCulture)),
                _ => null,
            }) ?? Unknown<T>();

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
        }
    }
}
