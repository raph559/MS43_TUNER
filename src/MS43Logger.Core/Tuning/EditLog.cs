using System.Globalization;
using System.Text;

namespace MS43Logger.Core.Tuning;

/// <summary>
/// The per-bin change log TunerPro keeps next to each bin (<c>&lt;bin name&gt;.log</c>), written in the same format so
/// one history covers both tools.
/// </summary>
public static class EditLog
{
    private const string NewLine = "\r\n";

    public static string Entry(XdfItem item, ConstantValue? before, ConstantValue? after, DateTime when)
    {
        ArgumentNullException.ThrowIfNull(item);
        var time = when.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        return item switch
        {
            XdfConstant constant when before is not null && after is not null =>
                $"{time}  Scalar:    {constant.Title} changed from {before.Display} - ({Hex(before.Raw, constant.Data)}) to {after.Display} - ({Hex(after.Raw, constant.Data)}).",
            XdfConstant constant => $"{time}  Scalar:    {constant.Title} changed.",
            XdfFlag flag => $"{time}  Flag:      {flag.Title} changed.",
            _ => $"{time}  Table:     {item.Title} changed.",
        };
    }

    /// <summary>Appends entries to the log next to <paramref name="binPath"/>, creating it with a header when missing.</summary>
    public static void Append(string binPath, IEnumerable<string> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binPath);
        var lines = entries.ToList();
        if (lines.Count == 0)
        {
            return;
        }

        var logPath = PathFor(binPath);
        var text = new StringBuilder();
        if (!File.Exists(logPath))
        {
            text.Append($"Edit Log for {Path.GetFileName(binPath)} created by MS43 Tuner.").Append(NewLine);
            text.Append('*', 74).Append(NewLine);
        }

        foreach (var line in lines)
        {
            text.Append(line).Append(NewLine);
        }

        File.AppendAllText(logPath, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static string PathFor(string binPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(binPath)) ?? ".", Path.GetFileNameWithoutExtension(binPath) + ".log");

    private static string Hex(long raw, XdfData data)
    {
        var mask = data.ElementBits >= 64 ? ulong.MaxValue : (1UL << data.ElementBits) - 1;
        return "0x" + ((ulong)raw & mask).ToString("X2", CultureInfo.InvariantCulture);
    }
}
