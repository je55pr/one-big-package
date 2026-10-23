using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OneBigPackage;

/// <summary>
/// Provisional Godot presentation of the native Kerwan taxi placements. The
/// underlying taxi/session models and authored positions remain RAC1-owned.
/// </summary>
public partial class OBPGame
{
    private Rac1MetropolisTaxiSession? _rac1MetropolisTaxis;
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _rac1TaxiNodes = [];
    private readonly Dictionary<int, StaticBody3D> _rac1TaxiCollisionBodies = [];
    private readonly Dictionary<int, Rac1TaxiNativeHover> _rac1TaxiHovers = [];
    private RuntimeWorldScene.DynamicObjectNode? _rac1FlyingTaxi;
    private Vector3 _rac1FlyingTaxiOrigin;
    private Vector3 _rac1TaxiBoardingPosition;
    private Vector3 _rac1TaxiRiderOffset;
    private Vector3 _rac1TaxiCameraWorldOffset;
    private bool _rac1TaxiCameraWorldOwned;
    private Node.ProcessModeEnum _rac1TaxiPreviousPlayerMode;

    /// <summary>
    /// Cancel an in-flight ride before normal death/retry/world transition
    /// processing. Collision and movement are restored together: leaving the
    /// PlayerHost process disabled would strand Ratchet after a reset.
    /// </summary>
    private void AbortRac1MetropolisTaxiRide()
    {
        var riding = _rac1MetropolisTaxis?.Riding;
        if (_rac1FlyingTaxi is null && riding is null)
            return;

        if (riding is not null &&
            _rac1TaxiCollisionBodies.TryGetValue(riding.InstanceIndex, out var hull) &&
            IsInstanceValid(hull))
            hull.CollisionLayer = 1;

        if (_player is { } player && IsInstanceValid(player) && _rac1FlyingTaxi is not null)
        {
            player.ProcessMode = _rac1TaxiPreviousPlayerMode;
            player.VisualRoot.Visible = true;
            Vector3 exit = _rac1TaxiBoardingPosition;
            // The authored boarding dock is preferable to a moving vehicle or
            // a far-end waypoint, neither of which guarantees walkable ground.
            if (player.IsInsideTree() &&
                TryResolveRac1TaxiLanding(player, exit, out var supported))
                exit = supported;
            player.ApplyRac1TaxiArrival(new RuntimeSpawn(
                -exit.X, exit.Y, exit.Z, player.Rac1CurrentYaw));
            // A recovered camera has a world-space transform and was moved
            // with the taxi while ordinary player physics was suspended.
            if (_rac1TaxiCameraWorldOwned)
                player.CenterCameraBehindPlayer();
        }
        _rac1TaxiCameraWorldOwned = false;

        _rac1MetropolisTaxis?.AbortRide();
        // A second taxi may be approaching or parked at its destination.
        // Only rewind the interrupted actor, not the rest of the population.
        if (riding is { } stop &&
            _rac1TaxiNodes.TryGetValue(stop.InstanceIndex, out var node) &&
            IsInstanceValid(node.Root))
            node.Root.GlobalPosition = FromTaxiPoint(
                stop.ApproachPath is { Count: > 1 }
                    ? stop.ApproachPath[0] : stop.Position);
        _rac1FlyingTaxi = null;
    }

    private void ReleaseRac1MetropolisTaxiHulls()
    {
        foreach (var hull in _rac1TaxiCollisionBodies.Values)
        {
            if (IsInstanceValid(hull))
                hull.QueueFree();
        }
        _rac1TaxiCollisionBodies.Clear();
    }

    private void ResetRac1MetropolisTaxis()
    {
        AbortRac1MetropolisTaxiRide();
        _rac1MetropolisTaxis = null;
        ReleaseRac1MetropolisTaxiHulls();
        _rac1TaxiNodes.Clear();
        _rac1TaxiHovers.Clear();
    }

