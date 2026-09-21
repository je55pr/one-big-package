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
/// player input, contact/proximity facts and presentation; OBP.RAC1 remains the
/// authority for wrench contact, crate rewards and class-749 native state.
/// </summary>
public partial class OBPGame
{
    private const int Rac1WitnessHostileInstance = 149;
    private const double Rac1NativeTicksPerSecond = Rac1RatchetMovementController.UpdateHz;
    private const float Rac1WrenchReach = 2.35f;
    private const float Rac1DirectContactRadius = 1.25f;
    private const float Rac1CrateOriginPadding = 1.0f;
    private const float Rac1PickupCollectRadius = 1.4f;
    // Host-only launch-vector/contact/expiry presentation. The native class-0x79
    // ballistic recurrence is recovered in OBP.RAC1, but host launch initialization,
    // contact geometry and a terminal lifetime consumer remain unresolved.
    private const float Rac1BombPresentationSpeed = 18f;
    private const float Rac1BombPresentationLifetime = 2f;
    private const float Rac1BombHostileContactRadius = 0.9f;

    private readonly Rac1WrenchCombatController _rac1Wrench = new();
    private Rac1BoltCrateSession _rac1BoltCrates = new();
    private Rac1Class749HostileSession _rac1Hostiles = new();
    private Rac1RatchetNanotechSession _rac1Nanotech = new();
    private Rac1CampaignRuntimeSession _rac1CampaignSession = new(
        new Rac1CampaignState(),
        new Rac1WeaponInventory(ownsFirstRanged: true, firstRangedAmmo: 6));
    private Rac1WeaponInventory _rac1Weapons => _rac1CampaignSession.Weapons;
    private Rac1BombGloveSession? _rac1BombGlove;
    private readonly List<RuntimeWorldScene.DynamicObjectNode> _rac1CrateNodes = [];
    private readonly Dictionary<int, Node3D> _rac1PickupNodes = [];
    private readonly Dictionary<long, Rac1HostedProjectile> _rac1Projectiles = [];
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _rac1HostileNodes = [];
    private readonly Dictionary<int, Rac1Class749HostProbe> _rac1HostileProbes = [];
    private bool _rac1SwingActive;
    private bool _rac1SwingResolved;
    private bool _rac1BombFireRequested;
    private double _rac1SwingAge;
    private double _rac1BombTickAccumulator;
    // Raw retail player-state word at +0x20a4. Its semantics remain intentionally unnamed.
    private int _rac1NativePlayerState20A4;
    private string _rac1CombatStatus = "off";

    private void ResetRac1LevelGameplay()
    {
        _rac1CrateNodes.Clear();
        _rac1PickupNodes.Clear();
        foreach (var projectile in _rac1Projectiles.Values)
            if (IsInstanceValid(projectile.Node)) projectile.Node.QueueFree();
        _rac1Projectiles.Clear();
        _rac1BoltCrates = new Rac1BoltCrateSession();
        _rac1Hostiles = new Rac1Class749HostileSession();
        _rac1Nanotech = new Rac1RatchetNanotechSession();
        _rac1BombGlove = new Rac1BombGloveSession(_rac1Weapons);
        _rac1HostileNodes.Clear();
        _rac1HostileProbes.Clear();
        _rac1SwingActive = false;
        _rac1SwingResolved = false;
        _rac1BombFireRequested = false;
        _rac1SwingAge = 0d;
        _rac1BombTickAccumulator = 0d;
        _rac1NativePlayerState20A4 = 0;
        _rac1CombatStatus = "off";
    }

    private void ConfigureRac1Gameplay(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        ResetRac1LevelGameplay();
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
                ?? CreateRac1FallbackNode(result, source, crate: true);
            _rac1CrateNodes.Add(node);
        }

        int rejectedHostiles = 0;
        foreach (var hostileSource in (world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
            .Where(source => source.NativeClassId == Rac1Class749Hostile.NativeClassId))
        {
            try
            {
                _ = Rac1Class749Hostile.ReadAuthored(hostileSource)
                    ?? throw new InvalidDataException("Class-749 source failed its authored-state contract.");
                var node = FindPresentedDynamic(result, hostileSource)
                    ?? CreateRac1FallbackNode(result, hostileSource, crate: false);
                var probe = _rac1Hostiles.Register(hostileSource, node.State);
                _rac1HostileNodes.Add(hostileSource.InstanceIndex, node);
                _rac1HostileProbes.Add(hostileSource.InstanceIndex, probe);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                rejectedHostiles++;
                GD.PrintErr($"[rac1-gameplay] class-749 i{hostileSource.InstanceIndex} unavailable: {ex.Message}");
            }
        }

        GD.Print($"[rac1-gameplay] ready: {_rac1CrateNodes.Count} admitted class-500 crates, " +
                 $"{_rac1HostileNodes.Count} class-749 hostiles ({rejectedHostiles} rejected)");
    }

