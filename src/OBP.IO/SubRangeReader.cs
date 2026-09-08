namespace OBP.IO;

/// <summary>
/// A fixed window over a parent reader — exposes a native container's inner
/// section as its own bounded source without copying it. Mirrors the
/// TypeScript <c>SubRangeReader</c>.
/// </summary>
public sealed class SubRangeReader : IRandomAccessReader
{
    private readonly IRandomAccessReader _source;
    private readonly long _start;

    public string Name { get; }
    public long Length { get; }

    public SubRangeReader(IRandomAccessReader source, long start, long length, string? name = null)
    {
        if (start < 0 || length < 0 || start > source.Length || length > source.Length - start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                $"Sub-range {start}+{length} lies outside {source.Name} (length {source.Length}).");
        }

        _source = source;
        _start = start;
        Length = length;
        Name = name ?? $"{source.Name}[{start}..{start + length}]";
    }

    public void Read(long offset, Span<byte> destination)
    {
        RandomAccessReaderExtensions.ValidateRange(Name, Length, offset, destination.Length);
        _source.Read(_start + offset, destination);
    }
}
