namespace MS43Logger.Core.Tests;

/// <summary>
/// The stock payload's on/off states the log writes on every row (V10.1 review): byte 0's trailing throttle (0x08), its
/// fuel cut (0x10), the brake light test switch (0x20), the brake light switch (0x40) and the clutch switch (0x80), under
/// the ADX's own names. They cost no read: every poll's payload carries them.
/// </summary>
public sealed class PayloadStatesTests
{
    [Fact]
    public void TheFiveStatesComeFromPayloadByte0()
    {
        var definition = BundledDefinitions.Load("ms43x");
        var payload = new byte[definition.GetMonitorExchange().Response.PacketSize];
        Assert.Equal(["PUSTATE", "PUCSTATE", "BTSSTATE", "BLSSTATE", "CSSTATE"], PayloadStates.CsvHeader);
        Assert.Equal(["0", "0", "0", "0", "0"], PayloadStates.CsvValues(definition, payload));
        payload[0] = 0x08 | 0x20 | 0x80;
        Assert.Equal(["1", "0", "1", "0", "1"], PayloadStates.CsvValues(definition, payload));
        payload[0] = 0x10 | 0x40;
        Assert.Equal(["0", "1", "0", "1", "0"], PayloadStates.CsvValues(definition, payload));
    }

    [Fact]
    public void NoPayloadGivesEmptyCells() =>
        Assert.Equal(["", "", "", "", ""], PayloadStates.CsvValues(BundledDefinitions.Load("ms43x"), ReadOnlySpan<byte>.Empty));
}
