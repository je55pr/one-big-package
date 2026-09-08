using OBP.IO;

namespace OBP.Tests.Helpers;

/// <summary>Wraps a reader and records every (offset, length) read — for asserting bounded access.</summary>
public sealed class RecordingReader(IRandomAccessReader inner) : IRandomAccessReader
{
    public List<(long Offset, int Length)> Reads { get; } = [];

    public string Name => inner.Name;
    public long Length => inner.Length;

    public void Read(long offset, Span<byte> destination)
    {
        Reads.Add((offset, destination.Length));
        inner.Read(offset, destination);
    }
}
