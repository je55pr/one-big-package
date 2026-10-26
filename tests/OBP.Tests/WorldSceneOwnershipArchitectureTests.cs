using Microsoft.CodeAnalysis;

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
        string destinations = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Destinations.cs"));
        Assert.DoesNotContain("_ = EnterWorldAfterSharedPreparationAsync", destinations, StringComparison.Ordinal);
        Assert.Contains("WorldScenes.PrepareLater(", destinations, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested();", destinations, StringComparison.Ordinal);
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        Assert.Contains("await scenes.PendingPreparation;", root, StringComparison.Ordinal);
        Assert.Contains("_worldScenes?.Dispose();", root, StringComparison.Ordinal);
    }
}
