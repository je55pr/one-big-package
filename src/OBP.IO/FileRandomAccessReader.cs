namespace OBP.IO;

/// <summary>
/// <see cref="IRandomAccessReader"/> backed by an ordinary seekable file. Opens
/// the file read-only with sharing so several readers can view the same ISO;
/// each <see cref="Read"/> seeks and reads only the requested range.
/// </summary>
public sealed class FileRandomAccessReader : IRandomAccessReader, IDisposable
{
    private readonly FileStream _stream;
    private readonly object _gate = new();

    public string Name { get; }
    public long Length { get; }

    public FileRandomAccessReader(string path, string? name = null)
    {
        _stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.RandomAccess);
        Length = _stream.Length;
        Name = name ?? Path.GetFileName(path);
    }

    public void Read(long offset, Span<byte> destination)
    {
        RandomAccessReaderExtensions.ValidateRange(Name, Length, offset, destination.Length);
        if (destination.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            _stream.Seek(offset, SeekOrigin.Begin);
            int read = 0;
            while (read < destination.Length)
            {
                int n = _stream.Read(destination[read..]);
                if (n <= 0)
                {
                    throw new EndOfStreamException($"Short read from {Name} @ {offset}+{destination.Length} (got {read}).");
                }

                read += n;
            }
        }
    }

    public void Dispose() => _stream.Dispose();
}
