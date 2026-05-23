namespace MS43Logger.Core.Tuning;

/// <summary>The CLI <c>diff A.bin B.bin --xdf PATH</c> command: prints every item and byte range that differs.</summary>
public static class DiffCommand
{
    public const string Usage = "Usage: ms43-logger diff A.bin B.bin --xdf PATH";

    /// <returns>0 when the comparison ran, whatever it found; 1 on bad arguments or unreadable files.</returns>
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        string? xdfPath = null;
        var bins = new List<string>();
        for (var index = 0; index < args.Count; index++)
        {
            if (args[index] == "--xdf" && index + 1 < args.Count)
            {
                xdfPath = args[++index];
            }
            else
            {
                bins.Add(args[index]);
            }
        }

        if (xdfPath is null || bins.Count != 2)
        {
            error.WriteLine(Usage);
            return 1;
        }

        try
        {
            var a = BinImage.Load(bins[0]);
            var b = BinImage.Load(bins[1]);
            var xdf = XdfDefinition.LoadFromFile(xdfPath);
            var firmwareA = FirmwareIdentifier.Identify(a);
            var firmwareB = FirmwareIdentifier.Identify(b);
            foreach (var (image, firmware) in new[] { (a, firmwareA), (b, firmwareB) })
            {
                if (!FirmwareIdentifier.Matches(firmware, xdf))
                {
                    error.WriteLine($"Warning: XDF \"{xdf.Title}\" does not match firmware {firmware.SoftwareId ?? "(unknown)"} of {image.Name}.");
                }
            }

            if (firmwareA.SoftwareId != firmwareB.SoftwareId)
            {
                error.WriteLine($"Warning: B is firmware {firmwareB.SoftwareId ?? "unknown"}, A is {firmwareA.SoftwareId ?? "unknown"}; items are compared at the same addresses.");
            }

            output.Write(ComparisonText.Format(BinComparer.Compare(xdf, a, b), a.Name, b.Name, Path.GetFileName(xdfPath)));
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }
}
