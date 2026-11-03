using System.Security.Cryptography;
using OBP.IO;

namespace OBP.Tests;

public class IoTests
{
    private static string WriteTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"obp-io-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void FileReader_ReadsExactRangesAndRejectsOutOfBounds()
    {
        var data = new byte[4096];
        Random.Shared.NextBytes(data);
        var path = WriteTempFile(data);
        try
        {
            using var reader = new FileRandomAccessReader(path);
            Assert.Equal(4096, reader.Length);

            var slice = reader.Read(100, 32);
            Assert.Equal(data.AsSpan(100, 32).ToArray(), slice);

            Assert.Equal(Array.Empty<byte>(), reader.Read(4096, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(4090, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(-1, 1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SubRangeReader_WindowsAParentReader()
    {
        var data = new byte[1000];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)i;
        }

        var path = WriteTempFile(data);
        try
        {
            using var file = new FileRandomAccessReader(path);
            var window = new SubRangeReader(file, 200, 300, "window");
            Assert.Equal(300, window.Length);
            Assert.Equal(data.AsSpan(250, 10).ToArray(), window.Read(50, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => window.Read(295, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SubRangeReader(file, 900, 200));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sha256_MatchesBclOverTheWholeSourceAndSubRanges()
    {
        var data = new byte[5 * 1024 * 1024 + 123];
        Random.Shared.NextBytes(data);
        var path = WriteTempFile(data);
        try
        {
            using var reader = new FileRandomAccessReader(path);

            var whole = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            Assert.Equal(whole, Hashing.Sha256Hex(reader));

            var sub = Convert.ToHexString(SHA256.HashData(data.AsSpan(1000, 2_000_000))).ToLowerInvariant();
            Assert.Equal(sub, Hashing.Sha256Hex(reader, 1000, 2_000_000));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