    private void ConfigureRac1MetropolisTaxis(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        _rac1MetropolisTaxis = Rac1MetropolisTaxiSession.TryCreate(world);
        if (_rac1MetropolisTaxis is not { } taxis) return;
        foreach (var stop in taxis.Stops)
        {
            var node = result.DynamicObjectNodes?.SingleOrDefault(n =>
                n.Source.InstanceIndex == stop.InstanceIndex &&
                n.Source.NativeClassId == stop.NativeClassId);
            if (node is null)
            {
                GD.PrintErr($"[rac1-taxi] missing rendered taxi i{stop.InstanceIndex}; disabling all taxis");
                _rac1MetropolisTaxis = null;
                ReleaseRac1MetropolisTaxiHulls();
                _rac1TaxiNodes.Clear();
                _rac1TaxiHovers.Clear();
                return;
            }
            _rac1TaxiNodes.Add(stop.InstanceIndex, node);
            if (stop.NativeHoverPhases is { } phases)
                _rac1TaxiHovers.Add(stop.InstanceIndex,
                    new Rac1TaxiNativeHover(phases));
            // The scene importer intentionally creates visual-only Mobies. Taxi
            // hulls are walkable in the game, so add collision only for these
            // five Kerwan instances, using the actual authored render surfaces.
            var body = new StaticBody3D { Name = "KerwanTaxiHull" };
            foreach (var mesh in node.Root.GetChildren().OfType<MeshInstance3D>())
            {
                if (mesh.Mesh is not { } surface || surface.GetSurfaceCount() == 0)
                    continue;
                body.AddChild(new CollisionShape3D
                {
                    Name = "TaxiHullSurface",
                    Shape = surface.CreateTrimeshShape(),
                    Transform = mesh.Transform,
                });
            }
            if (body.GetChildCount() == 0)
            {
                GD.PrintErr($"[rac1-taxi] i{stop.InstanceIndex} has no collision mesh; disabling taxis");
                _rac1MetropolisTaxis = null;
                ReleaseRac1MetropolisTaxiHulls();
                _rac1TaxiNodes.Clear();
                _rac1TaxiHovers.Clear();
                return;
            }
            node.Root.AddChild(body);
            _rac1TaxiCollisionBodies.Add(stop.InstanceIndex, body);
            // Class-816 starts at the first waypoint of its native inbound
            // route, not at the stop where the importer places the Moby.
            if (stop.ApproachPath is { Count: > 1 })
                node.Root.GlobalPosition = FromTaxiPoint(stop.ApproachPath[0]);
        }

        GD.Print($"[rac1-taxi] Kerwan: {_rac1TaxiNodes.Count} models, " +
            $"{taxis.Stops.Count(s => s.ApproachTrigger is not null)} native-triggered approach routes, " +
            $"{taxis.Stops.Count(s => s.OutboundPath.Count > 1)} passenger routes");

    }

    // The runtime uses Y-up coordinates, while Godot mirrors the X-axis.
    private static Rac1TaxiPoint ToTaxiPoint(Vector3 scenePosition) =>
        new(-scenePosition.X, scenePosition.Y, scenePosition.Z);

    private static Vector3 FromTaxiPoint(Rac1TaxiPoint position) =>
        RuntimeWorldScene.ToScene(position.X, position.Y, position.Z);

    // Native class 816/1012 both steer moby yaw toward their travel direction.
    // The source yaw-seek recurrence is still being recovered; this smooth
    // host presentation at least prevents the cabs sliding sideways.
    private static void OrientRac1Taxi(Node3D root, Vector3 target, double delta)
    {
        Vector3 motion = target - root.GlobalPosition;
        motion.Y = 0;
        if (motion.LengthSquared() < 0.0001f || motion.LengthSquared() > 100f)
            return;
        float targetYaw = Mathf.Atan2(motion.X, motion.Z);
        float currentYaw = root.GlobalRotation.Y;
        float fraction = Mathf.Clamp((float)delta * 5f, 0f, 1f);
        root.GlobalRotation = new(
            root.GlobalRotation.X,
            Mathf.LerpAngle(currentYaw, targetYaw, fraction),
            root.GlobalRotation.Z);
    }

    // Both Kerwan taxi families call native oscillators every updater tick.
    // This modifies rendered hulls and rider pose, never authored path
    // endpoints or collision-based dismount eligibility.
    private Vector3 PresentRac1Taxi(
        int instanceIndex, Node3D root, Vector3 authoredPosition, double delta)
    {
        OrientRac1Taxi(root, authoredPosition, delta);
        if (_rac1TaxiHovers.TryGetValue(instanceIndex, out var hover))
        {
            authoredPosition.Y += (float)hover.HeightOffset;
            Vector3 rotation = root.GlobalRotation;
            root.GlobalRotation = new Vector3(
                (float)hover.PitchRadians, rotation.Y, (float)hover.RollRadians);
        }
        root.GlobalPosition = authoredPosition;
        return authoredPosition;
    }

