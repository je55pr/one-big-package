namespace OBP.IO;

/// <summary>
/// Presents ordered split parts as one logical source without reassembling
/// them. A read that crosses a part boundary asks each underlying reader only
/// for the intersecting range. Mirrors the TypeScript
/// <c>ConcatenatedRandomAccessReader</c>.
/// </summary>
public sealed class ConcatenatedRandomAccessReader : IRandomAccessReader
{
    private readonly IRandomAccessReader[] _parts;
    private readonly long[] _starts;

    public string Name { get; }
    public long Length { get; }

    public ConcatenatedRandomAccessReader(IReadOnlyList<IRandomAccessReader> parts, string name = "concatenated")
    {
        if (parts.Count == 0)
        {
            throw new ArgumentException("At least one source part is required.", nameof(parts));
        }

        _parts = parts.ToArray();
        _starts = new long[_parts.Length];
        long size = 0;
        for (int i = 0; i < _parts.Length; i++)
        {
            if (_parts[i].Length < 0)
            {
                throw new ArgumentException($"Invalid source length for {_parts[i].Name}: {_parts[i].Length}");
            }

            _starts[i] = size;
            size += _parts[i].Length;
        }

        Length = size;
        Name = name;
    }

    public void Read(long offset, Span<byte> destination)
    {
        RandomAccessReaderExtensions.ValidateRange(Name, Length, offset, destination.Length);
        if (destination.Length == 0)
        {
            return;
        }

        int written = 0;
        long cursor = offset;
        int part = FindPart(cursor);

        while (written < destination.Length)
        {
            long partStart = _starts[part];
            var reader = _parts[part];
            long local = cursor - partStart;
            int chunk = (int)System.Math.Min(destination.Length - written, reader.Length - local);
            if (chunk <= 0)
            {
                part++;
                continue;
            }

            reader.Read(local, destination.Slice(written, chunk));
            written += chunk;
            cursor += chunk;
            part++;
        }
    }

    private int FindPart(long offset)
    {
        int low = 0, high = _parts.Length - 1;
        while (low <= high)
        {
            int mid = (low + high) >>> 1;
            long start = _starts[mid];
            long end = start + _parts[mid].Length;
            if (offset < start)
            {
                high = mid - 1;
            }
            else if (offset >= end)
            {
                low = mid + 1;
            }
            else
            {
                return mid;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(offset), $"No source part covers offset {offset}.");
    }
}
