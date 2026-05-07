namespace MS43Logger.Core.Tuning;

/// <summary>One table axis fed by a logged input: the breakpoints the ECU searches (raw, from the open image) and how to show the input.</summary>
public sealed record LiveAxisBinding(LiveInputSource Source, IReadOnlyList<long> Breakpoints, XdfMath Math, string Units, int? DecimalPlaces);

/// <param name="Input">The value the ECU searches the axis with, in its raw domain.</param>
/// <param name="Physical">That input in the axis's units, not clamped to the axis.</param>
public sealed record LiveReading(string Channel, long Input, double Physical, string Units, int? DecimalPlaces, int Clamped);

/// <summary>Where the engine is on a table for one sample. A null axis position means the table has one row or one column.</summary>
public sealed record CursorFix(AxisPosition? Row, AxisPosition? Col, IReadOnlyList<CellWeight> Cells, double Value, IReadOnlyList<LiveReading> Inputs, bool Approximate);

/// <summary>
/// Links an XDF table to the logger: XDF table → its data address → the code catalogue's axis inputs → the logger channel that carries
/// each input. Rows follow the XDF Y axis, columns the X axis; axes are matched by address, so the XDF orientation does not matter.
/// </summary>
public sealed class LiveTableBinding
{
    private const string NotListed = "No live input: the code catalogue does not list this table.";
    private const string Mismatch = "No live input: the XDF axes of this table are not the axes the ECU searches.";

    private readonly ValueCodec _codec;

    private LiveTableBinding(XdfTable table, ValueCodec codec, string? reason, LiveAxisBinding? row, LiveAxisBinding? col, bool interpolates)
    {
        Table = table;
        _codec = codec;
        Reason = reason;
        Row = row;
        Col = col;
        Interpolates = interpolates;
    }

    public XdfTable Table { get; }

    public bool HasInput => Reason is null;

    /// <summary>Why the table has no cursor; starts with "No live input:".</summary>
    public string? Reason { get; }

    public LiveAxisBinding? Row { get; }

    public LiveAxisBinding? Col { get; }

    public bool Interpolates { get; }

    private IEnumerable<LiveAxisBinding> Axes => new[] { Row, Col }.OfType<LiveAxisBinding>();

    public static LiveTableBinding Resolve(LiveInputMap? map, CalibrationDocument document, XdfTable table)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(table);
        var codec = document.Reader.Codec;
        LiveTableBinding NoInput(string reason) => new(table, codec, reason, null, null, false);

        var softwareId = document.Firmware.SoftwareId;
        if (map is null || !string.Equals(map.SoftwareId, softwareId, StringComparison.OrdinalIgnoreCase))
        {
            return NoInput($"No live input: there is no input map for firmware {softwareId ?? "unknown"}.");
        }

        var data = table.Z.Data;
        if (data.Address is not long address)
        {
            return NoInput(NotListed);
        }

        var catalog = map.FindTable(address);
        if (catalog is null)
        {
            return ResolveAxisTable(map, document, table, address, NoInput);
        }

        CatalogAxis? rowAxis = null, colAxis = null;
        foreach (var axis in catalog.Axes)
        {
            var first = axis.CountAddress + axis.ElementBits / 8;
            if (data.Rows > 1 && rowAxis is null && FirstValueAddress(document.Xdf, table.Y) == first)
            {
                rowAxis = axis;
            }
            else if (data.Cols > 1 && colAxis is null && FirstValueAddress(document.Xdf, table.X) == first)
            {
                colAxis = axis;
            }
            else
            {
                return NoInput(Mismatch);
            }
        }

        if ((data.Rows > 1) != (rowAxis is not null) || (data.Cols > 1) != (colAxis is not null) ||
            (rowAxis is not null && ReadCount(document.Image, rowAxis) != data.Rows) ||
            (colAxis is not null && ReadCount(document.Image, colAxis) != data.Cols))
        {
            return NoInput(Mismatch);
        }

        string? reason = null;
        var row = rowAxis is null ? null : Bind(map, document.Image, rowAxis, "rows", AxisDisplay(document.Xdf, table.Y), out reason);
        if (rowAxis is not null && row is null)
        {
            return NoInput(reason!);
        }

        var col = colAxis is null ? null : Bind(map, document.Image, colAxis, "columns", AxisDisplay(document.Xdf, table.X), out reason);
        if (colAxis is not null && col is null)
        {
            return NoInput(reason!);
        }

