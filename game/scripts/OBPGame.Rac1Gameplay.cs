using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

/// <summary>
/// Thin live host for the admitted R&C1 Goal 1 gameplay slice. Godot supplies
/// player input, contact/proximity facts and presentation; OBP.RAC1 remains the
/// authority for wrench contact, crate rewards and class-749 native state.
/// </summary>
public partial class OBPGame
{
    private const int Rac1RepresentativeHostileInstance = 149;
    private const double Rac1NativeTicksPerSecond = Rac1RatchetMovementController.UpdateHz;
    private const float Rac1WrenchReach = 2.35f;
    private const float Rac1DirectContactRadius = 1.25f;
    private const float Rac1CrateOriginPadding = 1.0f;
    private const float Rac1PickupCollectRadius = 1.4f;

    private readonly Rac1WrenchCombatController _rac1Wrench = new();
    private Rac1BoltCrateSession _rac1BoltCrates = new();
    private Rac1Class749HostileSession _rac1Hostiles = new();
    private readonly List<RuntimeWorldScene.DynamicObjectNode> _rac1CrateNodes = [];
    private readonly Dictionary<int, Node3D> _rac1PickupNodes = [];
    private RuntimeWorldScene.DynamicObjectNode? _rac1HostileNode;
    private Rac1Class749HostProbe? _rac1HostileProbe;
    private bool _rac1SwingActive;
    private bool _rac1SwingResolved;
    private double _rac1SwingAge;
    private string _rac1CombatStatus = "off";

    private void ResetRac1Gameplay()
    {
        _rac1CrateNodes.Clear();
        _rac1PickupNodes.Clear();
        _rac1BoltCrates = new Rac1BoltCrateSession();
        _rac1Hostiles = new Rac1Class749HostileSession();
        _rac1HostileNode = null;
        _rac1HostileProbe = null;
        _rac1SwingActive = false;
        _rac1SwingResolved = false;
        _rac1SwingAge = 0d;
        _rac1CombatStatus = "off";
    }

