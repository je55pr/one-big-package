using System.Xml.Linq;

namespace OBP.Tests;

/// <summary>
/// Guard assembly boundaries rather than relying on conventions in documentation.
/// Game-specific retail archaeology must never become a Godot parser dependency.
/// </summary>
public sealed class ArchitectureLayeringTests
{
    private static readonly string[] GameModules = ["OBP.RAC1", "OBP.RAC2", "OBP.RAC3"];

    [Fact]
    public void NativeSourceLibrariesCannotDependOnEachOtherOrOnGodot()
    {
        foreach (string game in GameModules)
        {
            var references = ProjectReferences(game);
            foreach (string other in GameModules.Where(m => m != game))
                Assert.DoesNotContain(other, references);
            Assert.DoesNotContain("OBP.Godot", references);
            Assert.DoesNotContain("OneBigPackage", references);

            AssertNoImports(game, "Godot", "OneBigPackage");
        }
    }

    [Fact]
    public void RuntimeModelCannotDependOnNativeParsingOrGodot()
    {
        Assert.Equal(["OBP.Core"], ProjectReferences("OBP.Runtime"));
        AssertNoImports("OBP.Runtime",
            "Godot", "OBP.Godot", "OBP.RAC1", "OBP.RAC2", "OBP.RAC3",
            "OBP.IO", "OBP.PS2", "OneBigPackage");
    }

    [Fact]
    public void GodotAdapterCannotDependOnSourceGameParsers()
    {
        Assert.Equal(
            ["OBP.Composition", "OBP.Core", "OBP.Runtime"],
            ProjectReferences("OBP.Godot"));
        AssertNoImports("OBP.Godot",
            "OBP.RAC1", "OBP.RAC2", "OBP.RAC3",
            "OBP.IO", "OBP.PS2", "OneBigPackage");
    }

    [Fact]
    public void Ps2PresentationIsTheOnlyExplicitSharedPs2ToRuntimeBridge()
    {
        Assert.Equal(
            ["OBP.PS2", "OBP.Runtime"],
            ProjectReferences("OBP.PS2.Presentation"));
        Assert.DoesNotContain("OBP.Godot", ProjectReferences("OBP.PS2.Presentation"));
    }

    private static string[] ProjectReferences(string project)
    {
        string path = Path.Combine(RepoPaths.Root, "src", project, project + ".csproj");
        Assert.True(File.Exists(path), path);
        return XDocument.Load(path)
            .Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AssertNoImports(string project, params string[] forbiddenNamespaces)
    {
        string root = Path.Combine(RepoPaths.Root, "src", project);
        foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            // Exclude generated obj/bin compiler output, which can carry code from
            // other packages and is not authored by this module.
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (string line in File.ReadLines(path))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("using ", StringComparison.Ordinal) &&
                    !trimmed.StartsWith("global using ", StringComparison.Ordinal))
                    continue;
                string import = trimmed.StartsWith("global using ", StringComparison.Ordinal)
                    ? trimmed["global using ".Length..]
                    : trimmed["using ".Length..];
                if (import.StartsWith("static ", StringComparison.Ordinal))
                    import = import["static ".Length..];
                if (import.StartsWith("global::", StringComparison.Ordinal))
                    import = import["global::".Length..];
                foreach (string forbidden in forbiddenNamespaces)
                    Assert.False(
                        import.StartsWith(forbidden + ".", StringComparison.Ordinal) ||
                        import.StartsWith(forbidden + ";", StringComparison.Ordinal),
                        $"{project}: forbidden source import in {relative}: {trimmed}");
            }
        }
    }
}
