using MS43Logger.Core;

namespace MS43Logger.Core.Tests;

/// <summary>Builds logger payloads with chosen raw channel values, laid out as the ADX describes each channel.</summary>
internal static class LivePayloads
{
    public static byte[] Build(AdxDefinition adx, params (string Channel, long Raw)[] values)
    {
        var payload = new byte[73];
        foreach (var (channel, raw) in values)
        {
            var value = adx.Values.First(v => string.Equals(v.Id, channel, StringComparison.OrdinalIgnoreCase));
            var bytes = value.SizeInBits / 8;
            for (var i = 0; i < bytes; i++)
            {
                var shift = 8 * (value.LsbFirst ? i : bytes - 1 - i);
                payload[value.PacketOffset + i] = (byte)(raw >> shift);
            }
        }

        return payload;
    }
}
