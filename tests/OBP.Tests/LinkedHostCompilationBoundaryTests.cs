using System.Reflection;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

/// <summary>
/// Source-linked host tests compile a second copy of OBP.Game types. Guard the
/// boundary, not the number of links: independent diagnostic extractions add
/// legitimate links without making them shared-runtime assemblies.
/// </summary>
public sealed class LinkedHostCompilationBoundaryTests
{
    private static readonly string[] SharedAssemblies =
    [
        "OBP.Core", "OBP.Fusion", "OBP.Godot", "OBP.IO", "OBP.PS2",
        "OBP.RAC1", "OBP.RAC2", "OBP.RAC3", "OBP.Runtime"
    ];

    private static XElement[] Links()
    {
        string project = Path.Combine(RepoPaths.Root, "tests", "OBP.Tests", "OBP.Tests.csproj");
        return XDocument.Load(project).Descendants("Compile")
            .Where(element => element.Attribute("Include")?.Value.StartsWith("../../game/", StringComparison.Ordinal) == true)
            .ToArray();
    }

    [Fact]
    public void ExplicitGameLinksResolveToOneCanonicalGameSourceAndVirtualPath()
    {
        string tests = Path.Combine(RepoPaths.Root, "tests", "OBP.Tests");
        var links = Links();
        Assert.NotEmpty(links);
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var virtualPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            string include = (string)link.Attribute("Include")!;
            string name = include["../../game/".Length..];
            Assert.True(name.StartsWith("scripts/", StringComparison.Ordinal) ||
                name.StartsWith("diagnostics/", StringComparison.Ordinal), include);
            Assert.DoesNotContain('\\', name);
            Assert.DoesNotContain("..", name, StringComparison.Ordinal);
            Assert.EndsWith(".cs", name, StringComparison.Ordinal);
            string expectedLink = (name.StartsWith("scripts/", StringComparison.Ordinal) ? "Game/" : "Diagnostics/") +
                name[(name.IndexOf('/') + 1)..];
            Assert.Equal(expectedLink, (string?)link.Attribute("Link"));
            Assert.True(sources.Add(include), $"Duplicate physical compile link: {include}");
            Assert.True(virtualPaths.Add(expectedLink), $"Duplicate virtual compile path: {expectedLink}");
            Assert.True(File.Exists(Path.Combine(tests, include)), include);
        }

        var gameProject = XDocument.Load(Path.Combine(RepoPaths.Root, "game", "OBP.Game.csproj"));
        Assert.DoesNotContain(gameProject.Descendants("Compile"), element => element.Attribute("Remove") is not null);
        var testsProject = XDocument.Load(Path.Combine(tests, "OBP.Tests.csproj"));
        Assert.DoesNotContain(testsProject.Descendants("ProjectReference"),
            element => (string?)element.Attribute("Include") is { } value && value.Contains("OBP.Game.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void LinkedFilesCannotBranchOnGameOnlyCompilationSymbols()
    {
        foreach (var link in Links())
        {
            string source = (string)link.Attribute("Include")!;
            string path = Path.Combine(RepoPaths.Root, "tests", "OBP.Tests", source);
            Assert.False(HasConditionalCompilation(File.ReadAllText(path)),
                $"{source}: OBP.Game receives GODOT/GODOT4 symbols but OBP.Tests does not. " +
                "Extract an engine-neutral contract or test both compilation modes explicitly.");
        }
    }

    [Theory]
    [InlineData("#if GODOT\npublic class GameOnly {}\n#endif\n", true)]
    [InlineData("#if DEBUG\npublic class DebugOnly {}\n#endif\n", true)]
    [InlineData("#define HOST\npublic class Conditional {}\n", true)]
    [InlineData("#pragma warning disable CS0168\npublic class Portable {}\n", false)]
    [InlineData("public class Portable {}\n", false)]
    public void CompilationModeGuardRecognizesDivergentSyntax(string source, bool expected) =>
        Assert.Equal(expected, HasConditionalCompilation(source));

    private static bool HasConditionalCompilation(string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantTrivia(descendIntoTrivia: true)
            .Any(trivia => trivia.GetStructure() is IfDirectiveTriviaSyntax or ElifDirectiveTriviaSyntax
                or DefineDirectiveTriviaSyntax or UndefDirectiveTriviaSyntax);

    [Fact]
    public void LinkedHostTypesDoNotDuplicateExistingSharedAssemblyTypes()
    {
        var published = SharedAssemblies.SelectMany(name => Assembly.Load(name).GetTypes())
            .Where(type => type.FullName is not null).Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var link in Links())
        {
            string source = (string)link.Attribute("Include")!;
            string path = Path.Combine(RepoPaths.Root, "tests", "OBP.Tests", source);
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
            foreach (var type in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                string namespaceName = string.Join(".", type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
                    .Reverse().Select(declaration => declaration.Name.ToString()));
                string nested = string.Join("+", type.Ancestors().OfType<BaseTypeDeclarationSyntax>()
                    .Reverse().Select(parent => parent.Identifier.ValueText).Append(type.Identifier.ValueText));
                string identity = (namespaceName.Length == 0 ? "" : namespaceName + ".") + nested;
                Assert.DoesNotContain(identity, published);
            }
        }
    }
}
