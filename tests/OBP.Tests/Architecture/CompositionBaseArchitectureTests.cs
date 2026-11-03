using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

/// <summary>
/// Composition modules bind owned components to narrow ports. They may read the application
/// through <c>CompositionBase</c>, so who can derive from it and how large it may grow are pinned.
/// </summary>
public sealed class CompositionBaseArchitectureTests
{
    // Shared views may only shrink: a member one module needs belongs in that module.
    private const int SharedMemberCeiling = 65;

    private static readonly string[] AllowedDerived =
    [
        "ApplicationDiagnostics", "CaptureComposition", "Rac1WorldComposition", "Rac2WorldComposition",
    ];

    private static readonly string[] CompositionFiles =
    [
        "game/scripts/OBPGame.cs", "game/scripts/ApplicationComponents.cs", "game/scripts/CompositionBase.cs",
        "game/diagnostics/ApplicationDiagnostics.cs", "game/scripts/CaptureComposition.cs",
        "game/scripts/Rac1WorldComposition.cs", "game/scripts/Rac2WorldComposition.cs",
    ];

    private static IEnumerable<(string Path, SyntaxNode Root)> GameSources() =>
        new[] { "game/scripts", "game/diagnostics" }
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, directory), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Replace('\\', '/').Contains("/obj/") && !path.Replace('\\', '/').Contains("/bin/"))
            .Select(path => (Path.GetRelativePath(RepoPaths.Root, path).Replace('\\', '/'),
                CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot()));

    [Fact]
    public void OnlyTheNamedCompositionModulesDeriveFromTheSharedBase()
    {
        var derived = GameSources().SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(type => type.BaseList?.Types.Any(b => b.Type.ToString() == "CompositionBase") == true))
            .Select(type => type.Identifier.ValueText).Order().ToArray();
        Assert.Equal(AllowedDerived, derived);
    }

    [Fact]
    public void GameplayFixturesAndScenariosNeverReceiveTheApplicationComponents()
    {
        foreach (var (path, root) in GameSources().Where(source => !CompositionFiles.Contains(source.Path)))
        {
            var offenders = root.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(name => name.Identifier.ValueText is "ApplicationComponents" or "CompositionBase");
            Assert.True(!offenders.Any(), $"{path} must receive narrow ports, not the application components.");
        }
    }

    [Fact]
    public void TheSharedSurfaceOnlyShrinks()
    {
        var path = Path.Combine(RepoPaths.Root, "game/scripts/CompositionBase.cs");
        var type = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(candidate => candidate.Identifier.ValueText == "CompositionBase");
        int members = type.Members.Count(member => member.Modifiers.Any(modifier =>
            modifier.IsKind(SyntaxKind.ProtectedKeyword) || modifier.IsKind(SyntaxKind.InternalKeyword) || modifier.IsKind(SyntaxKind.PublicKeyword)));
        Assert.True(members <= SharedMemberCeiling,
            $"CompositionBase has {members} shared members (ceiling {SharedMemberCeiling}); move single-module views into that module.");
    }
}
