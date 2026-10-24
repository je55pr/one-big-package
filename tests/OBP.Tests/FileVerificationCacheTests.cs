using System.Security.Cryptography;
using OBP.IO;

namespace OBP.Tests;

public sealed class FileVerificationCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "obp-verification-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_directory, "source.bin");
    private string Cache => Path.Combine(_directory, "verified");

    private string CreateSource()
    {
        Directory.CreateDirectory(_directory);
        var bytes = new byte[3 * 1024 * 1024];
        new Random(17).NextBytes(bytes);
        File.WriteAllBytes(Source, bytes);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    [Fact]
    public void FreshProcessReusesSuccessfulVerificationWithAtMostOneMegabyteOfReads()
    {
        string hash = CreateSource();
        using (var disc = new FileRandomAccessReader(Source))
            Assert.False(new FileVerificationCache(Cache).Verify(Source, disc, hash).Reused);
        using var reader = new CountingReader(new FileRandomAccessReader(Source));
        var result = new FileVerificationCache(Cache).Verify(Source, reader, hash);
        Assert.True(result.Reused);
        Assert.Equal(hash, result.Sha256);
        Assert.InRange(reader.BytesRead, 1, 1024 * 1024);
    }

    [Fact]
    public void ChangedTimestampRequiresFullVerificationEvenIfBytesMatch()
    {
        string hash = CreateSource();
        using (var disc = new FileRandomAccessReader(Source)) new FileVerificationCache(Cache).Verify(Source, disc, hash);
        File.SetLastWriteTimeUtc(Source, File.GetLastWriteTimeUtc(Source).AddSeconds(5));
        using var reader = new CountingReader(new FileRandomAccessReader(Source));
        Assert.False(new FileVerificationCache(Cache).Verify(Source, reader, hash).Reused);
        Assert.True(reader.BytesRead >= reader.Length);
    }

    [Fact]
    public void SampledContentChangeCannotReuseCacheEvenWhenTimestampAndSizeArePreserved()
    {
        string hash = CreateSource();
        using (var disc = new FileRandomAccessReader(Source)) new FileVerificationCache(Cache).Verify(Source, disc, hash);
        var stamp = File.GetLastWriteTimeUtc(Source);
        using (var write = File.OpenWrite(Source)) write.WriteByte(0xff);
        File.SetLastWriteTimeUtc(Source, stamp);
        using var reader = new FileRandomAccessReader(Source);
        Assert.Throws<InvalidDataException>(() => new FileVerificationCache(Cache).Verify(Source, reader, hash));
    }

    [Fact]
    public void CorruptCacheFallsBackToFullVerification()
    {
        string hash = CreateSource();
        using (var disc = new FileRandomAccessReader(Source)) new FileVerificationCache(Cache).Verify(Source, disc, hash);
        File.WriteAllText(Directory.GetFiles(Cache, "*.json").Single(), "{broken");
        using var reader = new FileRandomAccessReader(Source);
        Assert.False(new FileVerificationCache(Cache).Verify(Source, reader, hash).Reused);
    }

    [Fact]
    public void RejectedHashIsNeverCached()
    {
        CreateSource();
        using var reader = new FileRandomAccessReader(Source);
        Assert.Throws<InvalidDataException>(() => new FileVerificationCache(Cache).Verify(Source, reader, new string('0', 64)));
        Assert.False(Directory.Exists(Cache));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class CountingReader(FileRandomAccessReader inner) : IRandomAccessReader, IDisposable
    {
        public string Name => inner.Name;
        public long Length => inner.Length;
        public long BytesRead { get; private set; }
        public void Read(long offset, Span<byte> destination) { inner.Read(offset, destination); BytesRead += destination.Length; }
        public void Dispose() => inner.Dispose();
    }
}
