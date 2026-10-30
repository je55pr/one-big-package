using Microsoft.CodeAnalysis;

namespace OBP.Tests;

/// <summary>
/// #368: source-owned wrench damage envelopes are routed through the world
/// lease; the player's native wrench and RNG remain borrowed authorities.
/// </summary>
public sealed class Rac1WrenchDamageWorldOwnershipTests
{
    private static string Script(string file) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", file));

    [Fact]
    public void WrenchDamageDispatchAndCrateConsequencesLeaveTheRoot()
    {
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (var name in new[]
        {
            "TryStrikeRac1Crate", "TryStrikeRac1Hostile", "CompleteRac1CrateDamage",
        })
            Assert.Empty(root.GetMembers(name));

        string module = Script("Rac1WorldModule.cs");
        string rootFactory = Script("OBPGame.cs");
        string host = Script("Rac1WrenchDamageHost.cs");
        string otherWeapons = Script("OBPGame.Rac1Weapons.cs");

        Assert.Contains("WrenchDamage = new(Context, Breakables, Enemies, Loot, wrenchDamage)", module, StringComparison.Ordinal);
        Assert.Contains("internal Rac1WrenchDamageHost WrenchDamage { get; }", module, StringComparison.Ordinal);
        Assert.Contains("if (retiring) WrenchDamage.Dispose()", module, StringComparison.Ordinal);
        Assert.Contains("new Rac1WrenchDamagePorts(() => _persistentPlayer.Wrench, SelectRac1CrateReward)", rootFactory, StringComparison.Ordinal);
        Assert.Contains("Rac1World.WrenchDamage.TryStrikeCrate(target, result)", rootFactory, StringComparison.Ordinal);
        Assert.Contains("Rac1World.WrenchDamage.TryStrikeHostile(target, result)", rootFactory, StringComparison.Ordinal);
        Assert.Contains("owner.WrenchDamage.CompleteCrateDamage(node, admission)", rootFactory, StringComparison.Ordinal);
        Assert.Contains("Rac1World.WrenchDamage.CompleteCrateDamage(target,", otherWeapons, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", host, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/Rac1WrenchDamageHost.cs.uid")));
    }

    [Fact]
    public void PlayerLifetimeWrenchAndRandomRemainBorrowed()
    {
        string host = Script("Rac1WrenchDamageHost.cs");
        string player = Script("OBPGame.Rac1Combat.cs");

        Assert.Contains("Func<Rac1WrenchCombatController> Wrench", host, StringComparison.Ordinal);
        Assert.Contains("Func<int, int> SelectCrateReward", host, StringComparison.Ordinal);
        Assert.Contains("ports.Wrench().AdmitGoal1RuntimeTarget(", host, StringComparison.Ordinal);
        Assert.Contains("ports.Wrench().ResolveHostAdmittedDamage(", host, StringComparison.Ordinal);
        Assert.Contains("selectedTotal: ports.SelectCrateReward(authored.RewardCentre)", host, StringComparison.Ordinal);
        Assert.Contains("private int SelectRac1CrateReward(int centre)", player, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Inclusive(", player, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1WrenchCombatController(", host, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1PickupRandom(", host, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeCrateAndHostileDispatchRetainEventOrdering()
    {
        string host = Script("Rac1WrenchDamageHost.cs");
        Assert.Contains("_breakables.TryTriggerExplosiveDamage(target, hit)", host, StringComparison.Ordinal);
        Assert.Contains("_context.DamageTransport.Publish(hit)", host, StringComparison.Ordinal);
        Assert.Contains("_context.MobyRuntime.CanDispatchDamage(damageEvent)", host, StringComparison.Ordinal);
        Assert.Contains("_context.MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(", host, StringComparison.Ordinal);
        Assert.Contains("CompleteCrateDamage(target, admission)", host, StringComparison.Ordinal);
        Assert.Contains("_context.BoltCrates.CompleteDamage(", host, StringComparison.Ordinal);
        Assert.Contains("_loot.SpawnRac1CrateDebris(target)", host, StringComparison.Ordinal);
        Assert.Contains("_loot.DisableRac1CrateCollider(target.Source.InstanceIndex)", host, StringComparison.Ordinal);
        Assert.Contains("target.ApplyState(broken.EntityState)", host, StringComparison.Ordinal);
        Assert.Contains("_loot.SpawnRac1BoltPickups(rewardOrigin, broken.Pickups)", host, StringComparison.Ordinal);
        Assert.Contains("_loot.SpawnRac1CrateSupply(target, rewardOrigin)", host, StringComparison.Ordinal);
        Assert.Contains("_enemies.ApplyRac1EnemyWeaponDamage(hostile, damageEvent)", host, StringComparison.Ordinal);

        int admit = host.IndexOf("if (!_context.MobyRuntime.CanDispatchDamage(damageEvent))", StringComparison.Ordinal);
        int publish = host.IndexOf("_context.DamageTransport.Publish(damageEvent)", StringComparison.Ordinal);
        int dispatch = host.IndexOf("DispatchDamage<Rac1BoltCrateDamageAdmission>", StringComparison.Ordinal);
        int complete = host.IndexOf("CompleteCrateDamage(target, admission)", StringComparison.Ordinal);
        Assert.True(admit < publish && publish < dispatch && dispatch < complete);
    }

    [Fact]
    public void RetiredWorldRejectsLateContactsAndRewards()
    {
        string host = Script("Rac1WrenchDamageHost.cs");
        string module = Script("Rac1WorldModule.cs");
        Assert.Contains("if (_disposed) return false;", host, StringComparison.Ordinal);
        Assert.Contains("if (_disposed) return;", host, StringComparison.Ordinal);
        Assert.Contains("public void Dispose() => _disposed = true;", host, StringComparison.Ordinal);
        Assert.True(module.IndexOf("if (retiring) WrenchDamage.Dispose()", StringComparison.Ordinal)
            < module.IndexOf("if (retiring) Enemies.Dispose()", StringComparison.Ordinal));
    }
}
