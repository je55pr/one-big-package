using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

public sealed class FusionWeaponOwnershipArchitectureTests
{
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts", name));
    private static MethodDeclarationSyntax Method(string file, string name) => Assert.Single(
        CSharpSyntaxTree.ParseText(Read(file)).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>(),
        method => method.Identifier.ValueText == name && method.Parent?.Parent is BaseNamespaceDeclarationSyntax);

    [Fact]
    public void FusionPartialRetiresAndTheRootOnlyKeepsItsCompositionReference()
    {
        string path = Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.FusionWeapons.cs");
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".uid"));
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        Assert.DoesNotContain(root.GetMembers().OfType<IFieldSymbol>(), field => field.Name.StartsWith("_fusion", StringComparison.Ordinal));
        var owners = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.PlayerServicesComposition")!;
        var fields = owners.GetMembers().OfType<IFieldSymbol>().Where(field => field.Name.StartsWith("_fusion", StringComparison.Ordinal)).ToArray();
        var owner = Assert.Single(fields);
        Assert.Equal("FusionWeaponHost", owner.Type.Name);
        foreach (string file in new[] { "FusionWeaponHost.cs", "FusionWeaponSourceHost.cs", "FusionWeaponPresentationHost.cs", "FusionWeaponWorldActions.cs" })
            Assert.DoesNotContain("OBPGame", Read(file), StringComparison.Ordinal);
    }

    [Fact]
    public void SceneRetirementCannotDisposeSourcesOrResetAReplacementWorld()
    {
        var retire = Method("FusionWeaponHost.cs", "Retire");
        Assert.Contains("ReferenceEquals(_world, world)", retire.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Sources", retire.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("_modelPose", retire.ToString(), StringComparison.Ordinal);
        Assert.Contains("if (!_persistentSession) Session = null", retire.ToString(), StringComparison.Ordinal);
        var sourceReset = Method("FusionWeaponHost.cs", "ResetSources");
        Assert.Contains("ClearTransients()", sourceReset.ToString(), StringComparison.Ordinal);
        Assert.Contains("Sources.Reset()", sourceReset.ToString(), StringComparison.Ordinal);
        string resources = Read("GodotWorldResources.cs");
        Assert.Contains("public IDisposable? FusionWeapons", resources, StringComparison.Ordinal);
        Assert.Contains("FusionWeapons?.Dispose()", resources, StringComparison.Ordinal);
        string entry = Method("GodotWorldResources.cs", "Activate").ToString();
        int reset = entry.IndexOf("Rac1Gameplay.Configure()", StringComparison.Ordinal);
        int bind = entry.IndexOf("FusionWeapons = bindings.EquipmentWorld(Scene.Root)", StringComparison.Ordinal);
        int spawn = entry.IndexOf("bindings.SpawnPlayer(world)", StringComparison.Ordinal);
        Assert.InRange(bind, reset + 1, spawn - 1);
    }

    [Fact]
    public void ForeignAvatarsReceiveViewsAndNativeTransientsClearWithoutSourceAssets()
    {
        string attach = Method("PlayerAvatarPresentationHost.cs", "AttachWorldVisual").ToString();
        Assert.Contains("bindings.Weapons().AttachViews(view)", attach, StringComparison.Ordinal);
        int nativeViews = attach.IndexOf("UsePresentation.AttachViews(view)", StringComparison.Ordinal);
        Assert.True(nativeViews >= 0, "R&C1 view attachment is missing.");
        Assert.True(attach.IndexOf("bindings.Weapons().AttachViews(view)", StringComparison.Ordinal) > nativeViews);
        Assert.DoesNotContain("FusionWeapons.AttachViews", Method("Rac1WeaponUsePresentationHost.cs", "AttachViews").ToString(),
            StringComparison.Ordinal);
        string clear = Method("FusionWeaponHost.cs", "ClearTransients").ToString();
        Assert.Contains("player.ClearNativeTransients(freeImmediately)", clear, StringComparison.Ordinal);
        Assert.DoesNotContain("Sources.Assets", clear, StringComparison.Ordinal);
        Assert.Contains("ReleaseViews(freeImmediately: true)", Method("FusionWeaponHost.cs", "TickPreparation").ToString(),
            StringComparison.Ordinal);
        Assert.Contains("Rac1Gameplay = null", Method("GodotWorldResources.cs", "RetireGameplayBeforePlayer").ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinarySessionBindingPrecedesAnyExplicitFixtureAndDeathClearsOnce()
    {
        string configure = Method("FusionWeaponHost.cs", "Configure").ToString();
        int bind = configure.IndexOf("BindPersistent(persistent); return;", StringComparison.Ordinal);
        int isolate = configure.IndexOf("player.IsolateCampaign", StringComparison.Ordinal);
        Assert.InRange(bind, 0, isolate - 1);
        var death = Method("FusionWeaponHost.cs", "ClearOnDeath");
        Assert.Contains("if (_deathCleared) return", death.ToString(), StringComparison.Ordinal);
        Assert.Contains("ClearTransients()", death.ToString(), StringComparison.Ordinal);
        var clear = Method("FusionWeaponPresentationHost.cs", "Clear");
        foreach (string action in new[] { "StopAudio(freeImmediately)", "_projectiles.Clear()", "_effects.Clear()", "HideBeam()" })
            Assert.Contains(action, clear.ToString(), StringComparison.Ordinal);
    }
}
