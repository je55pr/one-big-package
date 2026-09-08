using System.Security.Cryptography;
using System.Text.Json;
using OBP.Core;
using OBP.IO;
using OBP.PS2;
using OBP.Tests.Helpers;

namespace OBP.Tests;

public class VerificationTests
{
    private static string WriteManifest(string game, string buildId, string? serial, long size, string sha256)
    {
        var obj = new
        {
            schemaVersion = 2,
            game,
            buildId,
            region = "NTSC-U",
            serial,
            revision = "test",
            payload = new { filename = "x.iso", sizeBytes = size, sha256 },
        };
        var path = Path.Combine(Path.GetTempPath(), $"obp-manifest-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(obj));
        return path;
    }

    [Fact]
    public void BuildVerification_ReturnsIdentityOnlyWhenSizeAndHashMatch()
    {
        var fixture = SyntheticPs2.Build("SCUS_972.68");
        var reader = new InMemoryReader("synthetic.iso", fixture.Bytes);
        string sha = Convert.ToHexString(SHA256.HashData(fixture.Bytes)).ToLowerInvariant();

        var good = AuthorityManifest.Load(WriteManifest("rac2", "rac2-test", "SCUS-97268", fixture.Bytes.Length, sha));
        var identity = BuildVerification.Verify(reader, good);
        Assert.Equal(ObpSourceGame.Rac2, identity.Game);
        Assert.Equal("rac2-test", identity.BuildId);
        Assert.Equal(sha, identity.Sha256);

        var wrongSize = AuthorityManifest.Load(WriteManifest("rac2", "rac2-test", null, fixture.Bytes.Length + 1, sha));
        Assert.Throws<InvalidDataException>(() => BuildVerification.Verify(reader, wrongSize));

        var wrongHash = AuthorityManifest.Load(WriteManifest("rac2", "rac2-test", null, fixture.Bytes.Length, new string('0', 64)));
        Assert.Throws<InvalidDataException>(() => BuildVerification.Verify(reader, wrongHash));
    }

    [Fact]
    public void Ps2BuildIdentification_CrossChecksTheDiscSerial()
    {
        var fixture = SyntheticPs2.Build("SCUS_972.68");
        var reader = new InMemoryReader("synthetic.iso", fixture.Bytes);
        string sha = Convert.ToHexString(SHA256.HashData(fixture.Bytes)).ToLowerInvariant();

        var matching = AuthorityManifest.Load(WriteManifest("rac2", "rac2-test", "SCUS-97268", fixture.Bytes.Length, sha));
        Assert.Equal("SCUS-97268", Ps2BuildIdentification.IdentifyAndVerify(reader, matching).Serial);

        var wrongSerial = AuthorityManifest.Load(WriteManifest("rac2", "rac2-test", "SCUS-99999", fixture.Bytes.Length, sha));
        Assert.Throws<InvalidDataException>(() => Ps2BuildIdentification.IdentifyAndVerify(reader, wrongSerial));
    }

    [Fact]
    public void ManifestLoader_ReadsTheCanonicalGcManifest()
    {
        var m = AuthorityManifest.Load(Path.Combine(RepoPaths.Manifests, "rac2-ntscu-v1.01.json"));
        Assert.Equal("rac2-ntscu-v1.01", m.BuildId);
        Assert.Equal("SCUS-97268", m.Serial);
        Assert.Equal(3_828_350_976, m.PayloadSizeBytes);
        Assert.Equal("9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5", m.PayloadSha256);
    }

    // The real end-to-end check: a retail Going Commando ISO must pass
    // ISO-9660 → SYSTEM.CNF → SCUS-97268 → exact size + SHA-256 verification
    // against research/manifests/. Skipped unless OBP_GC_ISO points at one.
    [SkippableFact]
    public void RetailGoingCommando_IdentifiesAndVerifiesAgainstTheAuthorityManifest()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        var manifest = AuthorityManifest.Load(Path.Combine(RepoPaths.Manifests, "rac2-ntscu-v1.01.json"));

        var boot = Ps2Boot.ReadBootInfo(reader);
        Assert.Equal("SCUS-97268", boot.Serial);
        Assert.Equal("BOOT2", boot.BootKey);

        var identity = Ps2BuildIdentification.IdentifyAndVerify(reader, manifest);
        Assert.Equal("rac2-ntscu-v1.01", identity.BuildId);
        Assert.Equal(ObpSourceGame.Rac2, identity.Game);
        Assert.Equal(manifest.PayloadSha256, identity.Sha256);
    }
}
