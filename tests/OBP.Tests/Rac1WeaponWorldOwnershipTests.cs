using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

/// <summary>
/// First #367/#368 ownership seam. Retail-backed weapon and detector scenarios
/// remain responsible for functional parity; these source-free checks prevent
/// the engine root from reclaiming world-gadget and guided-camera state.
/// </summary>
public sealed class Rac1WeaponWorldOwnershipTests
{
    private static string Script(string file) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", file));

    private static string Method(string file, string name) =>
        HostOwnershipAudit.Host.Value.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/" + file)
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == name).ToString();

    [Fact]
    public void DetectorRunsInRac1WorldLeaseInsteadOfRootPartial()
    {
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Rac1MetalDetector.cs")));
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/Rac1MetalDetectorHost.cs.uid")));
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Rac1MetalDetector.cs.uid")));
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string field in new[]
        {
            "_rac1MetalDetector", "_rac1DetectorEquipped", "_rac1DetectorHeldTicks",
            "_rac1DetectorCarry", "_rac1DetectorNearest", "_rac1MissileCamera"
        })
            Assert.Empty(root.GetMembers(field));

        var module = compilation.GetTypeByMetadataName("OBP.Game.Rac1WorldModule")!;
        Assert.Equal("Rac1WeaponHost",
            Assert.Single(module.GetMembers("Weapons").OfType<IPropertySymbol>()).Type.Name);
        Assert.Contains("Rac1MetalDetectorHost Detector", Script("Rac1WeaponHost.cs"), StringComparison.Ordinal);
        Assert.Contains("Weapons.ResetWorld()", Method("Rac1WorldModule.cs", "ResetBindings"), StringComparison.Ordinal);
        Assert.Contains("Weapons.Configure(world)", Method("Rac1WorldModule.cs", "ConfigureActors"), StringComparison.Ordinal);
        Assert.Contains("Weapons.Tick(delta)", Method("Rac1WorldModule.cs", "Tick"), StringComparison.Ordinal);
        Assert.Contains("Weapons.Dispose()", Method("Rac1WorldModule.cs", "Dispose"), StringComparison.Ordinal);
    }

    [Fact]
    public void DetectorBorrowsNativeProgressAndEmitsPhysicalBolts()
    {
        string host = Script("Rac1MetalDetectorHost.cs");
        string root = Script("OBPGame.cs");
        Assert.Contains("Rac1MetalDetectorSession", host, StringComparison.Ordinal);
        Assert.Contains("Rac1BuriedBoltProgress.Restore(ports.BuriedBolts().Snapshot())", host, StringComparison.Ordinal);
        Assert.Contains("_session.FindNearest(nativePlayer)", host, StringComparison.Ordinal);
        Assert.Contains("_session.TryExtract(_nearest, held, _heldTicks)", host, StringComparison.Ordinal);
        Assert.Contains("ports.SpawnBursts(", host, StringComparison.Ordinal);
        Assert.Contains("Rac1MetalDetectorSession.BoltsPerBurst", root, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Loot.SpawnRac1BoltPickups", root, StringComparison.Ordinal);
        Assert.Contains("if (_disposed) return false;", Method("Rac1MetalDetectorHost.cs", "Toggle"), StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", host, StringComparison.Ordinal);
    }

    [Fact]
    public void GuidedCameraRestoresFocusOnlyWhenItStillOwnsTheCamera()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var module = compilation.GetTypeByMetadataName("OBP.Game.Rac1WorldModule")!;
        Assert.Equal("Rac1WeaponHost",
            Assert.Single(module.GetMembers("Weapons").OfType<IPropertySymbol>()).Type.Name);
        Assert.Contains("Rac1GuidedWeaponCamera GuidedCamera", Script("Rac1WeaponHost.cs"), StringComparison.Ordinal);
        var begin = Method("Rac1GuidedWeaponCamera.cs", "Begin");
        var end = Method("Rac1GuidedWeaponCamera.cs", "End");
        Assert.Contains("player.Rac1RemoteControlActive = true", begin, StringComparison.Ordinal);
        Assert.Contains("bool wasCurrent = camera?.Current == true", end, StringComparison.Ordinal);
        Assert.Contains("player.Rac1RemoteControlActive = false", end, StringComparison.Ordinal);
        Assert.Contains("if (wasCurrent && GodotObject.IsInstanceValid(player.Camera))", end, StringComparison.Ordinal);
        Assert.Contains("GuidedCamera.End()", Method("Rac1WeaponHost.cs", "ResetWorld"), StringComparison.Ordinal);
        Assert.Contains("GuidedCamera.Dispose()", Method("Rac1WeaponHost.cs", "Dispose"), StringComparison.Ordinal);
        Assert.DoesNotContain("partial class OBPGame", Script("Rac1GuidedWeaponCamera.cs"), StringComparison.Ordinal);
    }
}
