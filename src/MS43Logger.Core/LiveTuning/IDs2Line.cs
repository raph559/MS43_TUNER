namespace MS43Logger.Core.LiveTuning;

/// <summary>A DS2 line to the ECU: one request at a time, whatever else is sharing it.</summary>
public interface IDs2Line
{
    /// <summary>Monotonic time used to space active-map updates; simulators can supply their own clock.</summary>
    long MonotonicMilliseconds => Environment.TickCount64;

    /// <summary>Waits without holding the serial request lock. Live sends run on a background thread.</summary>
    void WaitMilliseconds(int milliseconds) => Thread.Sleep(milliseconds);

    /// <summary>
    /// Sends <paramref name="payload"/> (from the command byte on) to the ECU as one frame and returns the ECU's whole reply
    /// frame: 12h first, a valid xor, not the echo of the request. Throws <see cref="TimeoutException"/> when none comes
    /// within <paramref name="listenMilliseconds"/>.
    /// </summary>
    byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400);
}

/// <summary>The ECU refused a DS2 memory read: its reply's status was not A0.</summary>
public sealed class Ds2RefusedException(int address, byte status)
    : Exception($"The ECU answered status {status:X2} to a read of {address:X6}.")
{
    public int Address { get; } = address;

    public byte Status { get; } = status;
}

/// <summary>DS2 command 06, the read-only memory read: <c>06 00 A2 A1 A0 n</c>, at most 123 bytes a request.</summary>
public static class Ds2Memory
{
    public const int MostRead = 123;

    /// <summary>Reads <paramref name="count"/> bytes from <paramref name="address"/>, in as many requests as it takes.</summary>
    public static byte[] Read(this IDs2Line line, int address, int count)
    {
        ArgumentNullException.ThrowIfNull(line);
        var data = new byte[count];
        for (var done = 0; done < count;)
        {
            var at = address + done;
            var n = Math.Min(MostRead, count - done);
            var request = new byte[] { 0x06, 0x00, (byte)(at >> 16), (byte)(at >> 8), (byte)at, (byte)n };
            byte[] reply;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    reply = line.Request(request);
                    break;
                }
                catch (TimeoutException) when (attempt < 2)
                {
                    // A read has no side effects. Retry this chunk, preserving completed chunks.
                    line.WaitMilliseconds(20);
                }
            }
            OverlayContract.ValidateFrame(reply);
            if (reply.Length < 4 || reply[2] != 0xA0)
            {
                throw new Ds2RefusedException(at, reply.Length > 2 ? reply[2] : (byte)0);
            }

            if (reply.Length - 4 != n)
            {
                throw new InvalidDataException($"The ECU answered {reply.Length - 4} bytes to a read of {n} at {at:X6}.");
            }

            Array.Copy(reply, 3, data, done, n);
            done += n;
        }

        return data;
    }
}
