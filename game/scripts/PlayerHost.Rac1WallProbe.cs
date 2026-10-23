using Godot;
using OBP.RAC1.Player;

namespace OneBigPackage;

public partial class PlayerHost
{
    /// <summary>
    /// Static-world execution adapter for the recovered wall-jump producer.
    /// Domain and wall-normal history are supplied by the controller. Godot's
    /// triangle rays remain an approximation of native masks and dynamic hits.
    /// </summary>
    private Rac1AirborneWallProbeResult ProbeRac1Wall(Rac1MovementContext context)
    {
        if (_rac1TraversalFloorHeight is not double supportHeight || Rac1Hanging || Rac1SwingshotActive)
            return default;
        int action = _rac1Movement.CurrentNativeMovementAction;
        if (action is not (7 or 9 or 0x11))
            return default;
        var position = ScenePlayerToNative(GlobalPosition);
        const double frequency = Rac1RatchetMovementController.UpdateHz;
        var previousMotion = new Rac1NativeVector3(
            -Velocity.X / frequency, Velocity.Z / frequency, Velocity.Y / frequency);
        var input = new Rac1AirborneWallProbeInput(
            new(position.X, position.Y, position.Z), previousMotion,
            _rac1Movement.VerticalStep, _rac1Yaw.CurrentYaw, context.GroundClearance,
            _rac1Movement.CurrentAirGravityPerUpdate, action,
            _rac1Movement.CurrentMovementDomain, 0, 0,
            // OBP adapter: P+0x255's exact producer is not reproduced. The
            // existing forward ray supplies its bounded static-hit substitute.
            context.ForwardClearance < 4f, context.ForwardCrateFamily,
            Rac1Hanging, _rac1Movement.HasWallJumped, _rac1Movement.PreviousWallNormalYaw);
        return Rac1AirborneWallProbe.Probe(input,
            ray => CastRac1TraversalRay(ray) is { SurfaceClass: >= 0 } hit ? hit : null,
            predicted => ProbeRac1Ledge(supportHeight, predictMotion: false, positionOverride: predicted) is not null);
    }
}
