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
    private const float Rac1PickupCollectRadius = 1.4f;
    // Host-only launch-vector/contact/expiry presentation. The native class-0x79
    // ballistic recurrence is recovered in OBP.RAC1, but host launch initialization,
    // contact geometry and a terminal lifetime consumer remain unresolved.
    private const float Rac1BombPresentationSpeed = 18f;
    private const float Rac1BombPresentationLifetime = 2f;
    private const float Rac1BombHostileContactRadius = 0.9f;
    // Host-side interpolation for the recovered class-749 state-6/state-8
    // destinations. Retail helper 0x002d54c8 proves destination-directed
    // locomotion, but its speed/turn tuning is not yet recovered. These values
    // make that recovered intent visible without promoting host tuning as retail.
    private const float Rac1Class749HostPresentationSpeed = 3.5f;
    private const float Rac1Class749HostPresentationTurnRate = 4.0f;

    private readonly Rac1WrenchCombatController _rac1Wrench = new();
    private Rac1MobyPersistenceSession _rac1MobyPersistence = new(levelId: 0);
    private Rac1BoltCrateSession _rac1BoltCrates = new();
    private Rac1MobyRuntimeSession _rac1MobyRuntime = new();
    private Rac1Class749HostileSession _rac1Hostiles = new();
    private Rac1RatchetNanotechSession _rac1Nanotech = new();
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
    private double _rac1BombTickAccumulator;
    // Raw retail player-state word at +0x20a4. Its semantics remain intentionally unnamed.
    private int _rac1NativePlayerState20A4;
    private Rac1RatchetNanotechSnapshot? _rac1LastEnvironmentalDeathBoundary;
    private Vector3 _rac1LastEnvironmentalDeathPosition;
    private double _rac1LastEnvironmentalDeathContactSeparation = double.NaN;
    private string _rac1CombatStatus = "off";

    private void ResetRac1LevelGameplay(int levelId = 0)
    {
        _rac1CrateNodes.Clear();
        _rac1PickupNodes.Clear();
        foreach (var projectile in _rac1Projectiles.Values)
            if (IsInstanceValid(projectile.Node)) projectile.Node.QueueFree();
        _rac1Projectiles.Clear();
        _rac1MobyPersistence = new Rac1MobyPersistenceSession(levelId);
        _rac1MobyRuntime = new Rac1MobyRuntimeSession();
        _rac1BoltCrates = new Rac1BoltCrateSession(
            _rac1MobyRuntime,
            _rac1MobyPersistence);
        _rac1Hostiles = new Rac1Class749HostileSession(_rac1MobyRuntime);
        _rac1Nanotech = new Rac1RatchetNanotechSession();
        _rac1BombGlove = new Rac1BombGloveSession(_rac1Weapons);
        _rac1Class749PresentationNodes.Clear();
        _rac1LinkedTargetNodes.Clear();
        _rac1HostileNodes.Clear();
        _rac1BombFireRequested = false;
        _rac1LastBombContactResolution = null;
        _rac1BombTickAccumulator = 0d;
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

        _hudState.BeginSession(Rac1HudProjection.Capture(_rac1Nanotech, _rac1Weapons));
        _rac1CombatStatus = "ready";
        foreach (var source in world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
        {
            if (source.NativeClassId != Rac1BoltCrate.NativeClassId)
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

            if (authored?.RewardCentre != 10)
            {
                continue;
            }

            var node = FindPresentedDynamic(result, source)
                ?? CreateRac1InvisibleRuntimeNode(result, source);
            _rac1BoltCrates.Register(source, node.State);
            _rac1CrateNodes.Add(node);
        }

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

        GD.Print($"[rac1-gameplay] ready: {_rac1CrateNodes.Count} admitted class-500 crates, " +
                 $"{_rac1HostileNodes.Count} recovered Veldin class-749 placements from " +
                 $"{authoredClass749} authored placements ({rejectedHostiles} rejected)");
    }

    private bool TryGetRac1Hostile(
        int instanceIndex,
        out RuntimeWorldScene.DynamicObjectNode? node,
        out Rac1Class749HostProbe? probe)
    {
        if (_world is { Game: "rac1", LevelId: Rac1Class749VeldinPopulation.LevelId } &&
            _rac1HostileNodes.TryGetValue(instanceIndex, out node))
        {
            probe = _rac1Hostiles.Probe(node.Source);
            return true;
        }

        node = null;
        probe = null;
        return false;
    }

    private Rac1Class749HostProbe[] SnapshotRac1HostileProbes() =>
        _rac1HostileNodes.Values
            .OrderBy(node => node.Source.InstanceIndex)
            .Select(node => _rac1Hostiles.Probe(node.Source))
            .ToArray();

    private bool IsRac1GameplayActive(RuntimeWorldScene.DynamicObjectNode node) =>
        _rac1MobyRuntime.Require(node.Source).IsActive;

    private static bool HasRac1HostContactGeometry(
        RuntimeWorldScene.DynamicObjectNode node) =>
        node.Root.GetChildren().Any(child => child is MeshInstance3D);

    private void SetRac1HostedPresence(
        RuntimeWorldScene.DynamicObjectNode node,
        RuntimeEntityPresence presence)
    {
        var runtime = _rac1MobyRuntime.Require(node.Source);
        _rac1MobyRuntime.SetTransform(
            runtime,
            RuntimeWorldScene.ToRuntimeTransform(node.Root.Transform));
        node.ApplyState(_rac1MobyRuntime.SetPresence(runtime, presence));
    }

    private void RefreshRac1HudState(params HudFeedbackDraft[] feedback)
    {
        if (_world?.Game != "rac1" || _rac1CombatStatus == "off") return;
        _hudState.Publish(Rac1HudProjection.Capture(_rac1Nanotech, _rac1Weapons), feedback);
    }

    private static RuntimeWorldScene.DynamicObjectNode? FindPresentedDynamic(
        RuntimeWorldScene.Result result,
        RuntimeDynamicObject source) =>
        result.DynamicObjectNodes?.FirstOrDefault(node =>
            node.Source.SourceGame == source.SourceGame &&
            node.Source.NativeClassId == source.NativeClassId &&
            node.Source.InstanceIndex == source.InstanceIndex);
    private void TrackRac1LinkedTarget(
        RuntimeWorld world,
        RuntimeWorldScene.Result result,
        int linkedInstanceIndex)
    {
        if (_rac1LinkedTargetNodes.ContainsKey(linkedInstanceIndex))
            return;

        var matches = (world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
            .Where(source => source.InstanceIndex == linkedInstanceIndex)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            GD.PrintErr(
                $"[rac1-gameplay] linked Moby i{linkedInstanceIndex} could not be resolved " +
                $"uniquely from authored runtime objects ({matches.Length} matches)");
            return;
        }

        RuntimeDynamicObject source = matches[0];
        var node = FindPresentedDynamic(result, source)
            ?? _rac1CrateNodes.FirstOrDefault(candidate =>
                candidate.Source.SourceGame == source.SourceGame &&
                candidate.Source.NativeClassId == source.NativeClassId &&
                candidate.Source.InstanceIndex == source.InstanceIndex)
            ?? CreateRac1InvisibleRuntimeNode(result, source);
        _rac1LinkedTargetNodes.Add(linkedInstanceIndex, node);
    }

    private static RuntimeWorldScene.DynamicObjectNode CreateRac1InvisibleRuntimeNode(
        RuntimeWorldScene.Result result,
        RuntimeDynamicObject source)
    {
        var root = new RuntimeWorldScene.RuntimeDynamicObjectRoot3D
        {
            Name = $"rac1_runtime_{source.NativeClassId}_{source.InstanceIndex}",
        };
        root.Configure(source);
        result.Root.AddChild(root);

        var node = new RuntimeWorldScene.DynamicObjectNode(source, root);
        node.ApplyState(RuntimeEntityState.FromAuthored(source));
        // Preserve authored identity/state for class registration, but do not
        // manufacture a visible/contactable host target when presentation is absent.
        root.Visible = false;
        return node;
    }

    private void ArmRac1Gameplay(DebugPlayer player)
    {
        if (_world?.Game != "rac1") return;
        player.Rac1PrimaryAttackRequested += OnRac1PrimaryAttackRequested;
        player.Rac1WeaponSelectionRequested += OnRac1WeaponSelectionRequested;
        player.Rac1RespawnRequested += OnRac1RespawnRequested;
        player.Rac1GameplayState = _rac1Nanotech.Probe();
    }

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

    private void OnRac1RespawnRequested()
    {
        // Explicit development control only. Ordinary Veldin play consumes the
        // recovered environmental restart automatically from TickRac1Gameplay.
        _ = TryCompleteRac1EnvironmentalRestart(automatic: false);
    }

    private bool TryCompleteRac1EnvironmentalRestart(bool automatic)
    {
        var death = _rac1Nanotech.Probe();
        if (_world is not { Game: "rac1" } world || _player is null ||
            _rac1CampaignSession.LevelCheckpoint is not { } checkpoint ||
            checkpoint.NativeLevelId != world.LevelId ||
            !death.HasRecoveredEnvironmentalRespawn)
            return false;

        // The recovered restart boundary resets Nanotech, placement, heading and
        // player motion. Godot does not infer a checkpoint trigger: level 0 reaches
        // this naturally through the recovered Veldin gate; level 2 is admitted only
        // when a separately identified checkpoint/death witness has been supplied.
        Rac1RestartPlacement restart = checkpoint.ResolveEnvironmentalRestart();
        var respawn = _rac1Nanotech.Respawn();
        _player.Rac1GameplayState = respawn;
        _player.ApplyRecoveredRac1Restart(restart.Placement);
        string source = automatic ? "automatic" : "development manual";
        _rac1CombatStatus =
            $"{source} environmental respawn L{world.LevelId} ({restart.Kind}): Nanotech {respawn.Nanotech}";
        RefreshRac1HudState();
        GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
        return true;
    }

    private void TickRac1Gameplay(double delta)
    {
        if (_world?.Game != "rac1" || _player is null || !IsInstanceValid(_player)) return;

        _rac1Nanotech.Actions.Update();
        _player.Rac1GameplayState = _rac1Nanotech.Probe();

        if (_rac1Nanotech.HasPendingRecoveredEnvironmentalRestart &&
            !TryCompleteRac1EnvironmentalRestart(automatic: true))
        {
            GD.PrintErr("[rac1-gameplay] automatic environmental restart pending without a valid recovered checkpoint session");
            return;
        }

        TickRac1VeldinEnvironmentalDeath();
        if (_rac1Nanotech.Probe().IsDead) return;

        TickRac1Swing(delta);
        TickRac1BombGlove(delta);
        TickRac1Projectiles(delta);
        TickRac1Hostiles(delta);
        TickRac1Pickups();
    }

    private void TickRac1VeldinEnvironmentalDeath()
    {
        if (_world is not { Game: "rac1", LevelId: 0, Environment: { } environment } ||
            _player is null || _rac1Nanotech.Probe().IsDead)
            return;

        double contactSeparation = MeasureRac1ContactSeparation();
        var dead = _rac1Nanotech.TryApplyVeldinEnvironmentalDeath(
            new Rac1VeldinEnvironmentalDeathFacts(
                NativeVerticalPosition: _player.GlobalPosition.Y,
                DeathHeight: environment.DeathHeight,
                ContactSeparation: contactSeparation,
                NativeSpecialPlayerState20A4: _rac1NativePlayerState20A4));
        if (dead is null) return;

        _rac1LastEnvironmentalDeathBoundary = dead;
        _rac1LastEnvironmentalDeathPosition = _player.GlobalPosition;
        _rac1LastEnvironmentalDeathContactSeparation = contactSeparation;
        _player.Rac1GameplayState = dead;
        _rac1CombatStatus = $"Veldin death plane: state 0x{dead.NativePlayerState:x2}, sequence {dead.NativeSequence} frame {dead.NativeSequenceFrame}; Nanotech {dead.Nanotech}";
        RefreshRac1HudState();
        GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
    }

    private double MeasureRac1ContactSeparation()
    {
        if (_player is null) return double.NaN;

        // DebugPlayer's CharacterBody origin is its feet. A downward ray therefore
        // supplies the live host contact-gap fact without moving the recovered threshold.
        Vector3 origin = _player.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(
            origin + Vector3.Up * 0.1f,
            origin + Vector3.Down * 1024f);
        query.Exclude = new global::Godot.Collections.Array<Rid> { _player.GetRid() };
        var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return double.PositiveInfinity;

        return Math.Max(0d, origin.Y - ((Vector3)hit["position"]).Y);
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
        var probe = _rac1MobyRuntime.DispatchDamage<Rac1Class749HostProbe>(
            damageEvent);
        // The native session admits both 0xfd and 0xfe terminal outcomes but does
        // not recover their selector. The live host uses 0xfd as an explicit
        // presentation choice so the proven terminal deactivation is observable.
        probe = _rac1Hostiles.ApplyTerminalStatus(
            hostile.Source, Rac1Class749Hostile.TerminalNativeStateFd);
        hostile.ApplyState(probe.EntityState);
        _rac1CombatStatus =
            $"hostile i{hostile.Source.InstanceIndex}: host contact -> health 0 -> terminal 0xfd";
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
    private void TickRac1BombGlove(double delta)
    {
        if (_rac1BombGlove is null || _rac1Nanotech.Probe().IsDead) return;
        _rac1BombTickAccumulator += Math.Max(0d, delta) * Rac1NativeTicksPerSecond;
        while (_rac1BombTickAccumulator >= 1d)
        {
            bool fire = _rac1BombFireRequested;
            var probe = _rac1BombGlove.Step(fire);
            if (fire) _rac1BombFireRequested = false;
            _rac1BombTickAccumulator -= 1d;
            if (probe.Shot is { } shot)
            {
                SpawnRac1BombProjectile(shot);
                PersistRac1CampaignState("Bomb Glove ammo consumption");
                _rac1CombatStatus =
                    $"Bomb Glove fired: ammo {shot.AmmoBefore}->{shot.AmmoAfter}; sequence {shot.Admission.NativePlayerSequenceId}";
                RefreshRac1HudState();
                GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
            }
            else if (fire && probe.UseAdmission is { Accepted: false } rejected)
            {
                _rac1CombatStatus = $"Bomb Glove use rejected: {rejected.Rejection}";
                GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
            }
        }
    }

    private void SpawnRac1BombProjectile(Rac1BombGloveShot shot)
    {
        if (_sceneResult is null || _player is null) return;

        // The corrected item-10 path has a recovered dedicated launch/step frame,
        // but its full launch-origin construction is not yet promoted. Keep only
        // the direct native-yaw direction mapping and retain the existing visible
        // muzzle offset as an explicit host presentation fallback.
        double nativeYaw = _player.Rac1CurrentYaw;
        Vector3 direction = PlayerAvatarFacing.NativeZUpPlanarDirectionToGodot(
            Math.Cos(nativeYaw),
            Math.Sin(nativeYaw));
        if (direction.LengthSquared() <= 1e-5f) return;
        direction = direction.Normalized();

        var node = new Node3D { Name = $"rac1_bomb_projectile_{shot.Projectile.ProjectileId}" };
        node.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.22f, Height = 0.44f },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0.48f, 0.08f),
            },
        });
        _sceneResult.Root.AddChild(node);
        node.GlobalPosition = _player.GlobalPosition + Vector3.Up * 1.1f + direction * 0.8f;
        _rac1Projectiles.Add(shot.Projectile.ProjectileId, new Rac1HostedProjectile(node, direction));
    }

    private void TickRac1Projectiles(double delta)
    {
        if (_rac1BombGlove is null || _rac1Projectiles.Count == 0) return;
        foreach (var pair in _rac1Projectiles.ToArray())
        {
            var projectile = pair.Value;
            if (!IsInstanceValid(projectile.Node))
            {
                _rac1Projectiles.Remove(pair.Key);
                continue;
            }

            Vector3 start = projectile.Node.GlobalPosition;
            Vector3 end = start + projectile.Direction * Rac1BombPresentationSpeed * (float)Math.Max(0d, delta);
            projectile.Node.GlobalPosition = end;
            projectile.Age += (float)Math.Max(0d, delta);

            bool impacted = false;
            Vector3 segment = end - start;
            var contacts = _rac1HostileNodes.Values
                .Where(hostile =>
                    IsInstanceValid(hostile.Root) &&
                    IsRac1GameplayActive(hostile) &&
                    HasRac1HostContactGeometry(hostile))
                .Select(hostile =>
                {
                    Vector3 target = hostile.Root.GlobalPosition + Vector3.Up * 0.5f;
                    float t = segment.LengthSquared() <= 1e-6f
                        ? 0f
                        : Mathf.Clamp((target - start).Dot(segment) / segment.LengthSquared(), 0f, 1f);
                    float separation = target.DistanceTo(start + segment * t);
                    return new { Hostile = hostile, T = t, Separation = separation };
                })
                .Where(candidate => candidate.Separation <= Rac1BombHostileContactRadius)
                .OrderBy(candidate => candidate.T)
                .ThenBy(candidate => candidate.Hostile.Source.InstanceIndex)
                .ToArray();

            var contactFacts = contacts
                .Select(contact =>
                {
                    var hostile = contact.Hostile;
                    var probe = _rac1Hostiles.Probe(hostile.Source);
                    return new Rac1MobyContactFacts(
                        hostile.Source,
                        probe.NativeState,
                        IsSourceMoby: false);
                })
                .ToArray();
            var contactResolution = _rac1BombGlove.ResolveGoal1ContactVolume(
                pair.Key,
                contactFacts);
            if (contactResolution.ProjectileCompleted)
                _rac1LastBombContactResolution = contactResolution;

            if (contactResolution.ProjectileCompleted)
            {
                int damageEventCount = contactResolution.DamageEvents.Count;
                int admittedConsequenceCount = 0;
                foreach (var damageEvent in contactResolution.DamageEvents)
                {
                    if (_rac1MobyRuntime.CanDispatchDamage(damageEvent))
                        admittedConsequenceCount++;
                }

                _rac1CombatStatus =
                    $"Bomb Glove contact: {damageEventCount} transported damage event(s), " +
                    $"{admittedConsequenceCount} recovered class-local consequence(s) admitted";
                GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
                impacted = true;
            }

            if (impacted || projectile.Age >= Rac1BombPresentationLifetime)
            {
                if (!impacted) _rac1BombGlove.CompleteProjectile(pair.Key);
                projectile.Node.QueueFree();
                _rac1Projectiles.Remove(pair.Key);
            }
        }
    }

    private void TickRac1Hostiles(double delta)
    {
        if (_player is null || _rac1Nanotech.Probe().IsDead)
        {
            return;
        }

        foreach (var hostile in _rac1HostileNodes.Values.OrderBy(node => node.Source.InstanceIndex))
        {
            if (!IsInstanceValid(hostile.Root) || !IsRac1GameplayActive(hostile))
                continue;

            var previous = _rac1Hostiles.Probe(hostile.Source);

            Vector3 toPlayer = _player.GlobalPosition - hostile.Root.GlobalPosition;
            double distance = toPlayer.Length();
            Vector3 planarToPlayer = new(toPlayer.X, 0f, toPlayer.Z);
            Vector3 planarForward = -hostile.Root.GlobalTransform.Basis.Z;
            planarForward.Y = 0f;
            double facingError = Math.PI;
            if (planarToPlayer.LengthSquared() > 1e-6f && planarForward.LengthSquared() > 1e-6f)
            {
                planarToPlayer = planarToPlayer.Normalized();
                planarForward = planarForward.Normalized();
                facingError = Math.Abs(planarForward.SignedAngleTo(planarToPlayer, Vector3.Up));
            }

            Vector3 hostilePosition = hostile.Root.GlobalPosition;
            Vector3 targetPosition = _player.GlobalPosition;
            bool linkedObjectTerminal = false;
            if (previous.LinkedTargetInstanceIndex is int linkedTargetInstanceIndex)
            {
                _rac1LinkedTargetNodes.TryGetValue(
                    linkedTargetInstanceIndex,
                    out var linkedNode);
                linkedObjectTerminal = linkedNode is null ||
                    !IsInstanceValid(linkedNode.Root) ||
                    linkedNode.State.Presentation.Presence != RuntimeEntityPresence.Active;
                if (!linkedObjectTerminal)
                    targetPosition = linkedNode!.Root.GlobalPosition;
            }

            var next = _rac1MobyRuntime.DispatchUpdate<Rac1Class749HostProbe>(
                hostile.Source,
                new Rac1Class749TargetFacts(
                    distance,
                    facingError,
                    new Rac1Class749WorldPoint(-hostilePosition.X, hostilePosition.Y, hostilePosition.Z),
                    StatusSentinel: null,
                    TargetPosition: new Rac1Class749WorldPoint(
                        -targetPosition.X,
                        targetPosition.Y,
                        targetPosition.Z),
                    LinkedObjectTerminal: linkedObjectTerminal));
            if (next.NativeState != previous.NativeState)
            {
                GD.Print(
                    $"[rac1-gameplay] hostile i{hostile.Source.InstanceIndex}: state {previous.NativeState} -> {next.NativeState}; " +
                    $"distance={distance:0.###} facing={facingError:0.###}");
            }

            foreach (var intent in next.HostIntents.OfType<Rac1Class749NavigationIntent>())
            {
                ApplyRac1Class749NavigationIntent(
                    hostile,
                    intent,
                    delta);
            }

            if (next.Attack is { } attack)
            {
                var beforeNanotech = _rac1Nanotech.Probe();
                var sourceKey = new Rac1MobyRuntimeKey(
                    hostile.Source.NativeClassId,
                    hostile.Source.InstanceIndex);
                var damageEvent = Rac1DamageRuntime.FromClass749Attack(
                    sourceKey,
                    attack);
                var nanotech = _rac1Nanotech.ApplyDamage(damageEvent);
                _player.Rac1GameplayState = nanotech;
                GD.Print($"[rac1-gameplay] hostile i{hostile.Source.InstanceIndex}: attack marker {attack.NativeMarker:0} damage {attack.NativeDamage:0.###}; Nanotech {nanotech.Nanotech}");
                _rac1CombatStatus = nanotech.IsDead
                    ? "Nanotech 0: combat-death restart/checkpoint semantics unresolved"
                    : $"class-749 hit: Nanotech {nanotech.Nanotech}/{nanotech.RespawnNanotech}";
                RefreshRac1HudState(Rac1HudProjection.DamageFeedback(beforeNanotech, nanotech));
            }
            if (_rac1Nanotech.Probe().IsDead)
            {
                break;
            }
        }
    }

    private void ApplyRac1Class749NavigationIntent(
        RuntimeWorldScene.DynamicObjectNode hostile,
        Rac1Class749NavigationIntent intent,
        double delta)
    {
        if (intent.Kind is not (
                Rac1Class749NavigationIntentKind.PursueRecoveredTarget or
                Rac1Class749NavigationIntentKind.ReturnHome))
            throw new NotSupportedException($"Unsupported class-749 navigation intent {intent.Kind}.");

        if (intent.Destination is not { } destination)
            throw new InvalidOperationException(
                $"Class-749 {intent.Kind} intent is missing its recovered destination.");

        float seconds = (float)Math.Clamp(
            delta,
            0d,
            1d / Rac1NativeTicksPerSecond);
        if (seconds <= 0f) return;

        // RuntimeWorldScene mirrors native X into Godot while retaining Y-up and Z.
        // The class-local producer has already supplied the recovered +0x180/+0x1d0
        // destination. Presentation consumes that descriptor and does not substitute
        // Ratchet's live transform.
        Vector3 target = new(
            -(float)destination.X,
            (float)destination.Y,
            (float)destination.Z);
        Vector3 current = hostile.Root.GlobalPosition;
        Vector3 offset = target - current;

        Vector3 planarDirection = new(offset.X, 0f, offset.Z);
        if (planarDirection.LengthSquared() > 1e-6f)
        {
            planarDirection = planarDirection.Normalized();
            Vector3 forward = -hostile.Root.GlobalTransform.Basis.Z;
            forward.Y = 0f;
            if (forward.LengthSquared() > 1e-6f)
            {
                forward = forward.Normalized();
                float turn = forward.SignedAngleTo(planarDirection, Vector3.Up);
                float maxTurn = Rac1Class749HostPresentationTurnRate * seconds;
                hostile.Root.RotateY(Mathf.Clamp(turn, -maxTurn, maxTurn));
            }
        }

        float distance = offset.Length();
        if (distance > 1e-6f)
        {
            float step = Math.Min(distance, Rac1Class749HostPresentationSpeed * seconds);
            hostile.Root.GlobalPosition = current + offset / distance * step;
        }

        var runtimeInstance = _rac1MobyRuntime.Require(hostile.Source);
        RuntimeEntityState synchronized = _rac1MobyRuntime.SetTransform(
            runtimeInstance,
            RuntimeWorldScene.ToRuntimeTransform(hostile.Root.Transform));
        hostile.ApplyState(synchronized);
    }

    private void SpawnRac1BoltPickups(Vector3 origin, IReadOnlyList<Rac1BoltPickup> pickups)
    {
        if (_sceneResult is null)
        {
            return;
        }

        for (int i = 0; i < pickups.Count; i++)
        {
            Rac1BoltPickup pickup = pickups[i];
            float radius = pickup.Value >= 5 ? 0.24f : 0.16f;
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0.82f, 0.18f),
            };
            var node = new Node3D { Name = $"rac1_bolt_{pickup.PickupId}_class{pickup.NativeClassId}" };
            node.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = radius, Height = radius * 2f },
                MaterialOverride = material,
            });
            _sceneResult.Root.AddChild(node);
            float angle = (float)(i * Math.Tau / Math.Max(1, pickups.Count));
            node.GlobalPosition = origin + new Vector3(Mathf.Cos(angle) * 0.7f, 0.65f, Mathf.Sin(angle) * 0.7f);
            _rac1PickupNodes.Add(pickup.PickupId, node);
        }
    }

    private void TickRac1Pickups()
    {
        if (_player is null || _rac1PickupNodes.Count == 0)
        {
            return;
        }
        var collected = new List<int>();
        foreach (var pair in _rac1PickupNodes)
        {
            if (!IsInstanceValid(pair.Value))
            {
                collected.Add(pair.Key);
                continue;
            }
            if (pair.Value.GlobalPosition.DistanceTo(_player.GlobalPosition) > Rac1PickupCollectRadius)
            {
                continue;
            }

            int value = _rac1BoltCrates.CollectPickup(pair.Key);
            pair.Value.QueueFree();
            collected.Add(pair.Key);
            _rac1CombatStatus = $"collected +{value} bolt{(value == 1 ? "" : "s")}";
            RefreshRac1HudState(Rac1HudProjection.CollectedBoltFeedback(value));
            GD.Print($"[rac1-gameplay] {_rac1CombatStatus}; total {_rac1BoltCrates.CollectedBolts}");
        }

        foreach (int pickupId in collected)
        {
            _rac1PickupNodes.Remove(pickupId);
        }
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
            ? "R recovered environmental respawn"
            : nanotech.IsDead
                ? "combat restart unresolved"
                : "environmental restart evidence: levels 0 and 2 only";
        return $"R&C1 combat: X attack · 1 wrench · 2 Bomb Glove · {restart} · {_rac1CombatStatus}\n" +
            $"Nanotech: {nanotech.Nanotech}/{nanotech.RespawnNanotech} ({nanotech.LifeState})   " +
            $"Weapon: {weapon}   item-10 ammo: {_rac1Weapons.FirstRangedAmmo}   projectiles: {_rac1Projectiles.Count}\n" +
            $"Bolts collected: {_rac1BoltCrates.CollectedBolts}   pickups: {_rac1PickupNodes.Count}   {hostile}";
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

    private sealed class Rac1HostedProjectile(Node3D node, Vector3 direction)
    {
        public Node3D Node { get; } = node;
        public Vector3 Direction { get; } = direction;
        public float Age { get; set; }
    }
}
