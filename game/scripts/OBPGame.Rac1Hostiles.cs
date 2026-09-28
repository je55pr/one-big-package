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

            Vector3 hostilePosition = hostile.Root.GlobalPosition;
            Vector3 targetPosition = _player.GlobalPosition;
            Rac1GameplayEntityRef? targetIdentity = Rac1GameplayEntityRef.Player;
            bool linkedObjectTerminal = false;
            if (previous.NativeState == Rac1Class749Hostile.LinkedObjectNativeState &&
                previous.LinkedTargetInstanceIndex is int linkedTargetInstanceIndex)
            {
                _rac1LinkedTargetNodes.TryGetValue(
                    linkedTargetInstanceIndex,
                    out var linkedNode);
                linkedObjectTerminal = linkedNode is null ||
                    !IsInstanceValid(linkedNode.Root) ||
                    linkedNode.State.Presentation.Presence != RuntimeEntityPresence.Active;
                if (linkedObjectTerminal)
                {
                    targetPosition = hostilePosition;
                    targetIdentity = null;
                }
                else
                {
                    targetPosition = linkedNode!.Root.GlobalPosition;
                    targetIdentity = Rac1GameplayEntityRef.Moby(
                        new Rac1MobyRuntimeKey(
                            linkedNode.Source.NativeClassId,
                            linkedNode.Source.InstanceIndex));
                }
            }

            Vector3 toTarget = targetPosition - hostilePosition;
            double distance = toTarget.Length();
            Vector3 planarToTarget = new(toTarget.X, 0f, toTarget.Z);
            Vector3 planarForward = -hostile.Root.GlobalTransform.Basis.Z;
            planarForward.Y = 0f;
            double facingError = Math.PI;
            if (planarToTarget.LengthSquared() > 1e-6f &&
                planarForward.LengthSquared() > 1e-6f)
            {
                planarToTarget = planarToTarget.Normalized();
                planarForward = planarForward.Normalized();
                facingError = Math.Abs(
                    planarForward.SignedAngleTo(planarToTarget, Vector3.Up));
            }

            Rac1Class749TargetDescriptor? targetDescriptor =
                targetIdentity is { } identity
                ? new Rac1Class749TargetDescriptor(
                    identity,
                    new Rac1Class749WorldPoint(
                        -targetPosition.X,
                        targetPosition.Y,
                        targetPosition.Z))
                : null;
            var next = _rac1MobyRuntime.DispatchUpdate<Rac1Class749HostProbe>(
                hostile.Source,
                new Rac1Class749TargetFacts(
                    distance,
                    facingError,
                    new Rac1Class749WorldPoint(
                        -hostilePosition.X,
                        hostilePosition.Y,
                        hostilePosition.Z),
                    StatusSentinel: null,
                    TargetDescriptor: targetDescriptor,
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
                _rac1DamageTransport.Publish(damageEvent);
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

}
