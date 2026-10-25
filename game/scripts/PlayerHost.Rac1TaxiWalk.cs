using Godot;

namespace OBP.Game;

public partial class PlayerHost
{
    // Godot adapter for Ratchet's native 0x65 walk-to state. The controller
    // and MoveRac1Contact remain responsible for actual motion and collision.
    // The authored destination and state transitions live in OBP.RAC1.
    private Vector3? _rac1TaxiWalkSceneTarget;
    private double _rac1TaxiWalkHeading;

    internal bool Rac1TaxiWalking => _rac1TaxiWalkSceneTarget.HasValue;

    internal void BeginRac1TaxiWalk(Vector3 sceneTarget, double nativeHeading)
    {
        _rac1TaxiWalkSceneTarget = sceneTarget;
        _rac1TaxiWalkHeading = nativeHeading;
    }

    internal void EndRac1TaxiWalk()
    {
        _rac1TaxiWalkSceneTarget = null;
    }

    private Vector2 Rac1TaxiWalkIntent()
    {
        if (_rac1TaxiWalkSceneTarget is not { } target)
            return Vector2.Zero;
        Vector3 direction = target - GlobalPosition;
        direction.Y = 0;
        float distance = direction.Length();
        if (distance < 0.14f) return Vector2.Zero;
        direction /= distance;

        // The raw stick's forward component is inverted before the
        // PlayerControlIntent applies the existing camera-relative basis.
        // Magnitude is reduced near the point to avoid oscillating across
        // the native 0.2-unit walk-to admission radius.
        var basis = GetRac1PlanarBasis();
        float right = (float)(direction.X * basis.RightX +
                              direction.Z * basis.RightY);
        float forward = (float)(direction.X * basis.ForwardX +
                                direction.Z * basis.ForwardY);
        float strength = Mathf.Clamp(distance / 1.5f, 0.15f, 1f);
        return new Vector2(right, -forward) * strength;
    }

    private void PresentRac1TaxiWalkFacing()
    {
        if (_rac1TaxiWalkSceneTarget is not { } target)
            return;
        Vector3 offset = target - GlobalPosition;
        offset.Y = 0;
        if (offset.LengthSquared() > .2f * .2f) return;
        // The native walk-to state writes +0x164c as facing target once
        // within range. Until the native heading recurrence can drive
        // 0x65 directly, use a bounded host angular correction rather
        // than teleporting Ratchet or bypassing contact motion.
        double change = Math.IEEERemainder(
            _rac1TaxiWalkHeading - Rac1CurrentYaw, 2d * Math.PI);
        if (Math.Abs(change) <= .001) return;
        Rac1CurrentYaw += Math.Clamp(change, -0.12d, 0.12d);
    }

    private void MaskRac1TaxiWalkActions()
    {
        if (!Rac1TaxiWalking) return;
        _liveInput = _liveInput with
        {
            CameraIntent = Vector2.Zero,
            ActionJustPressed = false,
            ActionHeld = false,
            EquippedUseJustPressed = false,
            JumpHeld = false,
            CrouchHeld = false,
            TraversalHeld = false
        };
    }
}
