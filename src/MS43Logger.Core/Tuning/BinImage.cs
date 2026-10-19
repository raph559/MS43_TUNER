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

    /// <summary>Reads element <paramref name="index"/> (row-major) of <paramref name="data"/>.</summary>
    public long ReadElement(XdfData data, int index)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Address is not long address || data.ElementBits is not (8 or 16 or 32))
        {
            throw new ArgumentOutOfRangeException(nameof(data), "The data has no readable storage.");
        }

        var size = data.ElementBytes;
        var offset = ToOffset(address) + (long)index * size;
        if (index < 0 || offset + size > Size)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"Element {index} at 0x{address:X} lies outside the {Size}-byte image.");
        }

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
}
