using System.Collections.Immutable;
using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// World-lease-local Godot owner for Kerwan taxi instances. Native route,
/// docking and traffic states remain in the RAC1 session, while authored
/// scene hulls and passenger/camera capture are private to this lease.
/// </summary>
internal sealed class Rac1MetropolisTaxiHost : IDisposable
{
    private readonly Rac1TaxiHostPorts _ports;
    private Rac1MetropolisTaxiSession? _session;
    private readonly Dictionary<int, Rac1HostedTaxiCab> _cabs = [];
    private Rac1HostedTaxiCab? _activeCab;
    private Vector3 _flyingOrigin;
    private Vector3 _boardingPosition;
    private Rac1TaxiPassengerFrame? _riderFrame;
    private Vector3 _cameraWorldOffset;
    private bool _cameraWorldOwned;
    private Node.ProcessModeEnum _previousPlayerMode;
    private Rac1MetropolisTaxiStop? _pendingStop;
    private Rac1TaxiHeroTargetSession? _heroTarget;
    private double _walkElapsed;
    private IRac1TaxiPassengerController? _capturedPassenger;
    private bool _disposed;

    internal Rac1MetropolisTaxiHost(Rac1TaxiHostPorts ports) => _ports = ports;

    // This is native route/session data, not the mutable presentation roster.
    internal Rac1MetropolisTaxiSession? NativeSession => _disposed ? null : _session;

    internal void AbortRide()
    {
        var riding = _session?.Riding;
        if (_activeCab is null && riding is null && _pendingStop is null)
            return;
        var player = _capturedPassenger ?? _ports.Passenger();
        if (player is { Valid: true })
            player.EndWalk();
        _pendingStop = null;
        _heroTarget = null;
        _walkElapsed = 0;
        if (riding is not null && _cabs.TryGetValue(riding.InstanceIndex, out var hull) && hull.Valid)
            hull.CollisionLayer = 1;
        if (player is { Valid: true } && _activeCab is not null)
        {
            player.ProcessMode = _previousPlayerMode;
            player.VisualVisible = true;
            var exit = _boardingPosition;
            if (player.InsideTree && TryResolveLanding(player, exit, out var supported))
                exit = supported;
            player.Arrive(exit);
            if (_cameraWorldOwned && player.CameraValid)
                player.RestoreCamera();
        }
        _cameraWorldOwned = false;
        _riderFrame = null;
        _session?.AbortRide();
        // Rewind only the interrupted cab. Other cabs may be independently
        // approaching or docked at their authored route endpoints.
        if (riding is { } stop && _cabs.TryGetValue(stop.InstanceIndex, out var cab) && cab.Valid)
            cab.Root.GlobalPosition = FromTaxiPoint(
                stop.ApproachPath is { Count: > 1 } ? stop.ApproachPath[0] : stop.Position);
        _activeCab = null;
        _capturedPassenger = null;
    }

    internal void Reset()
    {
        AbortRide();
        _session = null;
        foreach (var cab in _cabs.Values)
            cab.Dispose();
        _cabs.Clear();
        _capturedPassenger = null;
        _cameraWorldOwned = false;
        _riderFrame = null;
    }

    internal void Configure(RuntimeWorld world, RuntimeWorldScene.Result scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Reset();
        var session = Rac1MetropolisTaxiSession.TryCreate(world);
        if (session is null) return;
        foreach (var stop in session.Stops)
        {
            var node = scene.DynamicObjectNodes?.SingleOrDefault(n =>
                n.Source.InstanceIndex == stop.InstanceIndex &&
                n.Source.NativeClassId == stop.NativeClassId);
            if (node is null)
            {
                GD.PrintErr($"[rac1-taxi] missing rendered taxi i{stop.InstanceIndex}; disabling all taxis");
                Reset();
                return;
            }
            var cab = Rac1HostedTaxiCab.TryMount(stop, node);
            if (cab is null)
            {
                GD.PrintErr($"[rac1-taxi] i{stop.InstanceIndex} has no collision mesh; disabling taxis");
                Reset();
                return;
            }
            _cabs.Add(stop.InstanceIndex, cab);
        }
        _session = session;
        GD.Print($"[rac1-taxi] Kerwan: {_cabs.Count} models, " +
            $"{session.Stops.Count(s => s.ApproachTrigger is not null)} native-triggered approach routes, " +
            $"{session.Stops.Count(s => s.OutboundPath.Count > 1)} passenger routes");
    }

