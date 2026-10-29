using System.Reflection;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
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

    private static XElement[] Links(XDocument? project = null)
    {
        string tests = Path.Combine(RepoPaths.Root, "tests", "OBP.Tests");
        string game = Path.GetFullPath(Path.Combine(RepoPaths.Root, "game")) + Path.DirectorySeparatorChar;
        return (project ?? XDocument.Load(Path.Combine(tests, "OBP.Tests.csproj"))).Descendants("Compile")
            .Where(element => element.Attribute("Include") is not null)
            .Where(element => Path.GetFullPath(Path.Combine(tests, (string)element.Attribute("Include")!))
                .StartsWith(game, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
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
            string resolved = Path.GetFullPath(Path.Combine(tests, include));
            string name = Path.GetRelativePath(Path.Combine(RepoPaths.Root, "game"), resolved).Replace('\\', '/');
            Assert.Equal("../../game/" + name, include);
            Assert.True(name.StartsWith("scripts/", StringComparison.Ordinal) ||
                name.StartsWith("diagnostics/", StringComparison.Ordinal), include);
            Assert.DoesNotContain('\\', name);
            Assert.DoesNotContain("..", name, StringComparison.Ordinal);
            Assert.EndsWith(".cs", name, StringComparison.Ordinal);
            string expectedLink = (name.StartsWith("scripts/", StringComparison.Ordinal) ? "Game/" : "Diagnostics/") +
                name[(name.IndexOf('/') + 1)..];
            Assert.Equal(expectedLink, (string?)link.Attribute("Link"));
            Assert.True(sources.Add(resolved), $"Duplicate physical compile link: {include}");
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
    public void NonCanonicalGameLinksAreStillDiscovered()
    {
        var project = new XDocument(new XElement("Project",
            new XElement("ItemGroup", new XElement("Compile",
                new XAttribute("Include", "./../../game/scripts/CommandLineArgs.cs"),
                new XAttribute("Link", "Game/CommandLineArgs.cs")))));
        var link = Assert.Single(Links(project));
        string source = (string)link.Attribute("Include")!;
        Assert.NotEqual("../../game/scripts/CommandLineArgs.cs", source);
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

    private static string RuntimeTypeName(INamedTypeSymbol symbol)
    {
        var parts = new Stack<string>();
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.ContainingType)
            parts.Push(current.MetadataName);
        string prefix = symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString() + ".";
        return prefix + string.Join("+", parts);
    }

    [Theory]
    [InlineData("namespace Sample; public delegate void Changed();", "Sample.Changed")]
    [InlineData("namespace Sample; public class Widget<T> {}", "Sample.Widget`1")]
    [InlineData("namespace Sample; public class Outer<T> { public struct Inner<U> {} }", "Sample.Outer`1+Inner`1")]
    [InlineData("namespace Sample; public enum Position { First }", "Sample.Position")]
    public void RuntimeMetadataNamesIncludeAllTypeKinds(string code, string expected)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var model = CSharpCompilation.Create("Synthetic", [tree]).GetSemanticModel(tree);
        var names = tree.GetRoot().DescendantNodes()
            .Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            .Select(node => Assert.IsAssignableFrom<INamedTypeSymbol>(model.GetDeclaredSymbol(node)))
            .Select(RuntimeTypeName).ToArray();
        Assert.Contains(expected, names);
    }

    [Fact]
    public void GenericAndNonGenericMetadataNamesRemainDistinct()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace Sample; public class Widget<T> {}");
        var model = CSharpCompilation.Create("DistinctNames", [tree]).GetSemanticModel(tree);
        var declaration = Assert.Single(tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>());
        string name = RuntimeTypeName(Assert.IsAssignableFrom<INamedTypeSymbol>(model.GetDeclaredSymbol(declaration)));
        Assert.Equal("Sample.Widget`1", name);
        Assert.NotEqual("Sample.Widget", name);
    }

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
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path));
            var root = tree.GetRoot();
            var model = CSharpCompilation.Create("LinkedSourceAudit", [tree]).GetSemanticModel(tree);
            foreach (var type in root.DescendantNodes().Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
            {
                var symbol = Assert.IsAssignableFrom<INamedTypeSymbol>(model.GetDeclaredSymbol(type));
                string identity = RuntimeTypeName(symbol);
                Assert.DoesNotContain(identity, published);
            }
        }
    }
}
