using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

public sealed class WorldSceneOwnershipArchitectureTests
{
    [Fact]
    public void WorldFactsAreReadOnlyViewsOfTheSceneOwnerRatherThanSharedMutableFields()
    {
        var host = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string name in new[] { "_world", "_sceneResult", "_worldHost", "_activeDestination",
            "_currentDestinationId", "_worldPlayerEntry", "_worldEntrySnapReach" })
        {
            var member = Assert.Single(host.GetMembers(name));
            var property = Assert.IsAssignableFrom<IPropertySymbol>(member);
            Assert.Null(property.SetMethod);
        }
        Assert.Single(host.GetMembers("_worldScenes").OfType<IFieldSymbol>());
        Assert.Empty(host.GetTypeMembers("SceneLease"));
    }

    [Fact]
    public void DeferredEntryHasAnOwnedCompletionAndShutdownAwaitsIt()
    {
        string destinations = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/ApplicationNavigationCoordinator.cs"));
        Assert.DoesNotContain("_ = EnterWorldAfterSharedPreparationAsync", destinations, StringComparison.Ordinal);
        Assert.Contains("_ports.Scenes().PrepareLater(", destinations, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested();", destinations, StringComparison.Ordinal);
        Assert.Contains("generation != _intentGeneration", destinations, StringComparison.Ordinal);
        Assert.Contains("CancelNavigation();", destinations, StringComparison.Ordinal);
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        Assert.Contains("await scenes.PendingPreparation;", root, StringComparison.Ordinal);
        Assert.Contains("_worldScenes?.Dispose();", root, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedDestinationOrLabEntryKeepsBrowserCallbacksAliveForRetry()
    {
        string scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
        string source = File.ReadAllText(Path.Combine(scripts, "ApplicationNavigationCoordinator.cs"));
        var methods = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().ToArray();

        foreach (string name in new[] { "Choose", "EnterLab" })
        {
            var method = Assert.Single(methods, method => method.Identifier.ValueText == name);
            var calls = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Select(call => call.Expression.ToString()).ToArray();
            Assert.Contains("_ports.CancelPreparation", calls);
            Assert.DoesNotContain("CancelNavigation", calls);
        }

        Assert.Contains("if (generation == _intentGeneration) Choose(destination);", source, StringComparison.Ordinal);
        Assert.Contains("generation != _intentGeneration", source, StringComparison.Ordinal);
        Assert.Contains("++_intentGeneration;", source, StringComparison.Ordinal);
        foreach (string retired in new[] { "OBPGame.Navigation.cs", "OBPGame.Destinations.cs", "OBPGame.FusionLab.cs" })
            Assert.False(File.Exists(Path.Combine(scripts, retired)), retired);
    }
}