    // Runtime uses Y-up coordinates; the Godot presentation mirrors X.
    internal static Rac1TaxiPoint ToTaxiPoint(Vector3 position) =>
        new(-position.X, position.Y, position.Z);
    internal static Vector3 FromTaxiPoint(Rac1TaxiPoint position) =>
        RuntimeWorldScene.ToScene(position.X, position.Y, position.Z);
    internal static Rac1TaxiCabBasis ToTaxiCabBasis(Basis sceneBasis)
    {
        var axes = sceneBasis.Orthonormalized();
        return new(
            new(axes.X.X, axes.X.Y, axes.X.Z),
            new(axes.Y.X, axes.Y.Y, axes.Y.Z),
            new(axes.Z.X, axes.Z.Y, axes.Z.Z));
    }

    internal bool TryHandleInput(InputEvent input)
    {
        bool pressed = input switch
        {
            InputEventKey { Keycode: Key.T, Pressed: true, Echo: false } => true,
            InputEventJoypadButton { ButtonIndex: JoyButton.Y, Pressed: true } => true,
            _ => false,
        };
        if (_disposed || !pressed || !_ports.InputEnabled() || _session is not { } taxis ||
            _ports.Passenger() is not { Valid: true } player ||
            _ports.Dead() || !player.Alive ||
            player.Scripted || player.FlyEnabled ||
            player.RemoteControlActive)
            return false;

        if (_pendingStop is not null || taxis.IsRiding)
            return false;
        var stop = taxis.FindBoardable(ToTaxiPoint(player.Position));
        if (stop is null)
            return false;

        // The class-1012 source dock lacks proven walkable collision; its
        // authored waypoint is airborne above the currently imported floor.
        // Do not drive Ratchet through a falling 0x65 target walk. Preserve
        // the previously bounded direct-boarding host fallback until the
        // two-ended native dock / passenger attachment is recovered.
        var heroPosition = ToTaxiPoint(player.Position);
        double sourceDx = stop.Position.X - heroPosition.X;
        double sourceDz = stop.Position.Z - heroPosition.Z;
        // Even class-816's off-centre source hull lacks imported walkable
        // collision (the staged 0.65-unit offset falls to a lower floor).
        // Only attempt 0x65 target alignment from the immediate dock.
        // Wider approaches retain the safe bounded attachment path.
        if (stop.NativeClassId == 1012 ||
            sourceDx * sourceDx + sourceDz * sourceDz > 0.2d * 0.2d ||
            Math.Abs(stop.Position.Y - heroPosition.Y) > 1.5d)
        {
            if (stop.NativeClassId == 816)
                GD.Print($"[rac1-taxi] host direct-board source safety i{stop.InstanceIndex}: " +
                    $"hero={heroPosition}, dock={stop.Position}, dx={sourceDx:0.000}, " +
                    $"dz={sourceDz:0.000}, dy={stop.Position.Y - heroPosition.Y:0.000}");
            if (!taxis.TryBoard(heroPosition))
                return false;
            AttachPassenger(taxis, player);
            return true;
        }

        // Native 0x00233ee8 stores the chosen taxi path point in
        // Ratchet+0x1640, facing yaw in +0x164c, and enters 0x65.
        // Path +0xa8 (816) / +0x94 (1012) begins at the source dock.
        var headingPoint = stop.OutboundPath[1];
        var startPoint = stop.OutboundPath[0];
        double heading = Math.Atan2(
            headingPoint.X - startPoint.X,
            headingPoint.Z - startPoint.Z);
        _heroTarget = new Rac1TaxiHeroTargetSession(
            stop.Position, heading);
        _pendingStop = stop;
        _capturedPassenger = player;
        _walkElapsed = 0;
        player.BeginWalk(FromTaxiPoint(stop.Position), heading);
        GD.Print($"[rac1-taxi] begin native target walk i{stop.InstanceIndex} (0x65)");
        return true;
    }

