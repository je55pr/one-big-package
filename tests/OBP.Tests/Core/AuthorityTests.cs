using System.Text.Json;
using OBP.RAC1;
using OBP.RAC2;
using OBP.RAC3;

namespace OBP.Tests;

/// <summary>
/// The C# authority constants must stay in step with the canonical manifests in
/// <c>research/manifests/</c> — the same invariant the TypeScript
/// <c>importer-probes</c> tests enforce.
/// </summary>
public class AuthorityTests
{
    private static JsonElement Manifest(string file)
    {
        var path = Path.Combine(RepoPaths.Manifests, file);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("rac1-ntscu.json", "rac1-ntscu-original", "SCUS-97199")]
    [InlineData("rac2-ntscu-v1.01.json", "rac2-ntscu-v1.01", "SCUS-97268")]
    [InlineData("rac3-ntscu.json", "rac3-ntscu-original", "SCUS-97353")]
    public void ManifestMatchesConstants(string file, string buildId, string serial)
    {
        var m = Manifest(file);
        Assert.Equal(buildId, m.GetProperty("buildId").GetString());
        Assert.Equal(serial, m.GetProperty("serial").GetString());

        var (constBuildId, constSerial, constSha) = buildId switch
        {
            "rac1-ntscu-original" => (Rac1Authority.Primary.BuildId, Rac1Authority.Primary.Serial, Rac1Authority.Primary.Sha256),
            "rac2-ntscu-v1.01" => (Rac2Authority.Primary.BuildId, Rac2Authority.Primary.Serial, Rac2Authority.Primary.Sha256),
            "rac3-ntscu-original" => (Rac3Authority.Primary.BuildId, Rac3Authority.Primary.Serial, Rac3Authority.Primary.Sha256),
            _ => throw new ArgumentOutOfRangeException(nameof(buildId)),
        };

        Assert.Equal(buildId, constBuildId);
        Assert.Equal(serial, constSerial);
        Assert.Equal(m.GetProperty("payload").GetProperty("sha256").GetString(), constSha);
    }

    [Fact]
    public void OnlyThePrimaryRac2BuildIsSupportedForNativeImport()
    {
        Assert.Contains("rac2-ntscu-v1.01", Rac2Authority.SupportedBuildIds);
        Assert.DoesNotContain("rac2-gh-v2.00", Rac2Authority.SupportedBuildIds);
    }
}