    private bool TryGetRac1RepresentativeHostile(
        out RuntimeWorldScene.DynamicObjectNode? node,
        out Rac1Class749HostProbe? probe)
    {
        if (_rac1HostileNodes.TryGetValue(Rac1WitnessHostileInstance, out node) &&
            _rac1HostileProbes.TryGetValue(Rac1WitnessHostileInstance, out probe))
            return true;

        foreach (var pair in _rac1HostileNodes.OrderBy(pair => pair.Key))
        {
            if (_rac1HostileProbes.TryGetValue(pair.Key, out probe))
            {
                node = pair.Value;
                return true;
            }
        }

        node = null;
        probe = null;
        return false;
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
    private static RuntimeWorldScene.DynamicObjectNode CreateRac1FallbackNode(
        RuntimeWorldScene.Result result,
        RuntimeDynamicObject source,
        bool crate)
    {
        var root = new Node3D
        {
            Name = $"rac1_live_{source.NativeClassId}_{source.InstanceIndex}",
        };
        Mesh primitive = crate
            ? new BoxMesh { Size = new Vector3(1.15f, 1.15f, 1.15f) }
            : new SphereMesh { Radius = 0.6f, Height = 1.2f };
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = crate ? new Color(0.65f, 0.45f, 0.18f) : new Color(0.85f, 0.2f, 0.2f),
        };
        root.AddChild(new MeshInstance3D
        {
            Name = crate ? "BoltCrateHostMarker" : "Class749HostMarker",
            Mesh = primitive,
            MaterialOverride = material,
        });
        result.Root.AddChild(root);
        return new RuntimeWorldScene.DynamicObjectNode(source, root);
    }

