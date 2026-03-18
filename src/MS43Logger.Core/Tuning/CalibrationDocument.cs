namespace MS43Logger.Core.Tuning;

/// <summary>An open bin with its XDF: the working image, the image as last loaded or saved, and what differs.</summary>
public sealed class CalibrationDocument
{
    private CalibrationDocument(XdfDefinition xdf, BinImage image, string? path)
    {
        Xdf = xdf;
        Image = image;
        Saved = image.Clone();
        Path = path;
        Firmware = FirmwareIdentifier.Identify(image);
        XdfMatchesFirmware = FirmwareIdentifier.Matches(Firmware, xdf);
        Reader = new CalibrationReader(xdf, image);
    }

    public event EventHandler? Changed;

    public XdfDefinition Xdf { get; }

    /// <summary>The working image; edits write here.</summary>
    public BinImage Image { get; }

    /// <summary>The image as last loaded or saved.</summary>
    public BinImage Saved { get; private set; }

    public string? Path { get; private set; }

    public FirmwareInfo Firmware { get; }

    public bool XdfMatchesFirmware { get; }

    public CalibrationReader Reader { get; }

    public bool IsModified => Image.DiffRanges(Saved).Count > 0;

    public static CalibrationDocument Open(string binPath, string xdfPath) =>
        new(XdfDefinition.LoadFromFile(xdfPath), BinImage.Load(binPath), binPath);

    public static CalibrationDocument FromParts(XdfDefinition xdf, BinImage image, string? path) =>
        new(xdf ?? throw new ArgumentNullException(nameof(xdf)), image ?? throw new ArgumentNullException(nameof(image)), path);

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Items whose bytes differ from <see cref="Saved"/>, each listed once, tables first.</summary>
    public IReadOnlyList<XdfItem> ModifiedItems()
    {
        var ranges = Image.DiffRanges(Saved);
        if (ranges.Count == 0)
        {
            return Array.Empty<XdfItem>();
        }

        bool Touched(XdfData data)
        {
            if (data.Address is not long address)
            {
                return false;
            }

            var start = BinImage.ToOffset(address);
            var end = start + (long)data.Count * data.ElementBytes;
            return ranges.Any(range => range.Offset < end && range.Offset + range.Length > start);
        }

        return Xdf.Tables.Where(table => Touched(table.Z.Data)).Cast<XdfItem>()
            .Concat(Xdf.Constants.Where(constant => Touched(constant.Data)))
            .Concat(Xdf.Flags.Where(flag => Touched(flag.Data) && FlagChanged(flag)))
            .ToList();
    }

    // Several flags can share one word; only the flag whose own bit moved counts as changed.
    private bool FlagChanged(XdfFlag flag) =>
        !ImageContains(flag.Data) || (Image.ReadElement(flag.Data, 0) & flag.Mask) != (Saved.ReadElement(flag.Data, 0) & flag.Mask);

    private bool ImageContains(XdfData data) => data.IsSupported && Image.Contains(data);

    /// <summary>True once a <c>.bak</c> of the file on disk was written in this session.</summary>
    public bool BackupWritten { get; private set; }

    // Files already backed up while this app runs: reopening a file must not replace the .bak of its original bytes.
    private static readonly HashSet<string> BackedUpThisRun = new(StringComparer.OrdinalIgnoreCase);

    private static bool MarkBackedUp(string path)
    {
        lock (BackedUpThisRun)
        {
            return BackedUpThisRun.Add(path);
        }
    }
}
