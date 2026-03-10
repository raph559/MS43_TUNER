using System.Diagnostics;
using System.Globalization;
using System.Text;
using MS43Logger.Core;

return Run(args);

static int Run(string[] args)
{
    try
    {
        var options = CliOptions.Parse(args);
        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        var definition = LoadDefinition(options);
        var channels = definition.MatchChannels(options.ChannelFilters);

        if (options.ListChannels)
        {
            PrintChannels(definition, channels);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(options.Port))
        {
            throw new InvalidOperationException("A serial port is required unless you use --list-channels.");
        }

        using var csvWriter = options.CsvPath is null ? null : CsvSampleWriter.Create(options.CsvPath, channels);
        using var session = Ds2Session.Create(definition, options.Port);

        var stopRequested = false;
        ConsoleCancelEventHandler? cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopRequested = true;
        };

        Console.CancelKeyPress += cancelHandler;
        try
        {
            session.Connect();
            Console.WriteLine($"Connected on {options.Port} using {definition.SourceName}. Logging {channels.Count} channel(s).");

            var durationLimit = options.DurationSeconds is double seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
            var stopwatch = Stopwatch.StartNew();
            long sampleIndex = 0;

            while (!stopRequested)
            {
                if (options.Count is long countLimit && sampleIndex >= countLimit)
                {
                    break;
                }

                if (durationLimit is TimeSpan duration && stopwatch.Elapsed >= duration)
                {
                    break;
                }

                var payload = session.PollPayload();
                var decodedValues = definition.DecodePayload(payload, channels);
                var sample = new LogSample(DateTimeOffset.UtcNow, sampleIndex, decodedValues);

                csvWriter?.Write(sample);
                if (!options.NoConsole)
                {
                    PrintLiveSample(sample);
                }

                sampleIndex++;
                if (options.IntervalMs > 0)
                {
                    Thread.Sleep(options.IntervalMs);
                }
            }

            if (!options.NoConsole && !Console.IsOutputRedirected)
            {
                Console.WriteLine();
            }

            Console.WriteLine($"Stopped after {sampleIndex} sample(s).");
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            if (session.IsConnected)
            {
                session.Disconnect();
            }
        }
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Error: {exception.Message}");
        return 1;
    }
}

static AdxDefinition LoadDefinition(CliOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.DefinitionPath))
    {
        return AdxDefinition.LoadFromFile(options.DefinitionPath);
    }

    return BundledDefinitions.Load(options.Variant ?? "b30");
}

static void PrintChannels(AdxDefinition definition, IReadOnlyList<AdxValueDefinition> channels)
{
    Console.WriteLine($"{definition.SourceName} ({channels.Count} channel(s))");
    foreach (var channel in channels.OrderBy(channel => channel.PacketOffset).ThenBy(channel => channel.Id, StringComparer.OrdinalIgnoreCase))
    {
        var units = string.IsNullOrWhiteSpace(channel.Units) ? string.Empty : $" [{channel.Units}]";
        Console.WriteLine($"{channel.Id,-14} {channel.DisplayName}{units}");
    }
}

static void PrintLiveSample(LogSample sample)
{
    const int maxColumns = 6;
    var visibleValues = sample.Values.Take(maxColumns)
        .Select(value => $"{value.Definition.Id}={FormatValue(value)}")
        .ToList();

    if (sample.Values.Count > maxColumns)
    {
        visibleValues.Add($"+{sample.Values.Count - maxColumns} more");
    }

    var line = $"{sample.Index,6} {sample.Timestamp:HH:mm:ss.fff} {string.Join(" | ", visibleValues)}";
    if (Console.IsOutputRedirected)
    {
        Console.WriteLine(line);
        return;
    }

    var width = Math.Max(1, Console.WindowWidth - 1);
    if (line.Length < width)
    {
        line = line.PadRight(width);
    }

    Console.Write($"\r{line}");
}

static string FormatValue(DecodedValue value)
{
    var digits = Math.Clamp(value.Definition.DigitCount, 0, 6);
    return value.Value.ToString($"F{digits}", CultureInfo.InvariantCulture);
}

