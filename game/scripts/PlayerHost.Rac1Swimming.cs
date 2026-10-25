using Godot;
using OBP.RAC1.Player;

namespace OBP.Game;

public partial class PlayerHost
{
    public Func<Vector3, float?>? Rac1WaterSurfaceHeight { get; set; }
    public event Action? Rac1NovalisDrowned;
    public event Action<int>? Rac1WaterSoundRequested;

    private readonly Rac1WaterMovementSession _rac1Swimming = new();
    private bool _rac1SwimActive;
    private bool _rac1SwimDrowningReported;
    private bool _rac1WaterSquareHeld, _rac1WaterCrouchHeld;
    private Rac1SwimSnapshot _rac1SwimState;
    private Rac1WaterMovementResult _rac1SwimMotion;
    private Vector3? _rac1WaterContactStart;
    private Rac1NativeVector3? _rac1WaterContactMotion;

    public bool Rac1SwimmingActive => _rac1SwimActive;
    public Rac1SwimSnapshot? Rac1SwimState => _rac1SwimActive ? _rac1SwimState : null;
    public Rac1WaterAnimationFacts Rac1WaterAnimationFacts { get; set; }
    // Native swimming predicate20d568 and equipment-tail hide flag20ae;
    // remember the held item while suppressing its water presentation.
    public bool Rac1WaterHandItemHidden => _rac1SwimActive || _rac1Movement.TraversalAction == 0x12;

    private bool TryStepRac1Swimming(Vector2 move, bool ascendHeld, bool hydroHeld)
    {
        bool wasActive = _rac1SwimActive;
        float? surface = Rac1WaterSurfaceHeight?.Invoke(GlobalPosition);
        var contact = surface is float height ? ProbeRac1WaterContact(height) : default;
        var analogue = Rac1AnalogueInput.ConditionUnitAxes(move.X, -move.Y);
        double controlYaw = GetRac1ControlYaw();
        float targetYaw = analogue.IsActive ? (float)Rac1RatchetYawController.BuildMovementTarget(
            analogue.X, analogue.Y, controlYaw) : (float)_rac1Yaw.CurrentYaw;
        bool square = _liveInput.ActionHeld;
        var primaryMotion = _rac1WaterContactMotion ?? new Rac1NativeVector3(
            -Velocity.X / 60f, Velocity.Z / 60f, Velocity.Y / 60f);
        _rac1SwimMotion = _rac1Swimming.Step(new(
            contact, GlobalPosition.Y, primaryMotion, Velocity.Y / 60f,
            (float)_rac1Yaw.CurrentYaw, targetYaw, (float)analogue.Magnitude,
            CrossHeld: ascendHeld, CrossPressed: ascendHeld && !_rac1JumpWasHeld,
            SquareHeld: square, SquarePressed: square && !_rac1WaterSquareHeld,
            CrouchHeld: hydroHeld, CrouchPressed: hydroHeld && !_rac1WaterCrouchHeld,
            OwnsHydroPack: Rac1MovementEquipment.HydroPack,
            OwnsOxygenMask: Rac1MovementEquipment.OxygenMask,
            MovementDomain: Rac1Hanging ? 3 : _rac1Movement.CurrentMovementDomain,
            MovementAction: _rac1Movement.CurrentNativeMovementAction,
            HasApexed: _rac1Movement.MayReenterWater &&
                _rac1Movement.Phase != Rac1RatchetMovementPhase.Rising &&
                _rac1Movement.Phase != Rac1RatchetMovementPhase.JumpAnticipation,
            Animation: Rac1WaterAnimationFacts));
        _rac1WaterSquareHeld = square;
        _rac1WaterCrouchHeld = hydroHeld;
        _rac1WaterContactMotion = null;
        _rac1SwimActive = _rac1SwimMotion.Active;
        _rac1SwimState = _rac1SwimMotion.State;
        if (_rac1SwimMotion.Sound is int sound) Rac1WaterSoundRequested?.Invoke(sound);
        if (!_rac1SwimActive)
        {
            if (wasActive)
            {
                _rac1Movement.ResumeAfterTraversal(-primaryMotion.X, primaryMotion.Y, primaryMotion.Z);
                Velocity = new Vector3(-(float)primaryMotion.X, (float)primaryMotion.Z, (float)primaryMotion.Y) * 60f;
                _rac1SwimDrowningReported = false;
                UpdateRac1FacingPresentation();
                if (_rac1SwimMotion.Exit == Rac1WaterExit.Jump)
                    _rac1Movement.BeginWaterExitJump(ProbeRac1MovementContext(controlYaw), contact.Depth);
            }
            return false;
        }

        if (!wasActive)
        {
            _rac1Movement.ResumeAfterTraversal(Velocity.X / 60d, Velocity.Z / 60d, Velocity.Y / 60d);
            ResetRac1LedgeTraversal();
            ResetRac1Tether();
            ResetRac1SurfaceMotion();
        }
        _rac1SmallStepEligible = false;
        _rac1PresentationGrounded = false;
        ResetRac1WrenchAttack();
        _rac1Yaw.Reset(_rac1SwimMotion.Yaw);
        UpdateRac1FacingPresentation();
        var request = _rac1SwimMotion.Request;
        Velocity = new Vector3(-(float)request.X, (float)request.Z, (float)request.Y) * 60f;
        _rac1WaterContactStart = GlobalPosition;
        _locomotionAnimationInput = new Rac1LocomotionAnimationInput(
            Active: false, HelperAdmitted: false, ScalarAtSelection: 0,
            Traversal: _rac1SwimMotion.Animation);
        if (_rac1SwimMotion.DeathAnimationCompleted && !_rac1SwimDrowningReported)
        {
            _rac1SwimDrowningReported = true;
            Rac1NovalisDrowned?.Invoke();
        }
        return true;
    }

