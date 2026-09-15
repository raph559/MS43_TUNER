namespace MS43Logger.Core.LiveTuning;

/// <summary>What the app wants of a map in the ECU: nothing, sent and switched off, or live.</summary>
public enum MapIntent
{
    None,
    SentOff,
    Live,
}
