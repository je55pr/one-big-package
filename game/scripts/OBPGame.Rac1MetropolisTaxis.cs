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

        _rac1FlyingTaxi = null;
        _rac1MetropolisTaxis?.Reset();
        _rac1MetropolisTaxis = null;
        _rac1TaxiNodes.Clear();
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
        // Taxi dock is an authored Moby centre, not necessarily a safe foot
        // position. Resolve an exit point against current retail collision.
        var destination = FromTaxiPoint(flight.Arrival);
        var query = PhysicsRayQueryParameters3D.Create(
            destination + Vector3.Up * 8f,
            destination + Vector3.Down * 35f);
        query.Exclude = [player.GetRid()];
        var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        var exit = hit.Count != 0
            ? (Vector3)hit["position"] + Vector3.Up * 0.05f
            : destination + Vector3.Up * 1.5f;

        player.ProcessMode = _rac1TaxiPreviousPlayerMode;
        player.VisualRoot.Visible = true;
        // RuntimeSpawn is in world coordinates, not mirrored Godot scene space.
        player.ApplyRac1TaxiArrival(new RuntimeSpawn(
            -exit.X, exit.Y, exit.Z, player.Rac1CurrentYaw));
        GD.Print($"[rac1-taxi] arrived at {flight.From.DestinationLabel} ({exit})");
        taxis.CompleteArrival();
        _rac1FlyingTaxi = null;
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
