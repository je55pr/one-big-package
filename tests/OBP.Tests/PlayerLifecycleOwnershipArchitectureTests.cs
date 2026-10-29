using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

public sealed class PlayerLifecycleOwnershipArchitectureTests
{
    private static string Read(string file) => File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts", file));

    [Fact]
    public void HealthPartialRetiresAndTheRootOnlyComposesTheOwner()
    {
        string path = Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.PlayerHealth.cs");
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".uid"));
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string name in new[] { "_playerDamageActors", "_playerDamageWorld", "_playerDamageLease", "_playerServiceTick" })
            Assert.Empty(root.GetMembers(name));
        Assert.Single(root.GetMembers().OfType<IFieldSymbol>(), field => field.Type.Name == "GodotPlayerLifecycleHost");
        foreach (string file in new[] { "PlayerLifecycleBridge.cs", "GodotPlayerLifecycleHost.cs" })
            Assert.DoesNotContain("OBPGame", Read(file), StringComparison.Ordinal);
        Assert.DoesNotContain("using Godot", Read("PlayerLifecycleBridge.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void SceneResourcesOwnTheLeaseAndTheOwnerChecksCapturedCallbacks()
    {
        string resources = Read("GodotWorldResources.cs");
        Assert.Contains("public IDisposable? PlayerLife", resources, StringComparison.Ordinal);
        Assert.Contains("PlayerLife?.Dispose()", resources, StringComparison.Ordinal);
        string entry = Read("OBPGame.WorldEntry.cs");
        Assert.Contains("entry.Resources.PlayerLife = PlayerLife.BindWorld", entry, StringComparison.Ordinal);
        var host = CSharpSyntaxTree.ParseText(Read("GodotPlayerLifecycleHost.cs")).GetRoot();
        var bind = Assert.Single(host.DescendantNodes().OfType<MethodDeclarationSyntax>(), method => method.Identifier.ValueText == "BindPlayer");
        Assert.Contains("ReferenceEquals(_world, world)", bind.ToString(), StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_player, player)", bind.ToString(), StringComparison.Ordinal);
        var retry = Assert.Single(host.DescendantNodes().OfType<MethodDeclarationSyntax>(), method => method.Identifier.ValueText == "ShowRetry");
        Assert.Contains("ReferenceEquals(_world, world)", retry.ToString(), StringComparison.Ordinal);
        Assert.Contains("player.PlayerServicesPhysicsUpdated -= _tick", Read("GodotPlayerLifecycleHost.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryRespawnDelegatesWorldEntryWithoutAcquiringNativeParityRestartPolicy()
    {
        string bridge = Read("PlayerLifecycleBridge.cs");
        foreach (string native in new[] { "ApplyRecoveredRac1Restart", "ResolveEnvironmentalRestart", "Rac1VeldinEnvironmentalDeathFacts", "WorldContentIdentity", "ObpDestination" })
            Assert.DoesNotContain(native, bridge, StringComparison.Ordinal);
        Assert.Contains("ports.EnterWorld()", bridge, StringComparison.Ordinal);
        Assert.Contains("session.Restore(before)", bridge, StringComparison.Ordinal);
        Assert.Contains("ResolveEnvironmentalRestart", Read("OBPGame.Rac1Lifecycle.cs"), StringComparison.Ordinal);
    }
}
