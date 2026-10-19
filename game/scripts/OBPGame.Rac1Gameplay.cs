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

/// <summary>
/// Thin live host for the admitted R&C1 Goal 1 gameplay slice. Godot supplies
/// player input, explicitly host-policy wrench contact admission and presentation;
/// OBP.RAC1 retains recovered wrench facing and victim consequences.
/// </summary>
public partial class OBPGame
{
    private const double Rac1NativeTicksPerSecond = Rac1RatchetMovementController.UpdateHz;
    // Host presentation anchor only. Spatial admission dimensions live in the
    // explicitly non-retail Rac1WrenchHostContactPolicy.
    private const float Rac1WrenchHostRootHeight = 1.0f;

    private readonly Rac1WrenchCombatController _rac1Wrench = new();
    private Rac1MobyPersistenceSession _rac1MobyPersistence = new(levelId: 0);
    private Rac1BoltCrateSession _rac1BoltCrates = new();
    private Rac1BoltPickupSession _rac1BoltPickups = new();
    private Rac1BoltWallet _rac1BoltWallet = new();
    private Rac1MobyRuntimeSession _rac1MobyRuntime = new();
    private Rac1Class749HostileSession _rac1Hostiles = new();
    private Rac1RatchetNanotechSession _rac1Nanotech = new();
    private Rac1DamageTransportSession _rac1DamageTransport = new();
    private Rac1CampaignRuntimeSession _rac1CampaignSession = new(
        new Rac1CampaignState(),
        Rac1WeaponInventory.CreateOpeningVeldinWitness());
    private Rac1WeaponInventory _rac1Weapons => _rac1CampaignSession.Weapons;
    private Rac1BombGloveSession? _rac1BombGlove;
    private readonly List<RuntimeWorldScene.DynamicObjectNode> _rac1CrateNodes = [];
    private readonly Dictionary<int, Node3D> _rac1PickupNodes = [];
    private readonly Dictionary<long, Rac1HostedProjectile> _rac1Projectiles = [];
    private readonly List<RuntimeWorldScene.DynamicObjectNode> _rac1Class749PresentationNodes = [];
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _rac1LinkedTargetNodes = [];
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _rac1HostileNodes = [];
    private bool _rac1BombFireRequested;
    private Rac1BombGloveContactResolution? _rac1LastBombContactResolution;
    // Raw retail player-state word at +0x20a4. Its semantics remain intentionally unnamed.
    private int _rac1NativePlayerState20A4;
    private Rac1RatchetNanotechSnapshot? _rac1LastEnvironmentalDeathBoundary;
    private Vector3 _rac1LastEnvironmentalDeathPosition;
    private double _rac1LastEnvironmentalDeathContactSeparation = double.NaN;
    private string _rac1CombatStatus = "off";

    private void ResetRac1LevelGameplay(int levelId = 0)
    {
        ResetRac1WrenchHost();
        ResetRac1CombatRetry();
        ResetRac1Enemies();
        ResetRac1PickupPresentation();
        _rac1CrateNodes.Clear();
        _rac1PickupNodes.Clear();
        foreach (var projectile in _rac1Projectiles.Values)
            if (IsInstanceValid(projectile.Node)) projectile.Node.QueueFree();
        _rac1Projectiles.Clear();
        _rac1MobyPersistence = new Rac1MobyPersistenceSession(levelId);
        _rac1MobyRuntime = new Rac1MobyRuntimeSession();
        _rac1PlacedBolts = new Rac1PlacedBoltSession(_rac1MobyRuntime);
        _rac1BoltPickups = new Rac1BoltPickupSession();
        _rac1BoltCrates = new Rac1BoltCrateSession(
            _rac1MobyRuntime,
            _rac1MobyPersistence, _rac1BoltPickups);
        _rac1Hostiles = new Rac1Class749HostileSession(_rac1MobyRuntime, Rac1EffectRandom);
        _rac1Nanotech = new Rac1RatchetNanotechSession();
        _rac1DamageTransport = new Rac1DamageTransportSession();
        _rac1BombGlove = new Rac1BombGloveSession(_rac1Weapons);
        _rac1Class749PresentationNodes.Clear();
        _rac1LinkedTargetNodes.Clear();
        _rac1HostileNodes.Clear();
        _rac1BombFireRequested = false;
        _rac1LastBombContactResolution = null;
        ResetRac1BombPresentation();
        _rac1NativePlayerState20A4 = 0;
        _rac1LastEnvironmentalDeathBoundary = null;
        _rac1LastEnvironmentalDeathPosition = default;
        _rac1LastEnvironmentalDeathContactSeparation = double.NaN;
        _rac1CombatStatus = "off";
    }

