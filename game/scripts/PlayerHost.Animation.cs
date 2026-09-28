using Godot;
using OBP.Core.Math;
using OBP.Godot;
using OBP.Godot.Camera;
using OBP.Godot.Controls;
using OBP.Godot.Player;
using OBP.RAC1.Camera;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Camera;
using OBP.Runtime.Player;

namespace OneBigPackage;

public partial class PlayerHost : CharacterBody3D
{
    private void UpdateAnimationState(bool onFloor)
    {
        // Presentation follows the recovered controller state rather than the
        // retired DebugPlayer tuning. Jump anticipation is already native-air
        // presentation (sequence 7), while crouch states stay visually neutral
        // until a dedicated crouch presentation state is exposed.
        // Ordinary action state 2 keeps its locomotion selector through transient
        // support loss. The retained Veldin edge witness stays on sequence 4 for
        // all 16 unsupported ticks, so only actual jump/fall state may drive the
        // airborne presentation path.
        bool controllerAirborne =
            _rac1Movement.Phase != Rac1RatchetMovementPhase.Grounded &&
            !_rac1Movement.IsOrdinaryEdgeFall;
        bool recoveredSupportForPresentation = UseRac1Gameplay
            ? _rac1PresentationGrounded
            : onFloor;
        bool animationGrounded = recoveredSupportForPresentation && !controllerAirborne;
        bool justLanded = _animationGroundedInitialized && !_animationWasGrounded && animationGrounded;
        _animationGroundedInitialized = true;
        _animationWasGrounded = animationGrounded;

        bool attackRequested = _attackRequested;
        _attackRequested = false;
        float planarSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
        if (_rac1Movement.LocomotionState is Rac1RatchetLocomotionState.Crouched or Rac1RatchetLocomotionState.CrouchTurning)
            planarSpeed = 0f;
        else if (_rac1Movement.LocomotionState == Rac1RatchetLocomotionState.Moving &&
                 _rac1Movement.YawMode == Rac1RatchetYawMode.GroundRun)
            planarSpeed = Math.Max(planarSpeed, (float)PlayerAnimationStateMachine.RunEnterSpeed);

        double verticalPresentation = _rac1Movement.Phase == Rac1RatchetMovementPhase.JumpAnticipation
            ? PlayerAnimationStateMachine.JumpRiseVelocity + 0.01d
            : Velocity.Y;

        PlayerAnimationState previous = AnimationState;
        PlayerAnimationState current = _animationStateMachine.Update(new PlayerAnimationFacts(
            animationGrounded, planarSpeed, verticalPresentation, justLanded, attackRequested));
        if (current != previous)
            GD.Print($"[PlayerHost] animation {previous} -> {current}");
    }
    private void RequestPrimaryAction()
    {
        if (UseRac1Gameplay && !Rac1GameplayAlive)
            return;

        if (UseRac1Gameplay)
        {
            // R&C1 presentation is admitted by the equipped weapon path. In
            // particular, a rejected item-10 request must not play wrench seq 23.
            Rac1PrimaryAttackRequested?.Invoke();
        }
        else
        {
            _attackRequested = true;
            CrateStrikeRequested?.Invoke();
        }
    }

    private void ResetAnimationState()
    {
        _animationStateMachine = new PlayerAnimationStateMachine();
        _animationGroundedInitialized = false;
        _animationWasGrounded = false;
        _attackRequested = false;
    }

    /// <summary>Measure each jump: air time, horizontal distance, apex height, net height change.</summary>
    private void TrackJump(bool onFloor)
    {
        if (!_airborne && !onFloor && Velocity.Y > 0.1f)
        {
            _airborne = true;
            _jumpStart = GlobalPosition;
            _jumpStartTime = _time;
            _jumpApexY = GlobalPosition.Y;
        }
        else if (_airborne)
        {
            _jumpApexY = Mathf.Max(_jumpApexY, GlobalPosition.Y);
            if (onFloor)
            {
                _airborne = false;
                var p = GlobalPosition;
                float horiz = new Vector2(p.X - _jumpStart.X, p.Z - _jumpStart.Z).Length();
                float apex = _jumpApexY - _jumpStart.Y;
                float net = p.Y - _jumpStart.Y;
                _lastJump = $"{_time - _jumpStartTime:0.00}s  {horiz:0.0}u  apex {apex:0.0}u  net {net:+0.0;-0.0;0}u";
                GD.Print($"[PlayerHost] jump landed: {_lastJump}");
            }
        }
    }

}
