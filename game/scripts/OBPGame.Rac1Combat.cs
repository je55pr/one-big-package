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

        _rac1Nanotech.Actions.ApplyWeaponUseAdmission(use);
        if (_player is not null)
            _player.Rac1GameplayState = _rac1Nanotech.Probe();
        _player?.NotifyRac1WrenchAttackAccepted();
        _rac1CombatStatus = $"wrench swing: sequence {use.NativePlayerSequenceId}";
        GD.Print("[rac1-gameplay] primary attack -> ordinary wrench swing");
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
        _ = delta;
        if (_rac1Nanotech.Actions.Probe().CurrentNativeState !=
            Rac1PlayerActionDomain.Wrench)
            return;

        // Retail hit-active timing is unresolved. Resolve exactly once through
        // the host contact policy on the next gameplay tick, then leave the
        // recovered wrench action. Do not reinterpret profile-row values 17/23
        // as a native contact window.
        ResolveRac1WrenchContact();
        _rac1Nanotech.Actions.EnterState(Rac1PlayerActionDomain.Neutral);
        if (_player is not null)
            _player.Rac1GameplayState = _rac1Nanotech.Probe();
    }

    private void ResolveRac1WrenchContact()
    {
        if (_player is null) return;

        var facing = _rac1Wrench.ResolveFirstSwingFacing(_player.Rac1CurrentYaw);
        Vector3 forward = new(-(float)facing.X, 0f, (float)facing.Y);
        if (forward.LengthSquared() <= 1e-5f) return;
        forward = forward.Normalized();
        Vector3 root = _player.GlobalPosition + Vector3.Up * Rac1WrenchHostRootHeight;

        var candidates = _rac1CrateNodes
            .Concat(_rac1HostileNodes.Values)
            .Where(node =>
                IsInstanceValid(node.Root) &&
                IsRac1GameplayActive(node) &&
                HasRac1HostContactGeometry(node))
            .ToArray();
        RuntimeDynamicObject? selected = _rac1Wrench.SelectNearestGoal1HostTarget(
            new Rac1WrenchHostPoint(root.X, root.Y, root.Z),
            new Rac1WrenchHostDirection(forward.X, forward.Y, forward.Z),
            candidates.Select(node => new Rac1WrenchHostCandidate(
                node.Source,
                new Rac1WrenchHostPoint(
                    node.Root.GlobalPosition.X,
                    node.Root.GlobalPosition.Y,
                    node.Root.GlobalPosition.Z))));
        if (selected is null)
        {
            _rac1CombatStatus = "wrench: no host-admitted contact";
            return;
        }

        var target = candidates.Single(node => ReferenceEquals(node.Source, selected));
        bool applied = target.Source.NativeClassId switch
        {
            Rac1BoltCrate.NativeClassId => TryStrikeRac1Crate(target),
            Rac1Class749Hostile.NativeClassId => TryStrikeRac1Hostile(target),
            _ => false,
        };
        if (!applied)
            _rac1CombatStatus = "wrench: admitted target produced no recovered consequence";
    }

    private bool TryStrikeRac1Crate(RuntimeWorldScene.DynamicObjectNode target)
    {
        var authored = Rac1BoltCrate.ReadAuthored(target.Source);
        if (authored?.RewardCentre != 10)
            return false;

        var contactTarget = _rac1Wrench.AdmitGoal1RuntimeTarget(target.Source)
            ?? throw new InvalidOperationException(
                "Admitted R&C1 Bolt Crate was rejected by the wrench target contract.");
        var damage = _rac1Wrench.ResolveHostAdmittedDamage(contactTarget);
        if (damage is null)
            return false;

        var targetKey = new Rac1MobyRuntimeKey(
            target.Source.NativeClassId,
            target.Source.InstanceIndex);
        var damageEvent = Rac1DamageRuntime.FromWrench(targetKey, damage);
        _rac1DamageTransport.Publish(damageEvent);
        var admission =
            _rac1MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(
                damageEvent);
        var broken = _rac1BoltCrates.CompleteDamage(
            admission,
            // Deterministic host RNG choice within the recovered range.
            // This does not claim the retail RNG selector.
            selectedTotal: authored.RewardCentre);

        Vector3 rewardOrigin = target.Root.GlobalPosition;
        target.ApplyState(broken.EntityState);
        SpawnRac1BoltPickups(rewardOrigin, broken.Pickups);
        _rac1CombatStatus =
            $"crate {target.Source.InstanceIndex} broke via host contact policy: +{broken.PhysicalValue} bolts emitted";
        GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
        return true;
    }

    private bool TryStrikeRac1Hostile(RuntimeWorldScene.DynamicObjectNode hostile)
    {
        var contactTarget = _rac1Wrench.AdmitGoal1RuntimeTarget(hostile.Source)
            ?? throw new InvalidOperationException(
                "R&C1 class-749 hostile was rejected by the wrench target contract.");
        var damage = _rac1Wrench.ResolveHostAdmittedDamage(contactTarget);
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
            $"hostile i{hostile.Source.InstanceIndex}: host contact -> health 0 -> terminal lifetime (native 0xfd/0xfe selector unresolved)";
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