    private Rac1WaterContact ProbeRac1WaterContact(float waterY)
    {
        // Native floor query starts above the feet and reaches down 48 units;
        // Godot's filtered collision approximates native query-mask membership.
        var query = PhysicsRayQueryParameters3D.Create(
            GlobalPosition + Vector3.Up * .99f,
            new(GlobalPosition.X, Math.Max(0, GlobalPosition.Y - 48), GlobalPosition.Z));
        query.Exclude = [GetRid()];
        var space = GetWorld3D().DirectSpaceState;
        var floor = space.IntersectRay(query);
        float floorY = floor.TryGetValue("position", out var point) ? point.AsVector3().Y : 0;
        if (floorY >= waterY)
            return default;
        bool underfoot = waterY <= GlobalPosition.Y + .99f;
        if (!underfoot)
        {
            // The native periodic submerged ray can find water above the
            // ordinary floor ray. A solid ceiling in between must occlude it.
            var submergedQuery = PhysicsRayQueryParameters3D.Create(
                GlobalPosition + Vector3.Up * .99f,
                new(GlobalPosition.X, waterY - .01f, GlobalPosition.Z));
            submergedQuery.Exclude = query.Exclude;
            if (space.IntersectRay(submergedQuery).Count != 0) return default;
        }
        var ceilingQuery = PhysicsRayQueryParameters3D.Create(
            new(GlobalPosition.X, waterY + .01f, GlobalPosition.Z),
            new(GlobalPosition.X, waterY + .31f, GlobalPosition.Z));
        ceilingQuery.Exclude = query.Exclude;
        bool ceilingBlocked = space.IntersectRay(ceilingQuery).Count != 0;
        return new(true, waterY, floorY, ceilingBlocked, underfoot);
    }

    private void RecordRac1WaterMotion()
    {
        if (_rac1WaterContactStart is not Vector3 start) return;
        Vector3 displacement = GlobalPosition - start;
        // Surface bobbing is a native position write, not P+e8 velocity.
        // Do not carry that placement correction into a water-exit impulse.
        _rac1WaterContactMotion = new(-displacement.X, displacement.Z,
            _rac1SwimState.NativeDomain == 18 ? 0 : displacement.Y);
        _rac1WaterContactStart = null;
    }

    private void ResetRac1WaterMotion()
    {
        _rac1SwimActive = _rac1SwimDrowningReported = false;
        _rac1WaterSquareHeld = _rac1WaterCrouchHeld = false;
        _rac1Swimming.Reset();
        _rac1SwimMotion = default;
        _rac1WaterContactStart = null;
        _rac1WaterContactMotion = null;
        Rac1WaterAnimationFacts = default;
    }
}
