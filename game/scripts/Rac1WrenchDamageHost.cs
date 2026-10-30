using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Borrowed player-lifetime wrench authority and reward random stream.
/// A world lease dispatches contacts but never owns the player's controller.
/// </summary>
internal sealed record Rac1WrenchDamagePorts(
    Func<Rac1WrenchCombatController> Wrench,
    Func<int, int> SelectCrateReward);

/// <summary>
/// Source-owned wrench damage envelopes are dispatched to R&C1 world actor,
/// crate and loot owners. Does not own a second combat or reward session.
/// </summary>
internal sealed class Rac1WrenchDamageHost(
    Rac1WorldContext _context, Rac1BreakableHost _breakables,
    Rac1EnemyHost _enemies, Rac1LootPresentation _loot,
    Rac1WrenchDamagePorts ports) : IDisposable
{
    private bool _disposed;

    internal bool TryStrikeCrate(RuntimeWorldScene.DynamicObjectNode target, Rac1WrenchDamageResult? nativeDamage = null)
    {
        if (_disposed) return false;
        if (target.Source.NativeClassId == Rac1ExplosiveCrateSession.ClassId)
        {
            var explosive = ports.Wrench().AdmitGoal1RuntimeTarget(target.Source);
            if (explosive is null) return false;
            var strike = nativeDamage ?? ports.Wrench().ResolveHostAdmittedDamage(explosive.Value);
            if (strike is null) return false;
            var key = new Rac1MobyRuntimeKey(target.Source.NativeClassId, target.Source.InstanceIndex);
            var hit = Rac1DamageRuntime.FromWrench(key, strike);
            if (!_breakables.TryTriggerExplosiveDamage(target, hit)) return false;
            _context.DamageTransport.Publish(hit);
            return true;
        }
        var authored = Rac1BoltCrate.ReadAuthored(target.Source);
        if (authored is null)
            return false;

        var contactTarget = ports.Wrench().AdmitGoal1RuntimeTarget(target.Source)
            ?? throw new InvalidOperationException(
                "Admitted R&C1 Bolt Crate was rejected by the wrench target contract.");
        var damage = nativeDamage ?? ports.Wrench().ResolveHostAdmittedDamage(contactTarget);
        if (damage is null)
            return false;

        var targetKey = new Rac1MobyRuntimeKey(
            target.Source.NativeClassId,
            target.Source.InstanceIndex);
        var damageEvent = Rac1DamageRuntime.FromWrench(targetKey, damage);
        if (!_context.MobyRuntime.CanDispatchDamage(damageEvent)) return false;
        _context.DamageTransport.Publish(damageEvent);
        var admission =
            _context.MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(
                damageEvent);
        CompleteCrateDamage(target, admission);
        return true;
    }

    internal void CompleteCrateDamage(RuntimeWorldScene.DynamicObjectNode target, Rac1BoltCrateDamageAdmission admission)
    {
        if (_disposed) return;
        var authored = Rac1BoltCrate.ReadAuthored(target.Source)!;
        var broken = _context.BoltCrates.CompleteDamage(
            admission,
            selectedTotal: ports.SelectCrateReward(authored.RewardCentre));

        Vector3 rewardOrigin = target.Root.GlobalPosition;
        _loot.PlayRac1PickupSound(Rac1CrateAudio.BreakSoundClass(target.Source.NativeClassId),
            Rac1CrateAudio.BreakLocalSound, rewardOrigin);
        _loot.SpawnRac1CrateDebris(target);
        _loot.DisableRac1CrateCollider(target.Source.InstanceIndex);
        target.ApplyState(broken.EntityState);
        _loot.SpawnRac1BoltPickups(rewardOrigin, broken.Pickups);
        _loot.SpawnRac1CrateSupply(target, rewardOrigin);
        _context.CombatStatus =
            $"crate {target.Source.InstanceIndex} broke via host contact policy: +{broken.PhysicalValue} bolts emitted";
        GD.Print($"[rac1-gameplay] {_context.CombatStatus}");
    }

    internal bool TryStrikeHostile(RuntimeWorldScene.DynamicObjectNode hostile, Rac1WrenchDamageResult? nativeDamage = null)
    {
        if (_disposed) return false;
        var contactTarget = ports.Wrench().AdmitGoal1RuntimeTarget(hostile.Source)
            ?? throw new InvalidOperationException(
                "R&C1 class-749 hostile was rejected by the wrench target contract.");
        var damage = nativeDamage ?? ports.Wrench().ResolveHostAdmittedDamage(contactTarget);
        if (damage is null)
            return false;

        var targetKey = new Rac1MobyRuntimeKey(
            hostile.Source.NativeClassId,
            hostile.Source.InstanceIndex);
        var damageEvent = Rac1DamageRuntime.FromWrench(targetKey, damage);
        if (!_enemies.ApplyRac1EnemyWeaponDamage(hostile, damageEvent)) return false;
        _context.DamageTransport.Publish(damageEvent);
        return true;
    }

    public void Dispose() => _disposed = true;
}