static void PrintUsage()
{
    Console.WriteLine("MS43 Logger");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  ms43-logger --port COM3 [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --port COMx           Serial port for the K-line adapter.");
    Console.WriteLine("  --variant NAME        Bundled ADX variant: b22, b25, or b30. Default: b30.");
    Console.WriteLine("  --definition PATH     Custom ADX file to use instead of a bundled variant.");
    Console.WriteLine("  --channel NAME        Repeat to select channels by ID or title match.");
    Console.WriteLine("  --list-channels       Print the matched channels and exit.");
    Console.WriteLine("  --count N             Stop after N samples.");
    Console.WriteLine("  --duration SECONDS    Stop after the given runtime.");
    Console.WriteLine("  --interval-ms N       Optional delay between polls.");
    Console.WriteLine("  --csv PATH            Write CSV output to this file.");
    Console.WriteLine("  --no-console          Suppress the live console line.");
    Console.WriteLine("  --help                Show this help text.");
}

internal sealed record LogSample(DateTimeOffset Timestamp, long Index, IReadOnlyList<DecodedValue> Values);

internal sealed class CsvSampleWriter : IDisposable
{
    private readonly StreamWriter _writer;

    private CsvSampleWriter(StreamWriter writer)
    {
        _writer = writer;
    }

    public static CsvSampleWriter Create(string path, IReadOnlyList<AdxValueDefinition> channels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };

        var csvWriter = new CsvSampleWriter(writer);
        csvWriter.WriteHeader(channels);
        return csvWriter;
    }

    public void Write(LogSample sample)
    {
        var columns = new List<string>(sample.Values.Count + 2)
        {
            CsvEscape(sample.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
            sample.Index.ToString(CultureInfo.InvariantCulture),
        };

        columns.AddRange(sample.Values.Select(value => CsvEscape(FormatValueForCsv(value))));
        _writer.WriteLine(string.Join(",", columns));
    }

    public void Dispose()
    {
        _writer.Dispose();
    }

    private void WriteHeader(IEnumerable<AdxValueDefinition> channels)
    {
        var headers = new List<string> { "timestamp_utc", "sample_index" };
        headers.AddRange(channels.Select(channel => CsvEscape(channel.Id)));
        _writer.WriteLine(string.Join(",", headers));
    }

    private static string FormatValueForCsv(DecodedValue value)
    {
        var digits = Math.Clamp(value.Definition.DigitCount, 0, 6);
        return value.Value.ToString($"F{digits}", CultureInfo.InvariantCulture);
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return value;
    }
}

internal sealed class CliOptions
{
    public string? Port { get; private set; }

    public string? Variant { get; private set; } = "b30";

    public string? DefinitionPath { get; private set; }

    public List<string> ChannelFilters { get; } = new();

    public bool ListChannels { get; private set; }

    public long? Count { get; private set; }

    public double? DurationSeconds { get; private set; }

    public int IntervalMs { get; private set; }

    public string? CsvPath { get; private set; }

    public bool NoConsole { get; private set; }

    public bool ShowHelp { get; private set; }

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        var options = new CliOptions();

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--port":
                    options.Port = ReadValue(args, ref index, "--port");
                    break;
                case "--variant":
                    options.Variant = ReadValue(args, ref index, "--variant");
                    break;
                case "--definition":
                    options.DefinitionPath = ReadValue(args, ref index, "--definition");
                    break;
                case "--channel":
                    options.ChannelFilters.Add(ReadValue(args, ref index, "--channel"));
                    break;
                case "--list-channels":
                    options.ListChannels = true;
                    break;
                case "--count":
                    options.Count = ParseLong(ReadValue(args, ref index, "--count"), "--count");
                    break;
                case "--duration":
                    options.DurationSeconds = ParseDouble(ReadValue(args, ref index, "--duration"), "--duration");
                    break;
                case "--interval-ms":
                    options.IntervalMs = ParseInt(ReadValue(args, ref index, "--interval-ms"), "--interval-ms");
                    break;
                case "--csv":
                    options.CsvPath = ReadValue(args, ref index, "--csv");
                    break;
                case "--no-console":
                    options.NoConsole = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    options.ShowHelp = true;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown option '{arg}'.");
            }
        }

        return options;
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string optionName)
    {
        if (index + 1 >= args.Count)
        {
            throw new InvalidOperationException($"Missing value for {optionName}.");
        }

        index++;
        return args[index];
    }

    private static int ParseInt(string rawValue, string optionName)
    {
        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new InvalidOperationException($"{optionName} expects a non-negative integer.");
        }

        return value;
    }

    private static long ParseLong(string rawValue, string optionName)
    {
        if (!long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new InvalidOperationException($"{optionName} expects a non-negative integer.");
        }

        return value;
    }

    private static double ParseDouble(string rawValue, string optionName)
    {
        if (!double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new InvalidOperationException($"{optionName} expects a non-negative number.");
        }

        return value;
    }
}
