using Godot;
using OBP.RAC1.Player;

namespace OneBigPackage;

public partial class PlayerHost
{
    // Installed only for Novalis by the RAC1 gameplay owner. Scene X is
    // mirrored from the native decoded surface lookup at the integration edge.
    public Func<Vector3, float?>? Rac1WaterSurfaceHeight { get; set; }
    public event Action? Rac1NovalisDrowned;

    private readonly Rac1SwimmingSession _rac1Swimming = new();
    private bool _rac1SwimActive;
    private bool _rac1SwimDrowningReported;
    private Rac1SwimSnapshot _rac1SwimState;
    private float _rac1LastSurfaceHeight;

    public bool Rac1SwimmingActive => _rac1SwimActive;
    public Rac1SwimSnapshot? Rac1SwimState => _rac1SwimActive ? _rac1SwimState : null;

    private bool TryStepRac1Swimming(Vector2 move, bool ascendHeld, bool hydroHeld)
    {
        float? surfaceHeight = Rac1WaterSurfaceHeight?.Invoke(GlobalPosition);
        if (surfaceHeight is not float waterY ||
            GlobalPosition.Y > waterY + .35f ||
            GlobalPosition.Y < waterY - 20f)
        {
            if (_rac1SwimActive)
            {
                _rac1SwimActive = false;
                _rac1Swimming.Reset();
                _rac1SwimDrowningReported = false;
                _rac1Movement.ResumeAfterTraversal(
                    Velocity.X / 60d, Velocity.Z / 60d, Velocity.Y / 60d);
            }
            return false;
        }

        bool underwater = GlobalPosition.Y < waterY - .55f;
        // Surface membership is sourced from the authored class-zero triangle
        // footprint, not a fabricated axis-aligned lake box. The transition
        // depths and free-swim velocities below remain OBP host approximations.
        bool diveHeld = _liveInput.ActionHeld;
        var domain = underwater ? Rac1WaterDomain.Underwater : Rac1WaterDomain.Surface;
        bool moving = move.LengthSquared() > .005f;
        _rac1SwimState = _rac1Swimming.Step(new Rac1SwimInput(
            domain, moving, DivePressed: diveHeld, AscendPressed: ascendHeld,
            DiveAdmitted: underwater, SurfaceAdmitted: !underwater,
            OwnsHydroPack: false, HydroRequested: hydroHeld,
            OwnsOxygenMask: false,
            NativeDrowningGuardSatisfied: underwater && GlobalPosition.Y < waterY - 1f));
        _rac1LastSurfaceHeight = waterY;
        _rac1SwimActive = true;
        _rac1SmallStepEligible = false;
        _rac1PresentationGrounded = false;
        ResetRac1WrenchAttack();

        if (_rac1SwimState.Drowning)
        {
            Velocity = Vector3.Zero;
            if (!_rac1SwimDrowningReported)
            {
                _rac1SwimDrowningReported = true;
                Rac1NovalisDrowned?.Invoke();
            }
            return true;
        }

        Vector3 forward = -Camera.GlobalBasis.Z;
        forward.Y = 0;
        forward = forward.LengthSquared() > 1e-5f ? forward.Normalized() : Vector3.Forward;
        Vector3 right = new Vector3(-forward.Z, 0f, forward.X);
        Vector3 planar = right * move.X - forward * move.Y;
        if (planar.LengthSquared() > 1f) planar = planar.Normalized();
        float speed = underwater ? 3.1f : 3.8f;
        float rise = underwater
            ? (ascendHeld ? 2.8f : diveHeld ? -2.8f : 0f)
            : (diveHeld ? -2.8f : Mathf.Clamp((waterY - GlobalPosition.Y) * 5f, -2f, 2f));
        Velocity = planar * speed + Vector3.Up * rise;
        if (planar.LengthSquared() > .005f)
            _rac1Yaw.Step(move.X, -move.Y, GetRac1ControlYaw(),
                Rac1RatchetYawMode.GroundRun);
        UpdateRac1FacingPresentation();
        _locomotionAnimationInput = new Rac1LocomotionAnimationInput(
            Active: false, HelperAdmitted: false, ScalarAtSelection: 0,
            Traversal: _rac1SwimState.NativeSequence is int sequence
                ? new(_rac1SwimState.NativeAction, sequence, 0, 0, 10, Loop: true)
                : null);
        return true;
    }
}
