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
    private RuntimeWorldScene.DynamicObjectNode? _rac1FlyingTaxi;
    private Vector3 _rac1FlyingTaxiOrigin;
    private Node.ProcessModeEnum _rac1TaxiPreviousPlayerMode;

    private void ResetRac1MetropolisTaxis()
    {
        if (_player is { } player && IsInstanceValid(player) && _rac1FlyingTaxi is not null)
        {
            player.VisualRoot.Visible = true;
            player.ProcessMode = _rac1TaxiPreviousPlayerMode;
        }
        if (_rac1FlyingTaxi is { } oldTaxi && IsInstanceValid(oldTaxi.Root))
            oldTaxi.Root.GlobalPosition = _rac1FlyingTaxiOrigin;

        if (_rac1MetropolisTaxis?.Riding is { } riding &&
            _rac1TaxiCollisionBodies.TryGetValue(riding.InstanceIndex, out var activeHull) &&
            IsInstanceValid(activeHull))
            activeHull.CollisionLayer = 1;

        _rac1FlyingTaxi = null;
        _rac1MetropolisTaxis?.Reset();
        _rac1MetropolisTaxis = null;
        _rac1TaxiNodes.Clear();
        _rac1TaxiCollisionBodies.Clear();
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
                _rac1TaxiNodes.Clear();
                return;
            }
            _rac1TaxiNodes.Add(stop.InstanceIndex, node);
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
                _rac1TaxiNodes.Clear();
                _rac1TaxiCollisionBodies.Clear();
                return;
            }
            node.Root.AddChild(body);
            _rac1TaxiCollisionBodies.Add(stop.InstanceIndex, body);
        }

        GD.Print($"[rac1-taxi] Kerwan: {_rac1TaxiNodes.Count} authored models, " +
            $"{taxis.Stops.Count(s => s.OutboundPath.Count > 1)} source-backed outbound routes (T / Triangle)");

    }

    // The runtime uses Y-up coordinates, while Godot mirrors the X-axis.
    private static Rac1TaxiPoint ToTaxiPoint(Vector3 scenePosition) =>
        new(-scenePosition.X, scenePosition.Y, scenePosition.Z);

    private static Vector3 FromTaxiPoint(Rac1TaxiPoint position) =>
        RuntimeWorldScene.ToScene(position.X, position.Y, position.Z);

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
            _rac1Nanotech.Probe().IsDead || _args.CaptureFrame is not null)
            return false;

        if (!taxis.TryBoard(ToTaxiPoint(player.GlobalPosition)))
            return false;

        var boarding = taxis.Riding!;
        _rac1FlyingTaxi = _rac1TaxiNodes[boarding.InstanceIndex];
        _rac1FlyingTaxiOrigin = _rac1FlyingTaxi.Root.GlobalPosition;
        // Once boarded, the player is attached by the taxi host. The moving
        // hull must not interfere with the arrival ground-contact ray.
        _rac1TaxiCollisionBodies[boarding.InstanceIndex].CollisionLayer = 0;
        _rac1TaxiPreviousPlayerMode = player.ProcessMode;
        player.ProcessMode = Node.ProcessModeEnum.Disabled;
        player.VisualRoot.Visible = false;
        GD.Print($"[rac1-taxi] boarded i{boarding.InstanceIndex} -> {boarding.DestinationLabel}");
        return true;
    }

    private void TickRac1MetropolisTaxis(double delta)
    {
        if (_rac1MetropolisTaxis is not { } taxis ||
            _player is not { } player || !IsInstanceValid(player))
            return;

        Rac1TaxiFlight? flight = taxis.Tick(delta);
        if (flight is null || _rac1FlyingTaxi is not { } taxi) return;
        if (!IsInstanceValid(taxi.Root))
        {
            ResetRac1MetropolisTaxis();
            return;
        }

        var taxiPosition = FromTaxiPoint(flight.Position);
        taxi.Root.GlobalPosition = taxiPosition;
        player.GlobalPosition = taxiPosition + Vector3.Up * 0.5f;

        if (!flight.JustArrived) return;

        taxi.Root.GlobalPosition = _rac1FlyingTaxiOrigin;
        _rac1TaxiCollisionBodies[flight.From.InstanceIndex].CollisionLayer = 1;

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
            GD.PrintErr($"[rac1-taxi] route i{flight.From.InstanceIndex} has no " +
                $"supported landing at {destination}; returned to boarding dock");
        }

        player.ProcessMode = _rac1TaxiPreviousPlayerMode;
        player.VisualRoot.Visible = true;
        // RuntimeSpawn is in world coordinates, not mirrored Godot scene space.
        player.ApplyRac1TaxiArrival(new RuntimeSpawn(
            -exit.X, exit.Y, exit.Z, player.Rac1CurrentYaw));
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

        if (taxis.IsRiding) return "Taxi: flying...";
        var near = taxis.FindBoardable(ToTaxiPoint(player.GlobalPosition));
        return near is null ? string.Empty :
            $"Taxi: T / Triangle to board";
    }
}
