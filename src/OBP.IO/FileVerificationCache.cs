using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OBP.IO;

/// <summary>
/// Reuses a successful full-file verification when file metadata and a bounded
/// sampled fingerprint still match. A cache hit is a local trust decision, not
/// a new full-file authority proof. No source payload is persisted.
/// </summary>
public sealed class FileVerificationCache(string directory)
{
    public sealed record Result(string Sha256, bool Reused);
    private sealed record Stamp(long Length, long LastWriteUtcTicks, long CreationUtcTicks);
    private sealed record Entry(int Version, string Sha256, Stamp File, string Fingerprint);
    private const int SampleBytes = 64 * 1024, SampleCount = 16;

    public Result Verify(string path, IRandomAccessReader reader, string expectedSha256,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        string normalized = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized + "\n" + expectedSha256.ToLowerInvariant())));
        string cachePath = Path.Combine(directory, key + ".json");
        var before = ReadStamp(path);
        if (before.Length != reader.Length) throw new IOException("Source size changed while opening it.");
        var saved = ReadEntry(cachePath);
        if (saved is { Version: 1 } && saved.File == before &&
            string.Equals(saved.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase) &&
            saved.Fingerprint == Fingerprint(reader, cancellationToken) && ReadStamp(path) == before)
            return new(saved.Sha256, true);

        string hash = Hashing.Sha256Hex(reader, progress: progress, cancellationToken: cancellationToken);
        if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Disc SHA-256 differs from the supported original NTSC-U build.");
        string fingerprint = Fingerprint(reader, cancellationToken);
        if (ReadStamp(path) != before) throw new IOException("Source changed during verification. Try again after it stops changing.");
        cancellationToken.ThrowIfCancellationRequested();
        WriteEntry(cachePath, new(1, hash, before, fingerprint));
        return new(hash, false);
    }

    private static Stamp ReadStamp(string path)
    {
        var file = new FileInfo(path);
        return new(file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks);
    }

    private static string Fingerprint(IRandomAccessReader reader, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bytes = new byte[SampleBytes];
        Span<byte> position = stackalloc byte[8];
        int count = (int)Math.Min(SampleBytes, reader.Length);
        int samples = reader.Length <= SampleBytes ? 1 : SampleCount;
        for (int i = 0; i < samples; i++)
        {
            token.ThrowIfCancellationRequested();
            long offset = samples == 1 ? 0 : (reader.Length - count) * i / (samples - 1);
            BinaryPrimitives.WriteInt64LittleEndian(position, offset);
            hash.AppendData(position);
            reader.Read(offset, bytes.AsSpan(0, count));
            hash.AppendData(bytes.AsSpan(0, count));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static Entry? ReadEntry(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 16 * 1024) return null;
            return JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static void WriteEntry(string path, Entry entry)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(entry));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
