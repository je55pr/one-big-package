using OBP.IO;
using OBP.RAC2;
using OBP.Tests.Helpers;

namespace OBP.Tests;

/// <summary>
/// Checkpoint F: the "open a Going Commando disc image" façade the native file
/// picker sits on. The fast <see cref="GcIsoLoad.Identify"/> gate is covered with
/// synthetic discs; the full import + SHA-256 authority verification is
/// retail-gated on <c>OBP_GC_ISO</c>.
/// </summary>
public class GcIsoLoadTests
{
    [Fact]
    public void Identify_AcceptsTheSupportedGoingCommandoSerial()
    {
        var fixture = SyntheticPs2.Build("SCUS_972.68");
        var id = GcIsoLoad.Identify(new InMemoryReader("gc.iso", fixture.Bytes));

        Assert.True(id.Supported);
        Assert.Equal("SCUS-97268", id.DiscSerial);
        Assert.Equal("rac2-ntscu-v1.01", id.BuildId);
        Assert.Null(id.Problem);
    }

    [Fact]
    public void Identify_RejectsA_DifferentDiscSerial()
    {
        var fixture = SyntheticPs2.Build("SCUS_971.99"); // R&C1
        var id = GcIsoLoad.Identify(new InMemoryReader("rc1.iso", fixture.Bytes));

        Assert.False(id.Supported);
        Assert.Equal("SCUS-97199", id.DiscSerial);
        Assert.Contains("SCUS-97199", id.Problem);
        Assert.Contains("SCUS-97268", id.Problem);
    }

    [Fact]
    public void Identify_RejectsSomethingThatIsNotADiscImage()
    {
        var junk = new byte[64 * 1024];
        Random.Shared.NextBytes(junk);
        var id = GcIsoLoad.Identify(new InMemoryReader("notes.txt", junk));

        Assert.False(id.Supported);
        Assert.Null(id.DiscSerial);
        Assert.Contains("Could not read a PS2 disc image", id.Problem);
    }

    [Fact]
    public void PrimaryManifest_MatchesTheCanonicalGcManifest()
    {
        var embedded = GcIsoLoad.PrimaryManifest();
        var onDisk = AuthorityManifest.Load(Path.Combine(RepoPaths.Manifests, "rac2-ntscu-v1.01.json"));

        Assert.Equal(onDisk.BuildId, embedded.BuildId);
        Assert.Equal(onDisk.Serial, embedded.Serial);
        Assert.Equal(onDisk.PayloadSizeBytes, embedded.PayloadSizeBytes);
        Assert.Equal(onDisk.PayloadSha256, embedded.PayloadSha256);
    }

    [SkippableFact]
    public void RetailIso_LoadsOozlaAndVerifiesAgainstTheAuthorityHash()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);

        var load = GcIsoLoad.LoadLevel(reader, 1);
        Assert.True(load.Identity.Supported);
        Assert.True(load.Identity.SizeMatches);
        // 2 oc1134 instances are lifted out as animated mobies; class-500 Bolt
        // Crates are preserved separately as dynamic gameplay objects.
        Assert.Equal(302, load.World.Meshes.Count);
        Assert.Equal(1_860_879, load.World.TotalRenderTriangles);
        Assert.Equal(21_470, load.World.TotalDynamicTriangles);
        Assert.Equal(1_882_349, load.World.TotalRenderTriangles + load.World.TotalDynamicTriangles);
        Assert.Equal(190, load.World.DynamicObjects!.Count(o => o.NativeClassId == 500));
        Assert.Equal(2, load.World.AnimatedMeshes!.Count);

        var identity = GcIsoLoad.Verify(reader);

        Assert.Equal("rac2-ntscu-v1.01", identity.BuildId);
        Assert.Equal(Rac2Authority.Primary.Sha256, identity.Sha256);
    }
}
