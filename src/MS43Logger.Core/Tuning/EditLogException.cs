namespace MS43Logger.Core.Tuning;

/// <summary>
/// The bin was saved, but its edit log could not be written (another program held it, or the disk refused). The save
/// itself stands: the document is clean and <see cref="LogPath"/> names the log that missed its lines.
/// </summary>
public sealed class EditLogException(string logPath, Exception inner)
    : Exception($"{Path.GetFileName(logPath)} could not be written: {inner.Message}", inner)
{
    public string LogPath { get; } = logPath;
}