    private void ConfigureRac1Gameplay(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        ResetRac1Gameplay();
        if (world.Game != "rac1")
        {
            return;
        }

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

        var hostileSource = (world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
            .FirstOrDefault(source => source.NativeClassId == Rac1Class749Hostile.NativeClassId
                && source.InstanceIndex == Rac1RepresentativeHostileInstance);
        if (hostileSource is not null)
        {
            _rac1HostileNode = FindPresentedDynamic(result, hostileSource)
                ?? CreateRac1FallbackNode(result, hostileSource, crate: false);
            try
            {
                _rac1HostileProbe = _rac1Hostiles.RegisterRepresentative(
                    hostileSource, _rac1HostileNode.State);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                _rac1CombatStatus = $"hostile unavailable: {ex.Message}";
                _rac1HostileNode = null;
                _rac1HostileProbe = null;
            }
        }

        GD.Print($"[rac1-gameplay] ready: {_rac1CrateNodes.Count} admitted class-500 crates, " +
                 $"class-749 i{Rac1RepresentativeHostileInstance}={(_rac1HostileNode is null ? "missing" : "live")}");
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
        if (_world?.Game == "rac1")
        {
            player.WrenchAttackRequested += OnRac1WrenchAttackRequested;
        }
    }
    private void OnRac1WrenchAttackRequested()
    {
        _rac1SwingActive = true;
        _rac1SwingResolved = false;
        _rac1SwingAge = 0d;
        _rac1CombatStatus = "wrench swing";
        GD.Print("[rac1-gameplay] primary attack -> ordinary wrench swing");
    }

    private void TickRac1Gameplay(double delta)
    {
        if (_world?.Game != "rac1" || _player is null || !IsInstanceValid(_player))
        {
            return;
        }

        TickRac1Swing(delta);
        TickRac1Hostile();
        TickRac1Pickups();
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
        if (_player?.Camera is not { } camera)
        {
            return;
        }

        Vector3 forward = -camera.GlobalTransform.Basis.Z;
        forward.Y = 0f;
        if (forward.LengthSquared() <= 1e-5f)
        {
            return;
        }
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
        if (_rac1HostileNode is not { } hostile || _rac1HostileProbe is null ||
            !IsInstanceValid(hostile.Root) || !hostile.Root.Visible)
        {
            return false;
        }
        Vector3 to = hostile.Root.GlobalPosition - root;
        float along = to.Dot(forward);
        float perpendicular = (to - forward * along).Length();
        if (along <= 0f || along > Rac1WrenchReach + Rac1DirectContactRadius ||
            perpendicular > Rac1DirectContactRadius)
        {
            return false;
        }

        var contactTarget = _rac1Wrench.AdmitGoal1RuntimeTarget(hostile.Source)
            ?? throw new InvalidOperationException("Representative R&C1 class-749 hostile was rejected by the wrench target contract.");
        var damage = _rac1Wrench.ResolveForwardDirectRecord(
            Rac1WrenchCombatController.OrdinaryActionId,
            Rac1WrenchCombatController.OrdinaryProfileId,
            nativeAge,
            contactTarget);
        if (damage is null)
        {
            return false;
        }

        _rac1HostileProbe = _rac1Hostiles.ApplyWrenchDamage(hostile.Source, damage);
        // The native session admits both 0xfd and 0xfe terminal outcomes but does
        // not recover their selector. The live host uses 0xfd as an explicit
        // presentation choice so the proven terminal deactivation is observable.
        _rac1HostileProbe = _rac1Hostiles.ApplyTerminalStatus(
            hostile.Source, Rac1Class749Hostile.TerminalNativeStateFd);
        hostile.ApplyState(_rac1HostileProbe.EntityState);
        _rac1CombatStatus = $"hostile i{hostile.Source.InstanceIndex}: health 0 -> terminal 0xfd";
        GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
        return true;
    }
    private void TickRac1Hostile()
    {
        if (_rac1HostileNode is not { } hostile || _rac1HostileProbe is null ||
            _player is null || !IsInstanceValid(hostile.Root) || !hostile.Root.Visible)
        {
            return;
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

        double marker = _rac1HostileProbe.NativeState == Rac1Class749Hostile.AttackNativeState
            ? Rac1Class749Hostile.AttackMarker
            : 0d;
        var next = _rac1Hostiles.Step(
            hostile.Source,
            new Rac1Class749TargetFacts(TargetAcquired: true, distance, facingError),
            marker);
        if (next.NativeState != _rac1HostileProbe.NativeState)
        {
            GD.Print($"[rac1-gameplay] hostile i{hostile.Source.InstanceIndex}: state {_rac1HostileProbe.NativeState} -> {next.NativeState}");
        }
        if (next.Attack is { } attack)
        {
            GD.Print($"[rac1-gameplay] hostile i{hostile.Source.InstanceIndex}: attack marker {attack.NativeMarker:0} damage {attack.NativeDamage:0.###}");
            _rac1CombatStatus = $"hostile attack emitted ({attack.NativeDamage:0.###} native damage)";
        }
        _rac1HostileProbe = next;
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

        string hostile = _rac1HostileProbe is null
            ? "class-749 unavailable"
            : $"class-749 i{Rac1RepresentativeHostileInstance} state {_rac1HostileProbe.NativeState} health {_rac1HostileProbe.Health:0.###}";
        return $"R&C1 combat: X wrench · {_rac1CombatStatus}\n" +
            $"Bolts collected: {_rac1BoltCrates.CollectedBolts}   pickups: {_rac1PickupNodes.Count}   {hostile}";
    }

    private Rac1GameplaySnapshot? GetRac1GameplaySnapshot()
    {
        if (_world?.Game != "rac1" || _rac1CombatStatus == "off") return null;
        return new Rac1GameplaySnapshot(
            AdmittedCrates: _rac1CrateNodes.Count,
            DestroyedCrates: _rac1BoltCrates.DestroyedCrateCount,
            OutstandingPickups: _rac1BoltCrates.OutstandingPickupCount,
            CollectedBolts: _rac1BoltCrates.CollectedBolts,
            HostileInstance: _rac1HostileNode?.Source.InstanceIndex,
            HostileState: _rac1HostileProbe?.NativeState,
            HostileHealth: _rac1HostileProbe?.Health,
            HostileVisible: _rac1HostileNode is { } hostile && IsInstanceValid(hostile.Root) && hostile.Root.Visible,
            Status: _rac1CombatStatus);
    }

    private sealed record Rac1GameplaySnapshot(
        int AdmittedCrates,
        int DestroyedCrates,
        int OutstandingPickups,
        int CollectedBolts,
        int? HostileInstance,
        int? HostileState,
        float? HostileHealth,
        bool HostileVisible,
        string Status);
}
