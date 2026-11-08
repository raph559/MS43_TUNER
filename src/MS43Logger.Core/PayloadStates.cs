namespace MS43Logger.Core;

/// <summary>
/// The stock payload's on/off states the log writes on every row, under the ADX's own names (V10.1 review): the trailing
/// throttle (overrun) and its fuel cut, the brake light test switch, the brake light switch and the clutch switch. Every
/// poll's payload carries them, so they cost no read; an ADX without one leaves its cell empty.
/// </summary>
public static class PayloadStates
{
    public static IReadOnlyList<string> CsvHeader { get; } = ["PUSTATE", "PUCSTATE", "BTSSTATE", "BLSSTATE", "CSSTATE"];

    /// <summary>"1" or "0" for each state in <see cref="CsvHeader"/>, or empty where the payload or the ADX has no such bit.</summary>
    public static IReadOnlyList<string> CsvValues(AdxDefinition definition, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var values = new string[CsvHeader.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = definition.DecodeBitmask(CsvHeader[i], payload) switch { true => "1", false => "0", null => string.Empty };
        return values;
    }
}
