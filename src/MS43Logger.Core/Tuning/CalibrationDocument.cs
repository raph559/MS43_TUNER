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

    /// <summary>
    /// Writes the working image to <paramref name="path"/> (default: the open file). The first time an existing file is
    /// overwritten in this session its previous bytes go to <c>&lt;file&gt;.bak</c>. The image is written to a temporary
    /// file and moved into place, so a failure leaves the target as it was. Changed items are appended to the edit log.
    /// <paramref name="options"/> can turn the.bak and the edit log off. A log that cannot be written throws
    /// <see cref="EditLogException"/> after the save has completed and <see cref="Changed"/> has been raised.
    /// </summary>
    public void Save(string? path = null, DateTime? now = null, SaveOptions? options = null)
    {
        options ??= new SaveOptions();
        var target = System.IO.Path.GetFullPath(path ?? Path ?? throw new InvalidOperationException("Choose where to save this file."));
        var when = now ?? DateTime.Now;
        var entries = options.AppendEditLog ? LogEntries(when) : null;

        var temp = target + ".tmp";
        try
        {
            // The new bytes reach the disk before the move, so a power cut just after it cannot leave a short bin.
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(Image.ToArray());
                stream.Flush(flushToDisk: true);
            }

            // The file counts as backed up only once its.bak is written: a failed copy is tried again on the next save.
            if (options.KeepBackup && File.Exists(target) && !IsBackedUp(target))
            {
                File.Copy(target, target + ".bak", overwrite: true);
                MarkBackedUp(target);
                BackupWritten = true;
            }

            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        Saved = Image.Clone();
        Path = target;
        EditLogException? logFailure = null;
        if (entries is not null)
        {
            try
            {
                EditLog.Append(target, entries);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The bin is already in place; only the log missed its lines (it may be open in another program).
                logFailure = new EditLogException(EditLog.PathFor(target), error);
            }
        }

        RaiseChanged();
        if (logFailure is not null)
        {
            throw logFailure;
        }
    }

    // Files already backed up while this app runs: reopening a file must not replace the.bak of its original bytes.
    private static readonly HashSet<string> BackedUpThisRun = new(StringComparer.OrdinalIgnoreCase);

    private static bool IsBackedUp(string path)
    {
        lock (BackedUpThisRun)
        {
            return BackedUpThisRun.Contains(path);
        }
    }

    private static void MarkBackedUp(string path)
    {
        lock (BackedUpThisRun)
        {
            BackedUpThisRun.Add(path);
        }
    }

    private List<string> LogEntries(DateTime when)
    {
        var before = new CalibrationReader(Xdf, Saved);
        return ModifiedItems()
            .Select(item => item is XdfConstant constant
                ? EditLog.Entry(constant, before.ReadConstant(constant), Reader.ReadConstant(constant), when)
                : EditLog.Entry(item, null, null, when))
            .ToList();
    }
}