        return new LiveTableBinding(table, codec, null, row, col, catalog.Interpolates);
    }

    /// <summary>An axis table is not read as data by the ECU; it gets a cursor when every table using it searches it with the same input.</summary>
    private static LiveTableBinding ResolveAxisTable(LiveInputMap map, CalibrationDocument document, XdfTable table, long address, Func<string, LiveTableBinding> noInput)
    {
        var data = table.Z.Data;
        var countAddress = address - data.ElementBytes;
        var inputs = map.InputsOfAxis(countAddress);
        var rows = data.Rows > 1;
        var dimension = rows ? "rows" : "columns";
        if (inputs.Count == 0 || (data.Rows > 1 && data.Cols > 1))
        {
            return noInput(NotListed);
        }

        if (inputs.Count > 1)
        {
            return noInput($"No live input: the {dimension} input is computed in code or varies by call site ({string.Join("/", inputs)}).");
        }

        var axis = new CatalogAxis(countAddress, data.ElementBits, inputs[0]);
        if (ReadCount(document.Image, axis) != data.Count)
        {
            return noInput(Mismatch);
        }

        var binding = Bind(map, document.Image, axis, dimension, (table.Z.Math, table.Z.Units, table.Z.DecimalPlaces), out var reason);
        return binding is null
            ? noInput(reason!)
            : new LiveTableBinding(table, document.Reader.Codec, null, rows ? binding : null, rows ? null : binding, true);
    }

    private static LiveAxisBinding? Bind(LiveInputMap map, BinImage image, CatalogAxis axis, string dimension, (XdfMath Math, string Units, int? Decimals) display, out string? reason)
    {
        reason = null;
        if (axis.Input.IndexOfAny(new[] { '?', '#', '/' }) >= 0)
        {
            reason = $"No live input: the {dimension} input is computed in code or varies by call site ({axis.Input}).";
            return null;
        }

        if (!map.Inputs.TryGetValue(axis.Input, out var source))
        {
            reason = $"No live input: the {dimension} follow ECU variable {axis.Input}, which the logger does not read.";
            return null;
        }

        var count = ReadCount(image, axis);
        var values = new XdfData(axis.CountAddress + axis.ElementBits / 8, axis.ElementBits, false, true, count, 1, null);
        var breakpoints = Enumerable.Range(0, count).Select(i => image.ReadElement(values, i)).ToList();
        return new LiveAxisBinding(source, breakpoints, display.Math, display.Units, display.Decimals);
    }

    /// <summary>The axis's point count as stored in the bin, or −1 when it cannot be read.</summary>
    private static int ReadCount(BinImage image, CatalogAxis axis)
    {
        var count = new XdfData(axis.CountAddress, axis.ElementBits, false, true, 1, 1, null);
        if (!image.Contains(count))
        {
            return -1;
        }

        var n = (int)image.ReadElement(count, 0);
        return image.Contains(count with { Address = axis.CountAddress + axis.ElementBits / 8, Rows = Math.Max(1, n) }) ? n : -1;
    }

    private static long? FirstValueAddress(XdfDefinition xdf, XdfAxis axis) =>
        axis.LinkedTableId is int link && xdf.FindTableById(link) is XdfTable linked ? linked.Z.Data.Address : axis.Data?.Address;

    /// <summary>Same precedence as <see cref="CalibrationReader"/>: a linked axis table's scaling, else the axis's own.</summary>
    private static (XdfMath Math, string Units, int? Decimals) AxisDisplay(XdfDefinition xdf, XdfAxis axis)
    {
        if (axis.LinkedTableId is int link && xdf.FindTableById(link) is XdfTable linked && linked.Z.Data.IsSupported)
        {
            return (linked.Z.Math, axis.Units.Length > 0 ? axis.Units : linked.Z.Units, axis.DecimalPlaces ?? linked.Z.DecimalPlaces);
        }

        return (axis.Math, axis.Units, axis.DecimalPlaces);
    }

    public string Describe()
    {
        if (!HasInput)
        {
            return Reason!;
        }

        static string Part(LiveAxisBinding axis, string dimension) =>
            axis.Units.Length > 0 ? $"{axis.Source.Channel} ({axis.Units}) on {dimension}" : $"{axis.Source.Channel} on {dimension}";

        var parts = new List<string>();
        if (Row is { } row)
        {
            parts.Add(Part(row, "rows"));
        }

        if (Col is { } col)
        {
            parts.Add(Part(col, "columns"));
        }

        return "Live input: " + string.Join(", ", parts) + ".";
    }

    /// <summary>Null when <paramref name="adx"/> carries every channel this table needs where the input map expects it; otherwise the reason.</summary>
    public string? CheckLogger(AdxDefinition adx)
    {
        ArgumentNullException.ThrowIfNull(adx);
        foreach (var axis in Axes)
        {
            var source = axis.Source;
            var channel = Channel(adx, source);
            if (channel is null || channel.PacketOffset != source.PacketOffset || channel.SizeInBits != source.Bits || channel.FloatingPoint)
            {
                return $"No live input: the logger definition {adx.SourceName} does not carry {source.Channel} at offset 0x{source.PacketOffset:X2}.";
            }
        }

        return null;
    }

    /// <summary>Finds the cursor for one payload; call <see cref="CheckLogger"/> first for the definition the payload was decoded with.</summary>
    public CursorFix Locate(AdxDefinition adx, ReadOnlySpan<byte> payload, double[,] physical)
    {
        ArgumentNullException.ThrowIfNull(adx);
        ArgumentNullException.ThrowIfNull(physical);
        if (!HasInput)
        {
            throw new InvalidOperationException(Reason);
        }

        var inputs = new List<LiveReading>(2);
        AxisPosition? row = Row is { } r ? Read(adx, r, payload, inputs) : null;
        AxisPosition? col = Col is { } c ? Read(adx, c, payload, inputs) : null;
        var cells = CellWeights.Around(row, col, physical.GetLength(0), physical.GetLength(1));
        return new CursorFix(row, col, cells, CellWeights.Interpolate(physical, cells), inputs, Axes.Any(axis => axis.Source.Approximate));
    }

    private AxisPosition Read(AdxDefinition adx, LiveAxisBinding axis, ReadOnlySpan<byte> payload, List<LiveReading> inputs)
    {
        var channel = Channel(adx, axis.Source) ?? throw new InvalidOperationException($"The logger definition {adx.SourceName} has no channel {axis.Source.Channel}.");
        var input = axis.Source.Apply(AdxDefinition.ReadRaw(channel, payload));
        var position = AxisSearch.Find(axis.Breakpoints, input, Interpolates);
        inputs.Add(new LiveReading(axis.Source.Channel, input, _codec.ToPhysical(axis.Math, input), axis.Units, axis.DecimalPlaces, position.Clamped));
        return position;
    }

    private static AdxValueDefinition? Channel(AdxDefinition adx, LiveInputSource source) =>
        adx.Values.FirstOrDefault(value => string.Equals(value.Id, source.Channel, StringComparison.OrdinalIgnoreCase));
}
