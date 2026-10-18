using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OneBigPackage;

public partial class OBPGame
{
    private void OnRac1PrimaryAttackRequested()
    {
        if (_rac1Nanotech.Probe().IsDead) return;
        if (_rac1Weapons.Equipped == Rac1WeaponId.FirstRanged)
        {
            if (_player?.Rac1WrenchFrame.Active == true || _rac1WrenchThrow.Active)
                return;
            _rac1BombFireRequested = true;
            _rac1CombatStatus = "Bomb Glove fire requested";
            return;
        }

        var use = _rac1Wrench.AdmitOrdinaryUse(_rac1Weapons.Equipped == Rac1WeaponId.Wrench);
        if (!use.Accepted)
        {
            _rac1CombatStatus = $"wrench use rejected: {use.Rejection}";
            return;
        }

        _player?.NotifyRac1WrenchAttackAccepted();
        _rac1CombatStatus = "wrench input buffered";
    }

    private void OnRac1WeaponSelectionRequested(Rac1WeaponId weapon)
    {
        if (_rac1Weapons.TryEquip(weapon))
        {
            _rac1CombatStatus = weapon == Rac1WeaponId.Wrench ? "equipped wrench" : "equipped Bomb Glove item 10";
            RefreshRac1HudState();
            RefreshRac1WrenchPresentationVisibility();
            GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
        }
    }

    private void TickRac1Swing(double delta)
    {
        // Wrench motion, authored hit gates and presentation share the player's
        // fixed 60-Hz callback. Render updates cannot apply additional hits.
        _ = delta;
    }

    private bool TryStrikeRac1Crate(RuntimeWorldScene.DynamicObjectNode target, Rac1WrenchDamageResult? nativeDamage = null)
    {
        var authored = Rac1BoltCrate.ReadAuthored(target.Source);
        if (authored is null)
            return false;

        var contactTarget = _rac1Wrench.AdmitGoal1RuntimeTarget(target.Source)
            ?? throw new InvalidOperationException(
                "Admitted R&C1 Bolt Crate was rejected by the wrench target contract.");
        var damage = nativeDamage ?? _rac1Wrench.ResolveHostAdmittedDamage(contactTarget);
        if (damage is null)
            return false;

        var targetKey = new Rac1MobyRuntimeKey(
            target.Source.NativeClassId,
            target.Source.InstanceIndex);
        var damageEvent = Rac1DamageRuntime.FromWrench(targetKey, damage);
        if (!_rac1MobyRuntime.CanDispatchDamage(damageEvent)) return false;
        _rac1DamageTransport.Publish(damageEvent);
        var admission =
            _rac1MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(
                damageEvent);
        CompleteRac1CrateDamage(target, admission);
        return true;
    }

    private void CompleteRac1CrateDamage(RuntimeWorldScene.DynamicObjectNode target, Rac1BoltCrateDamageAdmission admission)
    {
        var authored = Rac1BoltCrate.ReadAuthored(target.Source)!;
        var broken = _rac1BoltCrates.CompleteDamage(
            admission,
            selectedTotal: SelectRac1CrateReward(authored.RewardCentre));

        Vector3 rewardOrigin = target.Root.GlobalPosition;
        PlayRac1PickupSound(Rac1CrateAudio.BreakSoundClass(target.Source.NativeClassId),
            Rac1CrateAudio.BreakLocalSound, rewardOrigin);
        SpawnRac1CrateDebris(target);
        DisableRac1CrateCollider(target.Source.InstanceIndex);
        target.ApplyState(broken.EntityState);
        SpawnRac1BoltPickups(rewardOrigin, broken.Pickups);
        SpawnRac1CrateSupply(target, rewardOrigin);
        _rac1CombatStatus =
            $"crate {target.Source.InstanceIndex} broke via host contact policy: +{broken.PhysicalValue} bolts emitted";
        GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
    }

    private int SelectRac1CrateReward(int centre)
    {
        var range = Rac1BoltCrate.RewardRange(centre);
        return range.Maximum == 0 ? 0 : Rac1EffectRandom.Inclusive(range.Minimum, range.Maximum);
    }

    private bool TryStrikeRac1Hostile(RuntimeWorldScene.DynamicObjectNode hostile, Rac1WrenchDamageResult? nativeDamage = null)
    {
        var contactTarget = _rac1Wrench.AdmitGoal1RuntimeTarget(hostile.Source)
            ?? throw new InvalidOperationException(
                "R&C1 class-749 hostile was rejected by the wrench target contract.");
        var damage = nativeDamage ?? _rac1Wrench.ResolveHostAdmittedDamage(contactTarget);
        if (damage is null)
            return false;

        var targetKey = new Rac1MobyRuntimeKey(
            hostile.Source.NativeClassId,
            hostile.Source.InstanceIndex);
        var damageEvent = Rac1DamageRuntime.FromWrench(targetKey, damage);
        _rac1DamageTransport.Publish(damageEvent);
        var probe = _rac1MobyRuntime.DispatchDamage<Rac1Class749HostProbe>(
            damageEvent);
        // Retail proves state 12 reaches the common terminalizer, but OBP does
        // not reconstruct the pool-side selector that chooses 0xfd versus 0xfe.
        // Complete the recovered lifetime transition without inventing that byte.
        probe = _rac1Hostiles.CompleteRecoveredDamageReaction(hostile.Source);
        hostile.ApplyState(probe.EntityState);
        _rac1CombatStatus =
            $"hostile i{hostile.Source.InstanceIndex}: wrench damage {damage.NativeDamage} -> health {probe.Health} -> terminal lifetime (native 0xfd/0xfe selector unresolved)";
        GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
        return true;
    }

    private static bool Rac1WrenchHostPolicyAdmits(
        Vector3 root,
        Vector3 forward,
        Vector3 targetCenter) =>
        Rac1WrenchHostContactPolicy.Admits(
            new Rac1WrenchHostPoint(root.X, root.Y, root.Z),
            new Rac1WrenchHostDirection(forward.X, forward.Y, forward.Z),
            new Rac1WrenchHostPoint(targetCenter.X, targetCenter.Y, targetCenter.Z));
}