    private void ArmRac1Gameplay(DebugPlayer player)
    {
        if (_world?.Game != "rac1") return;
        player.Rac1PrimaryAttackRequested += OnRac1PrimaryAttackRequested;
        player.Rac1WeaponSelectionRequested += OnRac1WeaponSelectionRequested;
        player.Rac1RespawnRequested += OnRac1RespawnRequested;
        player.Rac1GameplayAlive = !_rac1Nanotech.Probe().IsDead;
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

        _rac1SwingActive = true;
        _rac1SwingResolved = false;
        _rac1SwingAge = 0d;
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
            GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
        }
    }

    private void OnRac1RespawnRequested()
    {
        var death = _rac1Nanotech.Probe();
        if (_world is not { Game: "rac1", LevelId: 0 } || _player is null ||
            !death.HasRecoveredVeldinEnvironmentalRespawn)
            return;

        var respawn = _rac1Nanotech.Respawn();
        _player.ResetToSpawn();
        _player.Rac1GameplayAlive = true;
        _rac1CombatStatus = $"Veldin respawn: Nanotech {respawn.Nanotech}";
        RefreshRac1HudState();
        GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
    }

    private void TickRac1Gameplay(double delta)
    {
        if (_world?.Game != "rac1" || _player is null || !IsInstanceValid(_player)) return;

        TickRac1VeldinEnvironmentalDeath();
        if (_rac1Nanotech.Probe().IsDead) return;

        TickRac1Swing(delta);
        TickRac1BombGlove(delta);
        TickRac1Projectiles(delta);
        TickRac1Hostiles();
        TickRac1Pickups();
    }

    private void TickRac1VeldinEnvironmentalDeath()
    {
        if (_world is not { Game: "rac1", LevelId: 0, Environment: { } environment } ||
            _player is null || _rac1Nanotech.Probe().IsDead)
            return;

        var dead = _rac1Nanotech.TryApplyVeldinEnvironmentalDeath(
            new Rac1VeldinEnvironmentalDeathFacts(
                NativeVerticalPosition: _player.GlobalPosition.Y,
                DeathHeight: environment.DeathHeight,
                ContactSeparation: MeasureRac1ContactSeparation(),
                NativeSpecialPlayerState20A4: _rac1NativePlayerState20A4));
        if (dead is null) return;

        _player.Rac1GameplayAlive = false;
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
        if (!_rac1SwingActive)
        {
            return;
        }

        double previousAge = _rac1SwingAge;
        _rac1SwingAge += Math.Max(0d, delta) * Rac1NativeTicksPerSecond;
        bool crossedContactWindow = previousAge < Rac1WrenchCombatController.FirstSwingContactStartAge
            && _rac1SwingAge > Rac1WrenchCombatController.FirstSwingContactEndAge;
        if (!_rac1SwingResolved &&
            (_rac1Wrench.IsContactActive(Rac1WrenchCombatController.OrdinaryActionId,
                Rac1WrenchCombatController.OrdinaryProfileId, _rac1SwingAge) || crossedContactWindow))
        {
            double contactAge = crossedContactWindow
                ? (Rac1WrenchCombatController.FirstSwingContactStartAge + Rac1WrenchCombatController.FirstSwingContactEndAge) * 0.5d
                : _rac1SwingAge;
            _rac1SwingResolved = true;
            ResolveRac1WrenchContact(contactAge);
        }

        if (_rac1SwingAge > Rac1WrenchCombatController.FirstSwingContactEndAge)
        {
            _rac1SwingActive = false;
        }
    }

    private void ResolveRac1WrenchContact(double nativeAge)
    {
        if (_player is null) return;

        var facing = _rac1Wrench.ResolveFirstSwingFacing(_player.Rac1CurrentYaw);
        Vector3 forward = new(-(float)facing.X, 0f, (float)facing.Y);
        if (forward.LengthSquared() <= 1e-5f) return;
        forward = forward.Normalized();
        Vector3 root = _player.GlobalPosition + Vector3.Up * 1.0f;
        Vector3 tip = root + forward * Rac1WrenchReach;

        if (TryStrikeRac1Crate(nativeAge, root, tip))
        {
            return;
        }

        if (TryStrikeRac1Hostile(nativeAge, root, forward))
        {
            return;
        }

        _rac1CombatStatus = "wrench: no eligible contact";
    }
    private bool TryStrikeRac1Crate(double nativeAge, Vector3 root, Vector3 tip)
    {
        var sphere = _rac1Wrench.GetClass500ToolTipSphere(
            Rac1WrenchCombatController.OrdinaryActionId,
            Rac1WrenchCombatController.OrdinaryProfileId,
            nativeAge,
            new Rac1WrenchPoint(root.X, root.Y, root.Z),
            new Rac1WrenchPoint(tip.X, tip.Y, tip.Z));
        if (sphere is null)
        {
            return false;
        }

        Vector3 centre = new(
            (float)sphere.Value.Center.X,
            (float)sphere.Value.Center.Y,
            (float)sphere.Value.Center.Z);
        float maxDistance = (float)sphere.Value.Radius + Rac1CrateOriginPadding;
        var target = _rac1CrateNodes
            .Where(node => IsInstanceValid(node.Root) && node.Root.Visible)
            .Select(node => (Node: node, Distance: node.Root.GlobalPosition.DistanceTo(centre)))
            .Where(candidate => candidate.Distance <= maxDistance)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        if (target.Node is null)
        {
            return false;
        }

        var authored = Rac1BoltCrate.ReadAuthored(target.Node.Source);
        if (authored?.RewardCentre != 10)
        {
            return false;
        }
        var contactTarget = _rac1Wrench.AdmitGoal1RuntimeTarget(target.Node.Source)
            ?? throw new InvalidOperationException("Admitted R&C1 Bolt Crate was rejected by the wrench target contract.");
        var damage = _rac1Wrench.ApplyClass500ToolTipContact(
            Rac1WrenchCombatController.OrdinaryActionId,
            Rac1WrenchCombatController.OrdinaryProfileId,
            nativeAge,
            contactTarget,
            target.Node.Source,
            target.Node.State,
            _rac1BoltCrates,
            // Deterministic host RNG choice within the recovered range.
            // This does not claim the retail RNG selector.
            selectedTotal: authored.RewardCentre);
        if (damage?.BoltCrateBreak is not { } broken)
        {
            return false;
        }

        Vector3 rewardOrigin = target.Node.Root.GlobalPosition;
        target.Node.ApplyState(broken.EntityState);
        SpawnRac1BoltPickups(rewardOrigin, broken.Pickups);
        _rac1CombatStatus = $"crate {target.Node.Source.InstanceIndex} broke: +{broken.PhysicalValue} bolts emitted";
        GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
        return true;
    }

    private bool TryStrikeRac1Hostile(double nativeAge, Vector3 root, Vector3 forward)
    {
        var candidate = _rac1HostileNodes.Values
            .Where(hostile =>
                _rac1HostileProbes.ContainsKey(hostile.Source.InstanceIndex) &&
                IsInstanceValid(hostile.Root) &&
                hostile.Root.Visible)
            .Select(hostile =>
            {
                Vector3 to = hostile.Root.GlobalPosition - root;
                float along = to.Dot(forward);
                float perpendicular = (to - forward * along).Length();
                return new { Hostile = hostile, Along = along, Perpendicular = perpendicular };
            })
            .Where(hit =>
                hit.Along > 0f &&
                hit.Along <= Rac1WrenchReach + Rac1DirectContactRadius &&
                hit.Perpendicular <= Rac1DirectContactRadius)
            .OrderBy(hit => hit.Along)
            .ThenBy(hit => hit.Hostile.Source.InstanceIndex)
            .FirstOrDefault();
        if (candidate is null)
        {
            return false;
        }

        var hostile = candidate.Hostile;
        var contactTarget = _rac1Wrench.AdmitGoal1RuntimeTarget(hostile.Source)
            ?? throw new InvalidOperationException("R&C1 class-749 hostile was rejected by the wrench target contract.");
        var damage = _rac1Wrench.ResolveForwardDirectRecord(
            Rac1WrenchCombatController.OrdinaryActionId,
            Rac1WrenchCombatController.OrdinaryProfileId,
            nativeAge,
            contactTarget);
        if (damage is null)
        {
            return false;
        }

        var probe = _rac1Hostiles.ApplyWrenchDamage(hostile.Source, damage);
        _rac1HostileProbes[hostile.Source.InstanceIndex] = probe;
        // The native session admits both 0xfd and 0xfe terminal outcomes but does
        // not recover their selector. The live host uses 0xfd as an explicit
        // presentation choice so the proven terminal deactivation is observable.
        probe = _rac1Hostiles.ApplyTerminalStatus(
            hostile.Source, Rac1Class749Hostile.TerminalNativeStateFd);
        _rac1HostileProbes[hostile.Source.InstanceIndex] = probe;
        hostile.ApplyState(probe.EntityState);
        _rac1CombatStatus = $"hostile i{hostile.Source.InstanceIndex}: health 0 -> terminal 0xfd";
        GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
        return true;
    }
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
                    _rac1HostileProbes.ContainsKey(hostile.Source.InstanceIndex) &&
                    IsInstanceValid(hostile.Root) &&
                    hostile.Root.Visible)
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

            int admittedContacts = 0;
            foreach (var contact in contacts)
            {
                var hostile = contact.Hostile;
                var probe = _rac1HostileProbes[hostile.Source.InstanceIndex];
                var damage = _rac1BombGlove.ResolveGoal1Contact(
                    pair.Key, hostile.Source, probe.NativeState);
                if (damage is not null) admittedContacts++;
            }

            if (admittedContacts > 0)
            {
                _rac1BombGlove.CompleteProjectile(pair.Key);
                _rac1CombatStatus =
                    $"Bomb Glove contact: {admittedContacts} class-749 candidate(s); native consequence unresolved";
                GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
                impacted = true;
            }

            if (impacted || projectile.Age >= Rac1BombPresentationLifetime)
            {
                _rac1BombGlove.CompleteProjectile(pair.Key);
                projectile.Node.QueueFree();
                _rac1Projectiles.Remove(pair.Key);
            }
        }
    }

    private void TickRac1Hostiles()
    {
        if (_player is null || _rac1Nanotech.Probe().IsDead)
        {
            return;
        }

        foreach (var hostile in _rac1HostileNodes.Values.OrderBy(node => node.Source.InstanceIndex))
        {
            if (!_rac1HostileProbes.TryGetValue(hostile.Source.InstanceIndex, out var previous) ||
                !IsInstanceValid(hostile.Root) ||
                !hostile.Root.Visible)
            {
                continue;
            }

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
            var next = _rac1Hostiles.Step(
                hostile.Source,
                new Rac1Class749TargetFacts(
                    distance,
                    facingError,
                    new Rac1Class749WorldPoint(-hostilePosition.X, hostilePosition.Y, hostilePosition.Z)));
            if (next.NativeState != previous.NativeState)
            {
                GD.Print($"[rac1-gameplay] hostile i{hostile.Source.InstanceIndex}: state {previous.NativeState} -> {next.NativeState}");
            }
            if (next.Attack is { } attack)
            {
                var beforeNanotech = _rac1Nanotech.Probe();
                var nanotech = _rac1Nanotech.ApplyClass749Attack(attack);
                _player.Rac1GameplayAlive = !nanotech.IsDead;
                GD.Print($"[rac1-gameplay] hostile i{hostile.Source.InstanceIndex}: attack marker {attack.NativeMarker:0} damage {attack.NativeDamage:0.###}; Nanotech {nanotech.Nanotech}");
                _rac1CombatStatus = nanotech.IsDead
                    ? "Nanotech 0: combat-death restart/checkpoint semantics unresolved"
                    : $"class-749 hit: Nanotech {nanotech.Nanotech}/{nanotech.RespawnNanotech}";
                RefreshRac1HudState(Rac1HudProjection.DamageFeedback(beforeNanotech, nanotech));
            }
            _rac1HostileProbes[hostile.Source.InstanceIndex] = next;
            if (_rac1Nanotech.Probe().IsDead)
            {
                break;
            }
        }
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

        int activeHostiles = _rac1HostileNodes.Values.Count(hostile =>
            IsInstanceValid(hostile.Root) && hostile.Root.Visible);
        string hostile = TryGetRac1RepresentativeHostile(out var representativeNode, out var representativeProbe) &&
            representativeNode is not null && representativeProbe is not null
            ? $"class-749 family {_rac1HostileProbes.Count} ({activeHostiles} active); representative i{representativeNode.Source.InstanceIndex} state {representativeProbe.NativeState} health {representativeProbe.Health:0.###}"
            : $"class-749 family {_rac1HostileProbes.Count} ({activeHostiles} active)";
        var nanotech = _rac1Nanotech.Probe();
        string weapon = _rac1Weapons.Equipped == Rac1WeaponId.Wrench ? "Wrench" : "Bomb Glove";
        string restart = nanotech.HasRecoveredVeldinEnvironmentalRespawn
            ? "R Veldin environmental respawn"
            : nanotech.IsDead
                ? "combat restart unresolved"
                : "Veldin environmental respawn evidence only";
        return $"R&C1 combat: X attack · 1 wrench · 2 Bomb Glove · {restart} · {_rac1CombatStatus}\n" +
            $"Nanotech: {nanotech.Nanotech}/{nanotech.RespawnNanotech} ({nanotech.LifeState})   " +
            $"Weapon: {weapon}   item-10 ammo: {_rac1Weapons.FirstRangedAmmo}   projectiles: {_rac1Projectiles.Count}\n" +
            $"Bolts collected: {_rac1BoltCrates.CollectedBolts}   pickups: {_rac1PickupNodes.Count}   {hostile}";
    }

    private Rac1GameplaySnapshot? GetRac1GameplaySnapshot()
    {
        if (_world?.Game != "rac1" || _rac1CombatStatus == "off") return null;
        TryGetRac1RepresentativeHostile(out var representativeNode, out var representativeProbe);
        int activeHostiles = _rac1HostileNodes.Values.Count(hostile =>
            IsInstanceValid(hostile.Root) && hostile.Root.Visible);
        return new Rac1GameplaySnapshot(
            AdmittedCrates: _rac1CrateNodes.Count,
            DestroyedCrates: _rac1BoltCrates.DestroyedCrateCount,
            OutstandingPickups: _rac1BoltCrates.OutstandingPickupCount,
            CollectedBolts: _rac1BoltCrates.CollectedBolts,
            HostileCount: _rac1HostileProbes.Count,
            ActiveHostiles: activeHostiles,
            HostileInstance: representativeNode?.Source.InstanceIndex,
            HostileState: representativeProbe?.NativeState,
            HostileHealth: representativeProbe?.Health,
            HostileVisible: representativeNode is not null &&
                IsInstanceValid(representativeNode.Root) &&
                representativeNode.Root.Visible,
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
        int? HostileInstance,
        int? HostileState,
        float? HostileHealth,
        bool HostileVisible,
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
