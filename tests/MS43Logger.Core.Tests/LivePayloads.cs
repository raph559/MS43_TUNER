using MS43Logger.Core;

namespace MS43Logger.Core.Tests;

/// <summary>Builds logger payloads with chosen raw channel values, laid out as the ADX describes each channel.</summary>
internal static class LivePayloads
{
    public static byte[] Build(AdxDefinition adx, params (string Channel, long Raw)[] values) => Build(adx, Array.Empty<(int, byte)>(), values);

    /// <summary>
    /// Also sets whole frame bytes, as the ECU lays them out, for the status bytes and selectors. The frame is as long as the
    /// definition's reply (73 bytes for ms43x, 82 for the stock v2.9 ones).
    /// </summary>
    public static byte[] Build(AdxDefinition adx, (int Offset, byte Value)[] bytes, params (string Channel, long Raw)[] values)
    {
        var payload = new byte[adx.GetMonitorExchange().Response.PacketSize];
        foreach (var (offset, value) in bytes)
        {
            payload[offset] = value;
        }

        foreach (var (channel, raw) in values)
        {
            var value = adx.Values.First(v => string.Equals(v.Id, channel, StringComparison.OrdinalIgnoreCase));
            var count = value.SizeInBits / 8;
            for (var i = 0; i < count; i++)
            {
                var shift = 8 * (value.LsbFirst ? i : count - 1 - i);
                payload[value.PacketOffset + i] = (byte)(raw >> shift);
            }
        }

        return payload;
    }
}
