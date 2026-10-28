using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game;

internal sealed class Rac1GameplayHudPresenter(RuntimeWorld _world, Rac1WorldContext _context,
    Rac1WorldPresentation _presentation, Rac1GameplayHudPorts _ports, Func<bool> _retired)
{
    internal void Begin()
    {
        if (!_ports.Ordinary()) _ports.State.BeginSession(Rac1HudProjection.Capture(_ports.Health(), _ports.Weapons(), _ports.Wallet()));
    }

    internal void Refresh(params HudFeedbackDraft[] feedback)
    {
        if (_retired()) return;
        if (_ports.Ordinary() && _ports.HasPlayer()) { _ports.PublishPersistent(feedback); return; }
        if (_context.CombatStatus == "off") return;
        _ports.State.Publish(_ports.ProjectEquipment(Rac1HudProjection.Capture(_ports.Health(), _ports.Weapons(), _ports.Wallet(),
            _ports.Cargo(), _ports.Player()?.Rac1SwimState, _ports.Player()?.Rac1MovementEquipment.OxygenMask == true)), feedback);
    }

    private Rac1Class749HostProbe[] SnapshotHostiles() => _presentation.HostileNodes.Values
        .OrderBy(node => node.Source.InstanceIndex).Select(node => _context.Hostiles.Probe(node.Source)).ToArray();

    internal string Line()
    {
        if (_retired() || _world.Game != ObpSourceGame.Rac1 || _context.CombatStatus == "off")
        {
            return string.Empty;
        }

        var hostileProbes = SnapshotHostiles();
        int activeHostiles = hostileProbes.Count(probe =>
            probe.EntityState.Presentation.Presence == RuntimeEntityPresence.Active);
        string stateSummary = string.Join(
            ", ",
            hostileProbes
                .GroupBy(probe => probe.NativeState)
                .OrderBy(group => group.Key)
                .Select(group => $"s{group.Key}:{group.Count()}"));
        string hostile =
            $"class-749 population {hostileProbes.Length} ({activeHostiles} active)" +
            (stateSummary.Length > 0 ? $"; {stateSummary}" : string.Empty);
        var nanotech = _ports.Health().Probe();
        string weapon = Rac1WeaponProfiles.Name(_ports.Weapons().Equipped);
        string restart = nanotech.HasRecoveredEnvironmentalRespawn
            ? "automatic recovered environmental respawn pending"
            : nanotech.IsDead
                ? "Retry level"
                : "environmental restart evidence: levels 0 and 2 only";
        string swim = _ports.Player()?.Rac1SwimState is { } state
            ? $"   Water: {state.Water}   O2: {state.OxygenFraction:P0}" : string.Empty;
        return $"R&C1: Square wrench · Circle equipped item · Triangle quick select · Start testing options · {restart} · {_context.CombatStatus}\n" +
            $"Nanotech: {nanotech.Nanotech}/{nanotech.RespawnNanotech} ({nanotech.LifeState})   " +
            $"Weapon: {weapon}   item-10 ammo: {_ports.Weapons().FirstRangedAmmo}   projectiles: {_presentation.Projectiles.Count}\n" +
            $"Bolts: {_ports.Wallet().Balance}   pickups: {_presentation.PickupNodes.Count}   {hostile}{swim}{_ports.MetalDetectorText()}";
    }

    internal Rac1GameplaySnapshot? Snapshot()
    {
        if (_retired() || _world.Game != ObpSourceGame.Rac1 || _context.CombatStatus == "off") return null;
        var hostileProbes = SnapshotHostiles();
        int activeHostiles = hostileProbes.Count(probe =>
            probe.EntityState.Presentation.Presence == RuntimeEntityPresence.Active);
        return new Rac1GameplaySnapshot(
            AdmittedCrates: _presentation.CrateNodes.Count,
            DestroyedCrates: _context.BoltCrates.DestroyedCrateCount,
            OutstandingPickups: _context.BoltCrates.OutstandingPickupCount,
            CollectedBolts: _context.BoltCrates.CollectedBolts,
            HostileCount: hostileProbes.Length,
            ActiveHostiles: activeHostiles,
            LinkedHostiles: hostileProbes.Count(
                probe => probe.NativeState == Rac1Class749Hostile.LinkedObjectNativeState),
            IdleHostiles: hostileProbes.Count(
                probe => probe.NativeState == Rac1Class749Hostile.TargetSearchNativeState),
            PursuingHostiles: hostileProbes.Count(
                probe => probe.NativeState == Rac1Class749Hostile.TargetedNativeState),
            AttackingHostiles: hostileProbes.Count(
                probe => probe.NativeState == Rac1Class749Hostile.AttackNativeState),
            ReturningHostiles: hostileProbes.Count(
                probe => probe.NativeState == Rac1Class749Hostile.ReturnHomeNativeState),
            Nanotech: _ports.Health().Probe().Nanotech,
            LifeState: _ports.Health().Probe().LifeState,
            EquippedWeapon: _ports.Weapons().Equipped,
            BombGloveAmmo: _ports.Weapons().FirstRangedAmmo,
            HostedProjectiles: _presentation.Projectiles.Count,
            Status: _context.CombatStatus);
    }

}

internal sealed record Rac1GameplaySnapshot(
        int AdmittedCrates,
        int DestroyedCrates,
        int OutstandingPickups,
        int CollectedBolts,
        int HostileCount,
        int ActiveHostiles,
        int LinkedHostiles,
        int IdleHostiles,
        int PursuingHostiles,
        int AttackingHostiles,
        int ReturningHostiles,
        int Nanotech,
        Rac1RatchetLifeState LifeState,
        Rac1WeaponId EquippedWeapon,
        int BombGloveAmmo,
        int HostedProjectiles,
        string Status);
