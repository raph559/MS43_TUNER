namespace MS43Logger.Core.Tuning;

/// <summary>The two Saving switches of the Settings dialog: a.bak before the first save, and the TunerPro edit log.</summary>
public sealed record SaveOptions(bool KeepBackup = true, bool AppendEditLog = true);
