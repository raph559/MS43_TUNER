using System.Buffers.Binary;

namespace MS43Logger.Core.LiveTuning;

/// <summary>Session framing, stamped mutations and eight-byte patches for the current overlay.</summary>
public static class OverlayProtocol
{
    public const int Version = 10, Minor = 4;
    public const string Label = "10.4";

    public const int PatchBytes = 8;
    public const int MostWrite = 112;
    public const int MinimumPatchGapMilliseconds = 100;

    public static byte[] Open(uint session)
    {
        if (session == 0) throw new ArgumentOutOfRangeException(nameof(session));
        var bytes = new byte[6] { 0x6D, 10, 0, 0, 0, 0 };
        Put(bytes, 2, session);
        return bytes;
    }

    public static byte[] Mutation(byte[] command, uint session, uint revision)
    {
        if (command.Length < 2 || command[0] != 0x6D || command[1] is < 1 or > 8 || session == 0)
            throw new ArgumentException("Invalid session mutation.");
        var result = new byte[command.Length + 8];
        command.AsSpan(0, 2).CopyTo(result);
        Put(result, 2, session);
        Put(result, 6, revision);
        command.AsSpan(2).CopyTo(result.AsSpan(10));
        return result;
    }

    public static IEnumerable<int> ChangedWindows(byte[] before, byte[] after)
    {
        if (before.Length != after.Length || before.Length < PatchBytes || (before.Length & 1) != 0)
            throw new ArgumentException("Invalid table sizes.");
        for (var i = 0; i < before.Length;)
        {
            if (before[i] == after[i]) { i++; continue; }
            var at = Math.Min(i / PatchBytes * PatchBytes, before.Length - PatchBytes);
            yield return at;
            i = at + PatchBytes;
        }
    }

    public static (OverlayReason? Reason, uint Revision) Response(byte[] response)
    {
        OverlayContract.ValidateFrame(response);
        if (response.Length == 8 && response[2] == 0xA0) return (null, Get(response, 3));
        return (OverlayContract.Refusal(response), 0);
    }

    /// <summary>Encodes a patch; pass the verified catalogue for IDs beyond the original 124 maps.</summary>
    public static byte[] Patch(int table, int at, uint session, uint revision, ReadOnlySpan<byte> eightBytes, IReadOnlyList<OverlayMap>? catalogue = null)
    {
        var maps = catalogue ?? OverlayCatalogue.BaseMaps;
        if (session == 0 || revision == uint.MaxValue || eightBytes.Length != 8
            || (table != 255 && (table < 0 || table >= maps.Count || at < 0 || (at & 1) != 0 || at + 8 > maps[table].Bytes))
            || (table == 255 && (at != 0 || (eightBytes[7] & ~0x81) != 0)))
            throw new ArgumentException("Invalid live patch.");
        var p = new byte[26]; p[0] = 0x6D; p[1] = 9;
        Put(p, 2, session); Put(p, 6, revision); p[10] = (byte)table;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(12), (ushort)at);
        eightBytes.CopyTo(p.AsSpan(14)); Put(p, 22, OverlayContract.Crc32(p.AsSpan(2, 20))); return p;
    }

    /// <summary>Whether a header is this overlay's: version 10, minor 4.</summary>
    public static bool IsCurrent(OverlayHeader? header) => header is { Version: Version, Minor: Minor };

    /// <summary>Whether a bin carries this overlay in its header at file 0x4B544.</summary>
    public static bool IsCurrent(ReadOnlySpan<byte> image) =>
        OverlayHeader.VersionIn(image) == Version && OverlayHeader.MinorIn(image) == Minor;

    private static uint Get(byte[] data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
    private static void Put(byte[] data, int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at), value);
}