    private void ConfigureRac1Gameplay(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        ResetRac1LevelGameplay(world.LevelId);
        if (world.Game != "rac1")
        {
            return;
        }

        _hudState.BeginSession(Rac1HudProjection.Capture(_rac1Nanotech, _rac1Weapons, _rac1BoltWallet));
        ConfigureRac1PickupPresentation(world);
        _rac1CombatStatus = "ready";
        foreach (var source in world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
        {
            if (!Rac1BoltCrate.IsCrateClass(source.NativeClassId))
            {
                continue;
            }

            Rac1BoltCrateAuthoredState? authored;
            try
            {
                authored = Rac1BoltCrate.ReadAuthored(source);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (authored is null)
            {
                continue;
            }

            var node = FindPresentedDynamic(result, source)
                ?? CreateRac1InvisibleRuntimeNode(result, source);
            _rac1BoltCrates.Register(source, node.State);
            _rac1CrateNodes.Add(node);
            AddRac1CrateCollider(node);
        }

        ConfigureRac1PlacedBolts(world, result);

        int authoredClass749 = 0;
        int rejectedHostiles = 0;
        foreach (var hostileSource in (world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
            .Where(source => source.NativeClassId == Rac1Class749Hostile.NativeClassId))
        {
            authoredClass749++;
            var node = FindPresentedDynamic(result, hostileSource)
                ?? CreateRac1InvisibleRuntimeNode(result, hostileSource);
            _rac1Class749PresentationNodes.Add(node);
            if (!Rac1Class749Hostile.IsRecoveredVeldinPlacement(world.LevelId, hostileSource))
            {
                continue;
            }

            try
            {
                _ = Rac1Class749Hostile.ReadAuthored(hostileSource)
                    ?? throw new InvalidDataException("Class-749 source failed its authored-state contract.");
                var probe = _rac1Hostiles.RegisterVeldinPlacement(hostileSource, node.State);
                _rac1HostileNodes.Add(hostileSource.InstanceIndex, node);
                if (probe.LinkedTargetInstanceIndex is int linkedInstanceIndex)
                    TrackRac1LinkedTarget(world, result, linkedInstanceIndex);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                rejectedHostiles++;
                GD.PrintErr($"[rac1-gameplay] class-749 i{hostileSource.InstanceIndex} unavailable: {ex.Message}");
            }
        }

        ConfigureRac1Enemies(world, result);
        GD.Print($"[rac1-gameplay] ready: {_rac1CrateNodes.Count} admitted retail crates, " +
                 $"{_rac1HostileNodes.Count} recovered Veldin class-749 placements from " +
                 $"{authoredClass749} authored placements ({rejectedHostiles} rejected)");
    }

    private void RefreshRac1HudState(params HudFeedbackDraft[] feedback)
    {
        if (_world?.Game != "rac1" || _rac1CombatStatus == "off") return;
        _hudState.Publish(Rac1HudProjection.Capture(_rac1Nanotech, _rac1Weapons, _rac1BoltWallet), feedback);
    }

    private void ArmRac1Gameplay(PlayerHost player)
    {
        if (_world?.Game != "rac1") return;
        player.Rac1PrimaryAttackRequested += OnRac1PrimaryAttackRequested;
        player.Rac1WeaponSelectionRequested += OnRac1WeaponSelectionRequested;
        player.Rac1WrenchPhysicsUpdated += OnRac1WrenchPhysicsUpdated;
        player.Rac1GameplayState = _rac1Nanotech.Probe();
        player.Rac1MovementEquipment = Rac1MovementCapabilities.FromInventory(_rac1Weapons);
        player.Rac1ActiveBackpack = player.Rac1MovementEquipment.RememberedBackpack(_rac1Weapons);
        player.ConfigureRac1TraversalTargets(_world.DynamicObjects ?? []);
    }

    private void TickRac1Gameplay(double delta)
    {
        if (_movementStateLoading || _world?.Game != "rac1" || _player is null || !IsInstanceValid(_player)) return;

        _rac1Nanotech.Actions.Update();
        _player.Rac1GameplayState = _rac1Nanotech.Probe();

        if (_rac1Nanotech.HasPendingRecoveredEnvironmentalRestart &&
            !TryCompleteRac1EnvironmentalRestart(automatic: true))
        {
            GD.PrintErr("[rac1-gameplay] automatic environmental restart pending without a valid recovered checkpoint session");
            return;
        }

        TickRac1VeldinEnvironmentalDeath();
        if (_rac1Nanotech.Probe().IsDead)
        {
            ResetRac1WrenchHost();
            ShowRac1CombatRetry();
            return;
        }

        TickRac1Swing(delta);
        TickRac1Hostiles(delta);
        TickRac1Pickups(delta);
    }

    private string GetRac1GameplayHudLine()
    {
        if (_world?.Game != "rac1" || _rac1CombatStatus == "off")
        {
            return string.Empty;
        }

        var hostileProbes = SnapshotRac1HostileProbes();
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
        var nanotech = _rac1Nanotech.Probe();
        string weapon = _rac1Weapons.Equipped == Rac1WeaponId.Wrench ? "Wrench" : "Bomb Glove";
        string restart = nanotech.HasRecoveredEnvironmentalRespawn
            ? "automatic recovered environmental respawn pending"
            : nanotech.IsDead
                ? "Retry level"
                : "environmental restart evidence: levels 0 and 2 only";
        return $"R&C1 combat: X wrench · E equipped item · 1 wrench · 2 Bomb Glove · {restart} · {_rac1CombatStatus}\n" +
            $"Nanotech: {nanotech.Nanotech}/{nanotech.RespawnNanotech} ({nanotech.LifeState})   " +
            $"Weapon: {weapon}   item-10 ammo: {_rac1Weapons.FirstRangedAmmo}   projectiles: {_rac1Projectiles.Count}\n" +
            $"Bolts: {_rac1BoltWallet.Balance}   pickups: {_rac1PickupNodes.Count}   {hostile}";
    }

    private Rac1GameplaySnapshot? GetRac1GameplaySnapshot()
    {
        if (_world?.Game != "rac1" || _rac1CombatStatus == "off") return null;
        var hostileProbes = SnapshotRac1HostileProbes();
        int activeHostiles = hostileProbes.Count(probe =>
            probe.EntityState.Presentation.Presence == RuntimeEntityPresence.Active);
        return new Rac1GameplaySnapshot(
            AdmittedCrates: _rac1CrateNodes.Count,
            DestroyedCrates: _rac1BoltCrates.DestroyedCrateCount,
            OutstandingPickups: _rac1BoltCrates.OutstandingPickupCount,
            CollectedBolts: _rac1BoltCrates.CollectedBolts,
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
            Nanotech: _rac1Nanotech.Probe().Nanotech,
            LifeState: _rac1Nanotech.Probe().LifeState,
            EquippedWeapon: _rac1Weapons.Equipped,
            BombGloveAmmo: _rac1Weapons.FirstRangedAmmo,
            HostedProjectiles: _rac1Projectiles.Count,
            Status: _rac1CombatStatus);
    }

    private sealed record Rac1GameplaySnapshot(
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

}
