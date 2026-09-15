namespace MS43Logger.Core.LiveTuning;

/// <summary>A send's outcome: null and null when the map is live; else the refusal, or the map to free first when it does not fit.</summary>
public sealed record SendResult(OverlayReason? Refused, int? FreeFirst);
