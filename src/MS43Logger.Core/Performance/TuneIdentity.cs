using System.IO.Compression;
using System.Security.Cryptography;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Performance;

/// <summary>
/// The tune a run was made with, captured when it is armed: the bin and XDF, whether it had unsaved
/// edits and which, the SHA-256 of the saved and the working image, the rev limit and launch control the run used, and
/// <see cref="Snapshot"/>, the calibration area of the saved image, so two results can be compared table by table after the
/// bins changed on disk.
/// </summary>
public sealed record TuneIdentity(string? BinName, string? BinPath, string? SoftwareId, string? CalibrationName,
    string XdfTitle, string? XdfFile, bool HasUnsavedEdits, IReadOnlyList<string> ModifiedItems,
    string SavedSha256, string WorkingSha256, double? RevLimitRpm, bool? LaunchControl, string Snapshot)
{
    /// <summary>
    /// Per live map in the overlay's order, "flashed" or the live copy's version, when any map was live as the run was
    /// armed; null when none was, and in results saved before live tuning.
    /// </summary>
    public IReadOnlyList<string>? LiveMaps { get; init; }

    /// <summary>The live maps changed during the run: it ran on more than one set of maps.</summary>
    public bool LiveMapsChanged { get; init; }

    // The calibration occupies file offsets 0x70000 through 0x7FFFF.
    private const int CalibrationStart = 0x70000;
    private const int CalibrationLength = BinImage.Size - CalibrationStart;
    private const string RevLimitTable = "id_n_max_max_mt__gear";
    private const string LaunchControlSwitch = "c_conf_lc";

    /// <summary>
    /// Null without a document. The snapshot and limits come from the saved image, since unsaved edits cannot be in the car;
    /// <see cref="RevLimitRpm"/> is the <c>id_n_max_max_mt__gear</c> row whose gear-axis value equals
    /// <paramref name="gear"/>, null when the gear is null or the table is missing.
    /// </summary>
    public static TuneIdentity? From(CalibrationDocument? document, int? gear)
    {
        if (document is null)
        {
            return null;
        }

        var saved = document.Saved;
        var firmware = FirmwareIdentifier.Identify(saved);
        var reader = new CalibrationReader(document.Xdf, saved);
        return new TuneIdentity(
            document.Path is { } path ? Path.GetFileName(path) : null,
            document.Path,
            firmware.SoftwareId,
            firmware.CalibrationName,
            document.Xdf.Title,
            string.IsNullOrWhiteSpace(document.Xdf.SourceName) ? null : document.Xdf.SourceName,
            document.IsModified,
            document.ModifiedItems().Select(item => item.Title).ToArray(),
            Sha256(saved),
            Sha256(document.Image),
            RevLimit(document.Xdf, saved, reader, gear),
            LaunchControlOn(document.Xdf, saved, reader),
            SnapshotOf(saved));
    }

    /// <summary>Lower-case hex SHA-256 of the whole image.</summary>
    public static string Sha256(BinImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Convert.ToHexStringLower(SHA256.HashData(image.Bytes));
    }

    /// <summary>What <see cref="Snapshot"/> holds: 0x70000 to 0x7FFFF of the image, zlib-compressed and base64.</summary>
    public static string SnapshotOf(BinImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(image.Bytes[CalibrationStart..]);
        }

        return Convert.ToBase64String(packed.GetBuffer(), 0, (int)packed.Length);
    }

    /// <summary>
    /// A full-size image holding the snapshot's calibration area and zeros elsewhere. Throws
    /// <see cref="InvalidDataException"/> when the text is not a snapshot.
    /// </summary>
    public static BinImage Restore(string snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var bytes = new byte[BinImage.Size];
        try
        {
            using var zlib = new ZLibStream(new MemoryStream(Convert.FromBase64String(snapshot)), CompressionMode.Decompress);
            zlib.ReadExactly(bytes, CalibrationStart, CalibrationLength);
            // A longer stream is not a snapshot of this area either.
            if (zlib.ReadByte() >= 0)
            {
                throw new InvalidDataException("The calibration snapshot is longer than the calibration area.");
            }
        }
        catch (Exception error) when (error is FormatException or IOException)
        {
            throw new InvalidDataException("The calibration snapshot cannot be read.", error);
        }

        return BinImage.FromBytes(bytes, "snapshot.bin");
    }

    private static double? RevLimit(XdfDefinition xdf, BinImage image, CalibrationReader reader, int? gear)
    {
        if (gear is not { } wanted || xdf.FindByTitle(RevLimitTable) is not XdfTable table || !Readable(image, table.Z.Data))
        {
            return null;
        }

        var values = reader.ReadTable(table);
        // One limit per gear, down the rows; the row axis holds the gear numbers.
        for (var row = 0; row < values.Rows; row++)
        {
            if (row < values.Y.Values.Count && values.Y.Values[row] == wanted && double.IsFinite(values.Physical[row, 0]))
            {
                return values.Physical[row, 0];
            }
        }

        return null;
    }

    private static bool? LaunchControlOn(XdfDefinition xdf, BinImage image, CalibrationReader reader) =>
        xdf.FindByTitle(LaunchControlSwitch) is XdfConstant constant && Readable(image, constant.Data)
            ? reader.ReadConstant(constant).Raw != 0
            : null;

    // What CalibrationReader can read without throwing.
    private static bool Readable(BinImage image, XdfData data) =>
        data.IsSupported && data.Address is not null && data.ElementBits is 8 or 16 or 32 && image.Contains(data);
}