    private void AttachPassenger(
        Rac1MetropolisTaxiSession taxis, IRac1TaxiPassengerController player)
    {
        var boarding = taxis.Riding!;
        _capturedPassenger = player;
        _activeCab = _cabs[boarding.InstanceIndex];
        _flyingOrigin = _activeCab.Root.GlobalPosition;
        _boardingPosition = FromTaxiPoint(boarding.Position);
        var relative = player.Position - _flyingOrigin;
        var bounded = new Vector3(
            Mathf.Clamp(relative.X, -1.4f, 1.4f),
            Mathf.Clamp(relative.Y, 0.12f, 0.8f),
            Mathf.Clamp(relative.Z, -1.4f, 1.4f));
        // The native attach_frame_set call updates a relative transform
        // frame each tick, not the hero state. While proper frame and
        // animation parenting remain unported, keep Ratchet's boarding
        // offset in the cab's full yaw/pitch/roll frame, not world XYZ.
        _riderFrame = new Rac1TaxiPassengerFrame(
            new Rac1TaxiPoint(bounded.X, bounded.Y, bounded.Z),
            ToTaxiCabBasis(_activeCab.Root.GlobalTransform.Basis));
        // Native hero_cam_target_set enters the scripted walk-to point
        // state 0x65, with braking/settling states 0x66/0x67, not an
        // immediate camera cut. The source-backed state gate is now in
        // Rac1TaxiHeroTargetSession; host walking/animation and camera
        // request ownership are not connected yet. Until then, prevent
        // the recovered TopLevel camera from freezing at the dock.
        _cameraWorldOwned = player.CameraTopLevel;
        _cameraWorldOffset = player.CameraPosition - player.Position;
        // Once boarded, the player is attached by the taxi host. The moving
        // hull must not interfere with the arrival ground-contact ray.
        _cabs[boarding.InstanceIndex].CollisionLayer = 0;
        _previousPlayerMode = player.ProcessMode;
        player.ProcessMode = Node.ProcessModeEnum.Disabled;
        // The native attach frame does not itself hide Ratchet. Keep
        // the character visible during the host provisional ride.
        player.VisualVisible = true;
        GD.Print($"[rac1-taxi] boarded i{boarding.InstanceIndex} -> {boarding.DestinationLabel}");
    }

    private void TickTargetWalk(double delta,
        Rac1MetropolisTaxiSession taxis, IRac1TaxiPassengerController player)
    {
        if (_pendingStop is not { } stop ||
            _heroTarget is not { } target)
            return;
        _walkElapsed += delta;
        var hero = ToTaxiPoint(player.Position);
        if (Math.Abs(hero.Y - target.Target.Y) > 2d)
        {
            // The source marker may sit above unsupported imported taxi
            // geometry. Do not hold input while Ratchet falls from the dock.
            GD.Print($"[rac1-taxi] unsupported target height i{stop.InstanceIndex}; cancelling");
            AbortRide();
            return;
        }
        // Normal controller velocity is in units/second; native state
        // 0x65 admits a planar step in units per 60 Hz updater.
        double speed = new Vector2(player.Velocity.X, player.Velocity.Z).Length() / 60d;
        target.Observe(hero, player.Yaw, speed,
            player.Idle);
        if (target.DepartureAdmitted)
        {
            player.EndWalk();
            _pendingStop = null;
            _heroTarget = null;
            _walkElapsed = 0;
            // If another taxi changed the boarding context, do not
            // attach this player to an unrelated vehicle.
            if (taxis.FindBoardable(hero)?.InstanceIndex == stop.InstanceIndex &&
                taxis.TryBoard(hero))
                AttachPassenger(taxis, player);
            else
            {
                _capturedPassenger = null;
                GD.Print("[rac1-taxi] source dock unavailable at walk-to completion");
            }
        }
        else if (_walkElapsed >= 8d ||
                 !player.Alive || _ports.Dead())
        {
            // The target could be blocked by missing walkable geometry.
            // Never leave player controls or camera captured indefinitely.
            GD.Print($"[rac1-taxi] cancelled blocked target walk i{stop.InstanceIndex}");
            AbortRide();
        }
    }

