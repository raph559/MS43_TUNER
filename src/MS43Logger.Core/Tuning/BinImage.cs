namespace MS43Logger.Core.Tuning;

/// <summary>A 512 KiB MS43 flash image. CPU addresses are mirrored every 512 KiB: file offset = address &amp; 0x7FFFF.</summary>
public sealed class BinImage
{
    public const int Size = 524288;
    private const long AddressMask = Size - 1;

    private readonly byte[] _bytes;

    private BinImage(byte[] bytes, string name)
    {
        _bytes = bytes;
        Name = name;
    }

    public string Name { get; }

    public ReadOnlySpan<byte> Bytes => _bytes;

    public static BinImage Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromBytes(File.ReadAllBytes(path), Path.GetFileName(path));
    }

    public static BinImage FromBytes(byte[] bytes, string name = "image.bin")
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length != Size)
        {
            throw new InvalidDataException($"{name} is {bytes.Length} bytes; an MS43 bin must be exactly {Size} bytes (512 KiB).");
        }

        return new BinImage((byte[])bytes.Clone(), name);
    }

    public static int ToOffset(long address) => (int)(address & AddressMask);

    public bool Contains(XdfData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.Address is long address && ToOffset(address) + (long)data.Count * data.ElementBytes <= Size;
    }

    public BinImage Clone() => new((byte[])_bytes.Clone(), Name);

    public byte[] ToArray() => (byte[])_bytes.Clone();

    /// <summary>Contiguous byte ranges where this image differs from <paramref name="other"/>.</summary>
    public IReadOnlyList<(int Offset, int Length)> DiffRanges(BinImage other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var ranges = new List<(int Offset, int Length)>();
        var start = -1;
        for (var i = 0; i <= Size; i++)
        {
            var differs = i < Size && _bytes[i] != other._bytes[i];
            if (differs && start < 0)
            {
                start = i;
            }
            else if (!differs && start >= 0)
            {
                ranges.Add((start, i - start));
                start = -1;
            }
        }

        return ranges;
    }

    /// <summary>Writes element <paramref name="index"/> (row-major) of <paramref name="data"/>; nothing changes when the value or position is invalid.</summary>
    public void WriteElement(XdfData data, int index, long raw)
    {
        var offset = ElementOffset(data, index);
        var bits = data.ElementBits;
        var (min, max) = data.Signed ? (-(1L << (bits - 1)), (1L << (bits - 1)) - 1) : (0L, (1L << bits) - 1);
        if (raw < min || raw > max)
        {
            throw new ArgumentOutOfRangeException(nameof(raw), $"{raw} does not fit a {(data.Signed ? "signed" : "unsigned")} {bits}-bit element ({min}…{max}).");
        }

        var size = data.ElementBytes;
        var value = (ulong)raw;
        for (var i = 0; i < size; i++)
        {
            _bytes[offset + (data.LsbFirst ? i : size - 1 - i)] = (byte)(value >> (8 * i));
        }
    }

    /// <summary>Reads element <paramref name="index"/> (row-major) of <paramref name="data"/>.</summary>
    public long ReadElement(XdfData data, int index)
    {
        var offset = ElementOffset(data, index);
        var size = data.ElementBytes;
        ulong value = 0;
        for (var i = 0; i < size; i++)
        {
            var b = _bytes[offset + (data.LsbFirst ? i : size - 1 - i)];
            value |= (ulong)b << (8 * i);
        }

        if (data.Signed)
        {
            var shift = 64 - data.ElementBits;
            return (long)(value << shift) >> shift;
        }

        return (long)value;
    }

    private static int ElementOffset(XdfData data, int index)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Address is not long address || data.ElementBits is not (8 or 16 or 32))
        {
            throw new ArgumentOutOfRangeException(nameof(data), "The data has no readable storage.");
        }

        var offset = ToOffset(address) + (long)index * data.ElementBytes;
        if (index < 0 || offset + data.ElementBytes > Size)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"Element {index} at 0x{address:X} lies outside the {Size}-byte image.");
        }

        return (int)offset;
    }
}
