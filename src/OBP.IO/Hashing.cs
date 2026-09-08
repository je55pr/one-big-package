using System.Security.Cryptography;

namespace OBP.IO;

/// <summary>
/// Incremental SHA-256 over a random-access source without materialising it.
/// The native equivalent of the TypeScript streaming hasher; used for exact
/// authority / build verification against <c>research/manifests/</c>.
/// </summary>
public static class Hashing
{
    private const int ChunkSize = 4 * 1024 * 1024;

    /// <summary>
    /// Lower-case hex SHA-256 of the whole reader (or a sub-range). Optionally
    /// reports fractional progress (0..1) after each chunk and honours a
    /// cancellation token — a multi-gigabyte disc hash runs on a background
    /// thread in the interactive loader.
    /// </summary>
    public static string Sha256Hex(
        IRandomAccessReader reader,
        long offset = 0,
        long? length = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        long total = length ?? reader.Length - offset;
        RandomAccessReaderExtensions.ValidateRange(reader.Name, reader.Length, offset, total);

        using var sha = SHA256.Create();
        var buffer = new byte[ChunkSize];
        long done = 0;
        while (done < total)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int want = (int)System.Math.Min(ChunkSize, total - done);
            reader.Read(offset + done, buffer.AsSpan(0, want));
            sha.TransformBlock(buffer, 0, want, null, 0);
            done += want;
            progress?.Report(total == 0 ? 1.0 : (double)done / total);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
}