    internal void Tick(double delta)
    {
        if (_disposed || _session is not { } taxis ||
            (_capturedPassenger ?? _ports.Passenger()) is not { Valid: true } player)
            return;

        TickTargetWalk(delta, taxis, player);

        foreach (var hover in _cabs.Values)
            hover.AdvanceHover(delta);

        // The stationary class-816 hull also receives native bob/tilt
        // updates, despite having no authored passenger path.
        foreach (var stop in taxis.Stops.Where(s => s.OutboundPath.Count == 0 &&
            s.NativeHoverPhases.HasValue))
        {
            if (_cabs.TryGetValue(stop.InstanceIndex, out var parked) && parked.Valid)
                parked.Present(FromTaxiPoint(stop.Position), delta);
        }

        // Host-only first-play pacing: native per-waypoint braking produces
        // a 111s class-1012 round trip, far slower than practical gameplay.
        // Advance the authored route clock only, never HUD/hover/hero time.
        double routeDelta = delta * Rac1TaxiHostPresentationPolicy.RouteClockMultiplier;
        foreach (var traffic in taxis.TickTraffic(ToTaxiPoint(player.Position), routeDelta))
        {
            if (_cabs.TryGetValue(traffic.InstanceIndex, out var instance) && instance.Valid)
            {
                instance.Present(FromTaxiPoint(traffic.Position), routeDelta);
            }
        }

        Rac1TaxiFlight? flight = taxis.Tick(taxis.IsRiding ? routeDelta : delta);
        if (flight is null) return;
        if (_activeCab is not { } taxi)
        {
            AbortRide();
            return;
        }
        if (!taxi.Valid)
        {
            AbortRide();
            return;
        }

        var taxiPosition = taxi.Present(FromTaxiPoint(flight.Position), routeDelta);
        if (_riderFrame is not { } rider)
        {
            AbortRide();
            return;
        }
        var passenger = rider.PositionAt(
            new Rac1TaxiPoint(taxiPosition.X, taxiPosition.Y, taxiPosition.Z),
            ToTaxiCabBasis(taxi.Root.GlobalTransform.Basis));
        player.Position = new Vector3(
            (float)passenger.X, (float)passenger.Y, (float)passenger.Z);
        if (_cameraWorldOwned && player.CameraValid)
            player.CameraPosition = player.Position + _cameraWorldOffset;

        if (!flight.JustArrived) return;

        // Class-816 leaves the taxi at the destination until Ratchet moves
        // 15 native units away, then sends it down its authored exit spline.
        // Class-1012 is still a provisional return shuttle.
        if (flight.From.ApproachPath is not { Count: > 1 })
            taxi.Root.GlobalPosition = _flyingOrigin;
        // Keep the active hull out of the landing ray. Its top surface
        // is not evidence of a permanent walkable destination.
        // A path endpoint is NOT proof of walkable ground. Retail class-1012
        // taxis have additional native path/state segments not yet recovered:
        // two currently imported outbound splines end in mid-air. Never drop
        // the player into space when the path has no safe docking surface.
        var destination = FromTaxiPoint(flight.Arrival);
        bool docked = TryResolveLanding(player, destination, out var exit);
        if (!docked)
        {
            var source = FromTaxiPoint(flight.From.Position);
            if (!TryResolveLanding(player, source, out exit))
                exit = source + Vector3.Up * 0.1f;
            // This is the intentional class-1012 safety path, not a fatal
            // engine error: treat it as a host diagnostic in smoke runs.
            GD.Print($"[rac1-taxi] host landing fallback: route i{flight.From.InstanceIndex} " +
                $"has no supported landing at {destination}; returned to boarding dock");
        }

        if (_cabs.TryGetValue(flight.From.InstanceIndex, out var hull) && hull.Valid)
            hull.CollisionLayer = 1;

        player.ProcessMode = _previousPlayerMode;
        player.VisualVisible = true;
        // RuntimeSpawn is in world coordinates, not mirrored Godot scene space.
        player.Arrive(exit);
        if (_cameraWorldOwned && player.CameraValid)
            player.RestoreCamera();
        _cameraWorldOwned = false;
        GD.Print($"[rac1-taxi] finished i{flight.From.InstanceIndex} at {exit}, " +
            $"destination supported={docked}");
        taxis.CompleteArrival();
        _riderFrame = null;
        _activeCab = null;
        _capturedPassenger = null;
    }

