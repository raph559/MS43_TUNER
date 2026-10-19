using System.Text;

namespace MS43Logger.Core.Tuning;

/// <param name="SoftwareId">For example <c>43X001</c> or <c>430069</c>.</param>
/// <param name="CalibrationName">For example <c>ca43X001.DAT</c>.</param>
public sealed record FirmwareInfo(string? SoftwareId, string? CalibrationName);

public static class FirmwareIdentifier
{
    private const int CalibrationNameOffset = 0x70040;
    private const int MaxNameLength = 16;

    public static FirmwareInfo Identify(BinImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var bytes = image.Bytes.Slice(CalibrationNameOffset, MaxNameLength);
        var end = bytes.IndexOfAny((byte)0x00, (byte)0xFF);
        var name = Encoding.ASCII.GetString(end < 0 ? bytes : bytes[..end]);

        if (name.Length > 6 &&
            name.StartsWith("ca", StringComparison.Ordinal) &&
            name.EndsWith(".DAT", StringComparison.OrdinalIgnoreCase) &&
            name.All(c => c is >= ' ' and <= '~'))
        {
            return new FirmwareInfo(name[2..^4], name);
        }

        return new FirmwareInfo(null, null);
    }

    public static bool Matches(FirmwareInfo info, XdfDefinition xdf)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(xdf);
        return info.SoftwareId is string id && xdf.Title.Contains(id, StringComparison.OrdinalIgnoreCase);
    }
}
