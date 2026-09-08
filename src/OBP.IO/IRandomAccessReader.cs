namespace OBP.IO;

/// <summary>
/// Bounded random access over a byte source. The native equivalent of the
/// TypeScript <c>RandomAccessReader</c>. A multi-gigabyte ISO is a normal
/// seekable file here — never a single in-memory byte array.
/// </summary>
public interface IRandomAccessReader
{
    /// <summary>A short human name for diagnostics (usually the file name).</summary>
    string Name { get; }

    /// <summary>Total length of the logical source in bytes.</summary>
    long Length { get; }

    /// <summary>
    /// Read exactly <paramref name="destination"/>.Length bytes starting at
    /// <paramref name="offset"/>. Throws if the range lies outside the source or
    /// the underlying source returns short.
    /// </summary>
    void Read(long offset, Span<byte> destination);
}

public static class RandomAccessReaderExtensions
{
    /// <summary>Read <paramref name="length"/> bytes into a fresh array.</summary>
    public static byte[] Read(this IRandomAccessReader reader, long offset, int length)
    {
        var buffer = new byte[length];
        reader.Read(offset, buffer);
        return buffer;
    }

    public static void ValidateRange(string name, long length, long offset, long readLength)
    {
        if (offset < 0 || readLength < 0 || offset > length || readLength > length - offset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                $"Invalid read {name} @ {offset}+{readLength} (source length {length}).");
        }
    }
}