    // Limit dismounts to nearby upward-facing real collision. In particular,
    // never use the old "endpoint + 1.5" fallback: it stranded Ratchet in air.
    private bool TryResolveLanding(
        IRac1TaxiPassengerController player, Vector3 target, out Vector3 exit)
    {
        // A taxi that subsequently flies away cannot support a permanent
        // dismount. Exclude the four mobile class-816/1012 hulls, including
        // inactive cabs whose current meshes overlap a dock probe. However,
        // the authored static class-816 i921 is deliberately a walkable
        // parked platform at the destination and must remain eligible.
        var exclude = new global::Godot.Collections.Array<Rid> { player.BodyRid };
        foreach (var stop in _session?.Stops ?? [])
        {
            if (stop.OutboundPath.Count > 1 &&
                _cabs.TryGetValue(stop.InstanceIndex, out var hull) && hull.Valid)
                exclude.Add(hull.Hull.GetRid());
        }
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
            query.Exclude = exclude;
            var hit = player.World.DirectSpaceState.IntersectRay(query);
            if (!hit.TryGetValue("position", out var contact) ||
                !hit.TryGetValue("normal", out var normal) ||
                normal.AsVector3().Y < 0.70710677f)
                continue;
            Vector3 point = contact.AsVector3();
            if (point.Y < target.Y - 6f || point.Y > target.Y + 3f)
                continue;
            // An unrelated floor several units diagonally from an
            // airborne waypoint is not a taxi dock. Keep the nearest
            // cardinal samples (1.8u) but reject 2.55u diagonal jumps.
            // This is conservative OBP landing safety, NOT a native
            // class-1012 focus or docking constant.
            Vector2 lateral = new(point.X - target.X, point.Z - target.Z);
            if (lateral.LengthSquared() > 2f * 2f)
                continue;
            exit = point + Vector3.Up * 0.1f;
            return true;
        }

