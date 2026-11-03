using OBP.IO;
using OBP.RAC1;

namespace OBP.Tests;

/// <summary>
/// Opens the authenticated primary retail R&C1 authority for tests that assert
/// exact source-specific geometry counts or render semantics.
/// </summary>
internal static class Rac1VerifiedDisc
{
    internal static FileRandomAccessReader Open(string path)
    {
        var reader = new FileRandomAccessReader(path);
        try
        {
            Assert.Equal(Rac1Authority.PrimaryIsoSizeBytes, reader.Length);
            string expectedHash = Rac1Authority.Primary.Sha256
                ?? throw new InvalidOperationException("R&C1 authority SHA-256 is missing.");
            var verification = new FileVerificationCache(Path.Combine(
                Path.GetTempPath(), "obp-retail-test-verification"));
            Assert.Equal(expectedHash,
                verification.Verify(path, reader, expectedHash).Sha256,
                ignoreCase: true);
            return reader;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }
}