    private bool TryHandleRac1MetropolisTaxiInput(InputEvent input)
    {
        bool pressed = input switch
        {
            InputEventKey { Keycode: Key.T, Pressed: true, Echo: false } => true,
            InputEventJoypadButton { ButtonIndex: JoyButton.Y, Pressed: true } => true,
            _ => false,
        };
        if (!pressed || _mode != Mode.World || _rac1MetropolisTaxis is not { } taxis ||
            _player is not { } player || !IsInstanceValid(player) ||
            _rac1Nanotech.Probe().IsDead || !player.Rac1GameplayAlive ||
            player.Scripted || player.DevelopmentControls.FlyEnabled ||
            player.Rac1RemoteControlActive || GetTree().Paused ||
            PauseMenuOpen || ControllerSettingsOpen || Rac1PlanetMapOpen ||
            QuickSelectOpen || _args.CaptureFrame is not null)
            return false;

        if (!taxis.TryBoard(ToTaxiPoint(player.GlobalPosition)))
            return false;

        var boarding = taxis.Riding!;
        _rac1FlyingTaxi = _rac1TaxiNodes[boarding.InstanceIndex];
        _rac1FlyingTaxiOrigin = _rac1FlyingTaxi.Root.GlobalPosition;
        _rac1TaxiBoardingPosition = FromTaxiPoint(boarding.Position);
        var relative = player.GlobalPosition - _rac1FlyingTaxiOrigin;
        _rac1TaxiRiderOffset = new Vector3(
            Mathf.Clamp(relative.X, -1.4f, 1.4f),
            Mathf.Clamp(relative.Y, 0.12f, 0.8f),
            Mathf.Clamp(relative.Z, -1.4f, 1.4f));
        // The retail helper hero_cam_target_set invokes native player state
        // 0x65 and tracks a world-space target. The host has not recovered
        // that camera state yet. Preserve the current camera view relative
        // to Ratchet instead of stranding a TopLevel camera at the dock.
        _rac1TaxiCameraWorldOwned = player.Camera.TopLevel;
        _rac1TaxiCameraWorldOffset = player.Camera.GlobalPosition - player.GlobalPosition;
        // Once boarded, the player is attached by the taxi host. The moving
        // hull must not interfere with the arrival ground-contact ray.
        _rac1TaxiCollisionBodies[boarding.InstanceIndex].CollisionLayer = 0;
        _rac1TaxiPreviousPlayerMode = player.ProcessMode;
        player.ProcessMode = Node.ProcessModeEnum.Disabled;
        // Native attach_frame_set carries the visible character; hiding
        // Ratchet throughout the flight was not retail behavior.
        player.VisualRoot.Visible = true;
        GD.Print($"[rac1-taxi] boarded i{boarding.InstanceIndex} -> {boarding.DestinationLabel}");
        return true;
    }

    private void TickRac1MetropolisTaxis(double delta)
    {
        if (_rac1MetropolisTaxis is not { } taxis ||
            _player is not { } player || !IsInstanceValid(player))
            return;

        foreach (var hover in _rac1TaxiHovers.Values)
            hover.Advance(delta);

        // The stationary class-816 hull also receives native bob/tilt
        // updates, despite having no authored passenger path.
        foreach (var stop in taxis.Stops.Where(s => s.OutboundPath.Count == 0 &&
            s.NativeHoverPhases.HasValue))
        {
            if (_rac1TaxiNodes.TryGetValue(stop.InstanceIndex, out var parked) &&
                IsInstanceValid(parked.Root))
                PresentRac1Taxi(stop.InstanceIndex, parked.Root,
                    FromTaxiPoint(stop.Position), delta);
        }

        foreach (var traffic in taxis.TickTraffic(ToTaxiPoint(player.GlobalPosition), delta))
        {
            if (_rac1TaxiNodes.TryGetValue(traffic.InstanceIndex, out var instance) &&
                IsInstanceValid(instance.Root))
            {
                PresentRac1Taxi(traffic.InstanceIndex, instance.Root,
                    FromTaxiPoint(traffic.Position), delta);
            }
        }

        Rac1TaxiFlight? flight = taxis.Tick(delta);
        if (flight is null) return;
        if (_rac1FlyingTaxi is not { } taxi)
        {
            AbortRac1MetropolisTaxiRide();
            return;
        }
        if (!IsInstanceValid(taxi.Root))
        {
            AbortRac1MetropolisTaxiRide();
            return;
        }

        var taxiPosition = PresentRac1Taxi(
            flight.From.InstanceIndex, taxi.Root, FromTaxiPoint(flight.Position), delta);
        player.GlobalPosition = taxiPosition + _rac1TaxiRiderOffset;
        if (_rac1TaxiCameraWorldOwned && IsInstanceValid(player.Camera))
            player.Camera.GlobalPosition = player.GlobalPosition + _rac1TaxiCameraWorldOffset;

        if (!flight.JustArrived) return;

        // Class-816 leaves the taxi at the destination until Ratchet moves
        // 15 native units away, then sends it down its authored exit spline.
        // Class-1012 is still a provisional return shuttle.
        if (flight.From.ApproachPath is not { Count: > 1 })
            taxi.Root.GlobalPosition = _rac1FlyingTaxiOrigin;
        // Keep the active hull out of the landing ray. Its top surface
        // is not evidence of a permanent walkable destination.
        // A path endpoint is NOT proof of walkable ground. Retail class-1012
        // taxis have additional native path/state segments not yet recovered:
        // two currently imported outbound splines end in mid-air. Never drop
        // the player into space when the path has no safe docking surface.
        var destination = FromTaxiPoint(flight.Arrival);
        bool docked = TryResolveRac1TaxiLanding(player, destination, out var exit);
        if (!docked)
        {
            var source = FromTaxiPoint(flight.From.Position);
            if (!TryResolveRac1TaxiLanding(player, source, out exit))
                exit = source + Vector3.Up * 0.1f;
            // This is the intentional class-1012 safety path, not a fatal
            // engine error: treat it as a host diagnostic in smoke runs.
            GD.Print($"[rac1-taxi] host landing fallback: route i{flight.From.InstanceIndex} " +
                $"has no supported landing at {destination}; returned to boarding dock");
        }

        if (_rac1TaxiCollisionBodies.TryGetValue(flight.From.InstanceIndex, out var hull) &&
            IsInstanceValid(hull))
            hull.CollisionLayer = 1;

        player.ProcessMode = _rac1TaxiPreviousPlayerMode;
        player.VisualRoot.Visible = true;
        // RuntimeSpawn is in world coordinates, not mirrored Godot scene space.
        player.ApplyRac1TaxiArrival(new RuntimeSpawn(
            -exit.X, exit.Y, exit.Z, player.Rac1CurrentYaw));
        if (_rac1TaxiCameraWorldOwned)
            player.CenterCameraBehindPlayer();
        _rac1TaxiCameraWorldOwned = false;
        GD.Print($"[rac1-taxi] finished i{flight.From.InstanceIndex} at {exit}, " +
            $"destination supported={docked}");
        taxis.CompleteArrival();
        _rac1FlyingTaxi = null;
    }