        exit = default;
        return false;
    }

    internal string HudLine()
    {
        if (_session is not { } taxis ||
            _ports.Passenger() is not { Valid: true } player) return string.Empty;

        if (_pendingStop is not null) return "Taxi: boarding";
        if (taxis.IsRiding) return "Taxi: in flight";
        var hero = ToTaxiPoint(player.Position);
        var near = taxis.FindBoardable(hero);
        if (near is not null)
        {
            // Class-816 requests retail 0xbc9. Class-1012 selects
            // PVar+0x98/+0x9c after flipping route side (native
            // 0x002dcf34 / 0x002dcbc8), NOT by initial-side parity.
            // Our provisional source dock is at the start of +0x94,
            // the native side-1 path, for both retail class-1012 taxis.
            int? message = near.NativeClassId == 816 ? 0xbc9 :
                near.NativeHudSelectors?.MessageForCurrentSide(1);
            return message is { } id
                ? Rac1RetailUiAssets.Text(id, "Taxi: T / Triangle to ride")
                : "Taxi: T / Triangle to ride";
        }
        var inbound = taxis.Stops.FirstOrDefault(stop =>
            stop.ApproachTrigger?.Contains(hero) == true &&
            taxis.PhaseOf(stop.InstanceIndex) is
                Rac1MetropolisTaxiSession.TrafficPhase.WaitingForHero or
                Rac1MetropolisTaxiSession.TrafficPhase.Approaching);
        return inbound is null ? string.Empty : "Taxi: approaching";

    }

    internal bool ProbeLanding(Vector3 target, out Vector3 exit)
    {
        if (!_disposed && (_capturedPassenger ?? _ports.Passenger()) is { Valid: true } player)
            return TryResolveLanding(player, target, out exit);
        exit = default;
        return false;
    }

    internal ImmutableArray<StaticBody3D> CaptureHulls() =>
        [.. _cabs.Values.Where(cab => cab.Valid).Select(cab => cab.Hull)];

    internal Rac1TaxiHostSnapshot ReadSnapshot()
    {
        var passenger = _capturedPassenger ?? _ports.Passenger();
        System.Numerics.Vector3? expected = null;
        bool orthonormal = false;
        if (_activeCab is { Valid: true } cab)
        {
            var point = cab.Position;
            var basis = ToTaxiCabBasis(cab.Root.GlobalTransform.Basis);
            orthonormal = basis.IsOrthonormal();
            if (_riderFrame is { } rider)
            {
                var target = rider.PositionAt(new(point.X, point.Y, point.Z), basis);
                expected = new((float)target.X, (float)target.Y, (float)target.Z);
            }
        }
        return new(_cabs.Count, _cabs.Count, _cabs.Values.Count(cab => cab.HasHover),
            _session is not null, _session?.IsRiding == true, _session?.IsReturning == true,
            _pendingStop?.InstanceIndex, _heroTarget?.NativeState,
            _activeCab is not null, _riderFrame is not null, _cameraWorldOwned,
            expected, orthonormal,
            passenger is { Valid: true } && _session?.FindBoardable(ToTaxiPoint(passenger.Position)) is not null,
            [.. _cabs.Values.Select(cab => new Rac1TaxiCabSnapshot(
                cab.Stop.NativeClassId, cab.Stop.InstanceIndex,
                new(cab.Position.X, cab.Position.Y, cab.Position.Z),
                new(-cab.Root.GlobalTransform.Basis.X.X, -cab.Root.GlobalTransform.Basis.X.Y, -cab.Root.GlobalTransform.Basis.X.Z),
                _session?.PhaseOf(cab.Stop.InstanceIndex), cab.CollisionLayer))]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { Reset(); }
        finally { _disposed = true; }
    }
}

internal sealed record Rac1TaxiCabSnapshot(
    int NativeClassId, int InstanceIndex, System.Numerics.Vector3 Position,
    System.Numerics.Vector3 Nose, Rac1MetropolisTaxiSession.TrafficPhase? Phase, uint CollisionLayer);

internal sealed record Rac1TaxiHostSnapshot(
    int Nodes, int Hulls, int Hovers, bool SessionPresent,
    bool Riding, bool Returning, int? PendingStop, int? HeroTargetState,
    bool Flying, bool RiderFrame, bool CameraWorldOwned,
    System.Numerics.Vector3? ExpectedPassengerPosition, bool CabBasisOrthonormal,
    bool BoardableAtPlayer, ImmutableArray<Rac1TaxiCabSnapshot> Cabs);