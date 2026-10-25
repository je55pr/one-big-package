using System.Text.Json;
using System.Xml.Linq;

namespace OBP.Tests;

public sealed class SourceNamingPolicyTests
{
    private sealed record LegacyPolicy(string Owner, string Rationale, string[] Declarations);
    private static string PolicyPath => Path.Combine(RepoPaths.Root, "tests", "OBP.Tests", "source-naming-legacy.json");

    [Fact]
    public void PublicSourceNamesCannotGrowBeyondTheExactMigrationCensus()
    {
        var findings = new[] { "src", "game", "tests" }.SelectMany(directory => Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, directory), "*.cs", SearchOption.AllDirectories))
            .Select(path => (Full: path, Relative: Path.GetRelativePath(RepoPaths.Root, path).Replace('\\', '/')))
            .Where(item => !item.Relative.Contains("/obj/", StringComparison.Ordinal) && !item.Relative.Contains("/bin/", StringComparison.Ordinal) && !item.Relative.Contains("/.godot/", StringComparison.Ordinal))
            .SelectMany(item => SourceNamingAudit.Scan(item.Relative, File.ReadAllText(item.Full))).Select(finding => finding.Key).Distinct().Order(StringComparer.Ordinal).ToArray();
        var policy = JsonSerializer.Deserialize<LegacyPolicy[]>(File.ReadAllText(PolicyPath))!;
        Assert.All(policy, entry => { Assert.Contains(entry.Owner, new[] { "#291", "#292", "shared-ps2-evidence" }); Assert.False(string.IsNullOrWhiteSpace(entry.Rationale)); });
        string[] allowed = policy.SelectMany(entry => entry.Declarations).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(allowed.Length, allowed.Distinct(StringComparer.Ordinal).Count());
        Assert.True(findings.SequenceEqual(allowed), "Unapproved names or stale migration entries:\n" + string.Join("\n", findings.Except(allowed).Concat(allowed.Except(findings).Select(key => "REMOVE stale " + key))));
    }

    [Fact]
    public void SourceProjectsRetainCanonicalAssemblyAndNamespaceRoots()
    {
        var errors = new[] { "src", "game", "tests", "tools" }.SelectMany(directory => Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, directory), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .SelectMany(path =>
            {
                var properties = XDocument.Load(path).Descendants("PropertyGroup").Elements();
                return SourceNamingAudit.ProjectErrors(Path.GetRelativePath(RepoPaths.Root, path).Replace('\\', '/'),
                    properties.FirstOrDefault(item => item.Name == "RootNamespace")?.Value, properties.FirstOrDefault(item => item.Name == "AssemblyName")?.Value);
            });
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("src/OBP.RAC2/Gameplay/GcNewSession.cs", "namespace OBP.RAC2.Gameplay; public class GcNewSession {}")]
    [InlineData("src/OBP.RAC3/UyaNewSession.cs", "namespace OBP.RAC3; public interface IUyaNewSession {}")]
    [InlineData("src/OBP.RAC2/Gameplay/Health.cs", "namespace OBP.RAC2.Gameplay; public class UniversalHealth {}")]
    [InlineData("src/OBP.RAC3/UyaFile.cs", "namespace OBP.RAC3; public class Rac3Actor {}")]
    [InlineData("game/scripts/GcNewController.cs", "namespace OneBigPackage; public class GcNewController {}")]
    [InlineData("src/OBP.RAC2/Rac2Delegates.cs", "namespace OBP.RAC2; public delegate void GcNewHandler();")]
    [InlineData("src/OBP.RAC2/Rac2Session.cs", "namespace OBP.RAC2; public class Rac2Session { public class GcNestedAlias {} }")]
    [InlineData("src/OBP.Runtime/Player.cs", "namespace OBP.GC; public class Player {}")]
    [InlineData("src/OBP.RAC2/Rac2Actor.cs", "namespace OneBigPackage; public class Rac2Actor {}")]
    public void NewAliasesMisleadingGenericTypesAndNoncanonicalFilesFail(string path, string source) => Assert.NotEmpty(SourceNamingAudit.Scan(path, source));

    [Theory]
    [InlineData("src/OBP.RAC2/Gameplay/Rac2Session.cs", "namespace OBP.RAC2.Gameplay; public class Rac2Session { public record State(int Tick); }")]
    [InlineData("src/OBP.Runtime/RuntimeWorld.cs", "namespace OBP.Runtime; public record RuntimeWorld(int Tick);")]
    [InlineData("src/OBP.PS2/VifPacket.cs", "namespace OBP.PS2; public class VifPacket {}")]
    [InlineData("game/scripts/PlayerHost.cs", "namespace OneBigPackage; public class PlayerHost {}")]
    public void CanonicalSourceNestedRecordsAndNeutralRetailVocabularyRemainValid(string path, string source) => Assert.Empty(SourceNamingAudit.Scan(path, source));

    [Fact]
    public void DuplicateSourceProjectVocabularyIsRejected()
    {
        Assert.NotEmpty(SourceNamingAudit.ProjectErrors("src/OBP.GC/OBP.GC.csproj", "OBP.GC", null));
        Assert.NotEmpty(SourceNamingAudit.ProjectErrors("src/OBP.RAC3/OBP.RAC3.csproj", "OBP.UYA", null));
        Assert.Empty(SourceNamingAudit.ProjectErrors("src/OBP.PS2/OBP.PS2.csproj", "OBP.PS2", null));
    }
}
