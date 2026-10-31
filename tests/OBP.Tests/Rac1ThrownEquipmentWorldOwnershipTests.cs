using OBP.Game;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using Microsoft.CodeAnalysis;

namespace OBP.Tests;

public sealed class Rac1ThrownEquipmentWorldOwnershipTests
{
    [Fact]
    public void SharedThrowClockKeepsTheOriginalSequenceAndGeneration()
    {
        using var host = new Rac1ThrownEquipmentAnimationHost();
        var input = new Rac1LocomotionAnimationInput(false, false, 0);

        Assert.Equal(input, host.Select(input, true, false, null, 0));
        host.Begin();
        var entered = host.Select(input, true, false, null, 0);
        Assert.True(host.Active);
        Assert.Equal(1, host.Generation);
        var traversal = Assert.IsType<Rac1MovementAnimationSelection>(entered.Traversal);
        Assert.Equal(0x23, traversal.NativeAction);
        Assert.Equal(Rac1BombProjectileSession.ThrowSequence, traversal.Sequence);
        Assert.Equal(1, traversal.EntryGeneration);
        Assert.Equal(Rac1BombProjectileSession.ThrowEntryFrame, traversal.EntryFrame);
        Assert.Equal(Rac1BombProjectileSession.ThrowBlendTicks, traversal.BlendTicks);

        var returning = host.Select(input, true, false, 44, Rac1BombProjectileSession.ThrowFinishTime);
        var ground = Assert.IsType<Rac1MovementAnimationSelection>(returning.GroundEntry);
        Assert.Equal(0, ground.NativeAction);
        Assert.Equal(0, ground.Sequence);
        Assert.Equal(1, ground.EntryGeneration);
        Assert.Equal(-1, ground.BlendTicks);
        Assert.False(host.Active);
        Assert.Equal(1, host.Generation);

        host.Begin();
        Assert.Equal(2, host.Generation);
        host.Stop();
        Assert.False(host.Active);
        Assert.Equal(2, host.Generation);
        host.Reset();
        Assert.Equal(0, host.Generation);
    }

    [Fact]
    public void DeathWrenchOrOtherTraversalCancelsWithoutReplacingInput()
    {
        var input = new Rac1LocomotionAnimationInput(false, false, 0);
        using var host = new Rac1ThrownEquipmentAnimationHost();

        host.Begin();
        Assert.Equal(input, host.Select(input, false, false, 0, 0));
        Assert.False(host.Active);

        host.Begin();
        Assert.Equal(input, host.Select(input, true, true, 0, 0));
        Assert.False(host.Active);

        host.Begin();
        var traversal = input with { Traversal = new(42, 12, 7) };
        Assert.Equal(traversal, host.Select(traversal, true, false, 0, 0));
        Assert.False(host.Active);
    }

    [Fact]
    public void DisposedWorldCannotRestartItsThrowClock()
    {
        var host = new Rac1ThrownEquipmentAnimationHost();
        var input = new Rac1LocomotionAnimationInput(false, false, 0);
        host.Begin();
        host.Dispose();
        Assert.False(host.Active);
        Assert.Equal(0, host.Generation);
        Assert.Equal(input, host.Select(input, true, false, 0, 0));
        Assert.Throws<ObjectDisposedException>(host.Begin);
    }

    [Fact]
    public void SharedThrowStateIsOwnedByWeaponLeaseNotRoot()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        Assert.Empty(root.GetMembers("_rac1BombThrowGeneration"));
        Assert.Empty(root.GetMembers("_rac1BombThrowActive"));
        var world = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/Rac1WeaponHost.cs"));
        Assert.Contains("ThrownAnimation = new()", world, StringComparison.Ordinal);
        Assert.Contains("ThrownAnimation.Reset()", world, StringComparison.Ordinal);
        Assert.Contains("ThrownAnimation.Dispose()", world, StringComparison.Ordinal);
        var projectiles = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/Rac1BombProjectileHost.cs"));
        var playerFrame = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.WorldEntry.cs"));
        Assert.Contains("_weapons.ThrownAnimation.Begin()", projectiles, StringComparison.Ordinal);
        Assert.Contains("Weapons.ThrownAnimation.Select(input", playerFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac1BombThrowGeneration", projectiles, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/Rac1ThrownEquipmentAnimationHost.cs.uid")));
    }
}