    // Limit dismounts to nearby upward-facing real collision. In particular,
    // never use the old "endpoint + 1.5" fallback: it stranded Ratchet in air.
    private static bool TryResolveRac1TaxiLanding(
        PlayerHost player, Vector3 target, out Vector3 exit)
    {
        Vector2[] nearby =
        [
            Vector2.Zero,
            new(1.8f, 0), new(-1.8f, 0),
            new(0, 1.8f), new(0, -1.8f),
            new(1.8f, 1.8f), new(-1.8f, 1.8f),
            new(1.8f, -1.8f), new(-1.8f, -1.8f),
        ];
        foreach (var offset in nearby)
        {
            var sample = target + new Vector3(offset.X, 0, offset.Y);
            var query = PhysicsRayQueryParameters3D.Create(
                sample + Vector3.Up * 8f, sample + Vector3.Down * 35f);
            query.Exclude = [player.GetRid()];
            var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (!hit.TryGetValue("position", out var contact) ||
                !hit.TryGetValue("normal", out var normal) ||
                normal.AsVector3().Y < 0.70710677f)
                continue;
            Vector3 point = contact.AsVector3();
            if (point.Y < target.Y - 6f || point.Y > target.Y + 3f)
                continue;
            exit = point + Vector3.Up * 0.1f;
            return true;
        }

        exit = default;
        return false;
    }

    private string GetRac1MetropolisTaxiHudLine()
    {
        if (_rac1MetropolisTaxis is not { } taxis ||
            _player is not { } player || !IsInstanceValid(player)) return string.Empty;

        if (taxis.IsRiding) return "Taxi: in flight";
        var hero = ToTaxiPoint(player.GlobalPosition);
        var near = taxis.FindBoardable(hero);
        if (near is not null)
        {
            // Class 816 requests retail HUD message 0xbc9. Class 1012
            // selects among 0xbcb/0xbcd/0xbce/0xbcf depending on side.
            // Text is source decoded; input admission remains host policy.
            int message = near.NativeClassId == 816 ? 0xbc9 :
                near.NativeInitialSide == 0 ? 0xbce : 0xbcf;
            return Rac1RetailUiAssets.Text(message, "Taxi: T / Triangle to ride");
        }
        var inbound = taxis.Stops.FirstOrDefault(stop =>
            stop.ApproachTrigger?.Contains(hero) == true &&
            taxis.PhaseOf(stop.InstanceIndex) is
                Rac1MetropolisTaxiSession.TrafficPhase.WaitingForHero or
                Rac1MetropolisTaxiSession.TrafficPhase.Approaching);
        return inbound is null ? string.Empty : "Taxi: approaching";

    }
}
