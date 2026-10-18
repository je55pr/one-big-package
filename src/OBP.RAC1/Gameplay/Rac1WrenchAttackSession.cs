using OBP.RAC1.Player;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Ordinary-axis wrench actions 0x13/14/15. Recovered profile selection, authored
/// time gates and motion remain independent from target/collision adapters.
/// Auto-target yaw and alternate-axis/moving-surface producers are not invented.
/// </summary>
public sealed class Rac1WrenchAttackSession
{
    public readonly record struct Input(
        bool Pressed, bool JumpPressed, bool CrouchHeld, bool Grounded,
        int MovementAction, int MovementAge, float MovementAnimationTime,
        bool HasApexed, float GroundClearance, float FacingYaw,
        float ContactPlanarScalar, float VerticalStep,
        float AnimationTime, bool AnimationBlending, bool ThrowActive, int FlipDirection = 3);

    public readonly record struct Frame(
        bool Active, int NativeAction, int ProfileId, int Age,
        float PlanarX, float PlanarY, float Vertical, float Damage,
        bool HitActive, bool ReleaseWrench, bool JumpCancelled,
        Rac1MovementAnimationSelection? Animation, long Generation, bool WrenchReleased = false);

    private int _pressAge = 1000;
    private int _jumpAge = 1000;
    private int _age;
    private float _scalar;
    private float _vertical;
    private float _facing;
    private bool _released;
    private bool _hasDescended;
    private bool _returnQueued;
    private long _generation;
    private Rac1MovementAnimationSelection? _animation;

    public Rac1WrenchAttackProfile? Profile { get; private set; }
    public bool Active => Profile is not null;
    public long Generation => _generation;
    public int Age => _age;
    public int PressAge => _pressAge;
    public bool Released => _released;

    public Frame Step(Input input)
    {
        if (!float.IsFinite(input.FacingYaw) || !float.IsFinite(input.AnimationTime) ||
            !float.IsFinite(input.MovementAnimationTime) || !float.IsFinite(input.ContactPlanarScalar) ||
            !float.IsFinite(input.VerticalStep) || !float.IsFinite(input.GroundClearance) ||
            input.ContactPlanarScalar < 0 || input.GroundClearance < 0 || input.MovementAge < 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        _pressAge = input.Pressed ? 0 : Math.Min(1000, _pressAge + 1);
        _jumpAge = input.JumpPressed ? 0 : Math.Min(1000, _jumpAge + 1);
        bool entered = false;
        if (Profile is { } previous)
        {
            if (!input.AnimationBlending && input.AnimationTime >= previous.ChainFrom &&
                _pressAge < previous.BufferedPressWindow(input.AnimationTime) && !input.ThrowActive)
            {
                int next = input.CrouchHeld ? 3 : previous.Family == 0 && input.AnimationTime < previous.ChainBefore
                    ? (previous.ComboIndex + 1) % 3 : 0;
                Begin(next, input, retainScalar: true);
                entered = true;
            }
        }
        else if (_pressAge < (input.Grounded ? 7 : input.MovementAction is 0x0b or 0x0e ? 18 : 10) &&
            !input.ThrowActive && SelectAdmission(input) is int admitted)
        {
            Begin(admitted, input, retainScalar: false);
            entered = true;
        }
        if (Profile is not { } profile)
            return default;

        float time = entered ? EntryTime(profile) : input.AnimationTime;
        bool blending = entered || input.AnimationBlending;
        if (!entered && !blending && time > profile.JumpFrom && _jumpAge <
            Math.Max(1, 2 * ((int)MathF.Floor(time) - profile.BufferReference)) && input.Grounded)
        {
            ResetAction();
            _jumpAge = 1000;
            return new(false, 0, profile.Id, _age, 0, 0, 0, 0, false, false, true, null, _generation);
        }
        if (!entered && !blending && time > profile.FinishAfter &&
            (profile.Family != 2 || _released && !input.ThrowActive))
        {
            ResetAction();
            return default;
        }

        float multiplier = 1;
        bool release = false;
        if (profile.Family == 0)
        {
            float speed = profile.ComboIndex switch
            {
                0 => time < 18 ? 4.4f : 0,
                1 => !blending && time >= 9 ? 0 : time > 8 ? 3 : time > 7 ? 4.5f : 5.7f,
                _ => blending || time < 12 ? 3.7f : 0,
            };
            _scalar = Approach(_scalar, speed * Dt, (speed * Dt > _scalar ? 37 : 28) * DtSquared);
            _vertical = input.Grounded ? -54 * DtSquared : _vertical - 25 * DtSquared;
        }
        else if (profile.Family == 1)
        {
            _scalar = Approach(_scalar, 0, 15 * DtSquared);
            if (_age < 9)
            {
                float speed = 7.7f * Dt * (input.GroundClearance > 2.25f ? .5f :
                    input.GroundClearance > 1.8f ? .7f : 1f);
                _vertical = Approach(_vertical, speed, 150 * DtSquared);
            }
            else if (_age > 14)
                _vertical = MathF.Max(-37 * Dt, _vertical - 130 * DtSquared);
            _hasDescended |= _vertical < 0;
            multiplier = !_hasDescended ? .33f : input.Grounded && time < 26 ? 2f : 1f;
            if (input.Grounded && _hasDescended)
                _vertical = 0;
        }
        else
        {
            _scalar = Approach(_scalar, 0, 28 * DtSquared);
            _vertical = input.Grounded ? -54 * DtSquared : _vertical - 25 * DtSquared;
            if (!_released && !blending && time > 33)
                _released = release = true;
            if (_released && !input.ThrowActive && !release && !_returnQueued)
            {
                _animation = _animation!.Value with { ExitLoopFrame = 26 };
                _returnQueued = true;
            }
        }
        _animation = _animation!.Value with { PlaybackMultiplier = multiplier };
        var result = new Frame(true, profile.NativeAction, profile.Id, _age,
            MathF.Cos(_facing) * _scalar, MathF.Sin(_facing) * _scalar, _vertical,
            profile.NativeDamage, profile.Family != 2 && profile.CanHit(time, blending),
            release, false, _animation, _generation, _released);
        _age++;
        return result;
    }

    public static int? SelectAdmission(Input input)
    {
        if (input.Grounded)
            return input.CrouchHeld ? 3 : 0;
        bool allowed = input.MovementAction is 6 or 7 or 8 or 9 or 0x11 or 0x2d ||
            input.MovementAction == 0x0e && input.MovementAnimationTime > 32 ||
            input.MovementAction == 0x0b && input.MovementAnimationTime > (input.FlipDirection == 3 ? 20 : 18);
        // Flip/boost use an 18-update press buffer, not an 18-update action-age gate.
        if (!allowed || input.MovementAge <= 10)
            return null;
        return input.GroundClearance > (input.HasApexed || input.MovementAction == 8 ? .57f : .1f) ? 4 : 0;
    }

    public void Reset()
    {
        ResetAction();
        _pressAge = _jumpAge = 1000;
    }

    private void ResetAction()
    {
        Profile = null;
        _animation = null;
        _scalar = _vertical = 0;
        _age = 0;
        _released = _hasDescended = _returnQueued = false;
    }

    private void Begin(int profileId, Input input, bool retainScalar)
    {
        Profile = Rac1WrenchAttackProfile.Profiles[profileId];
        _scalar = retainScalar ? _scalar : input.ContactPlanarScalar;
        _vertical = input.VerticalStep;
        _facing = input.FacingYaw;
        _age = 0;
        _released = _hasDescended = _returnQueued = false;
        _pressAge = 1000;
        _animation = new(Profile.NativeAction, Profile.BodySequence, ++_generation,
            Profile.EntryFrame, Profile.BlendTicks,
            LoopStartFrame: Profile.Family == 2 ? 6 : null,
            LoopEndFrame: Profile.Family == 2 ? 21 : null);
    }

    private static float EntryTime(Rac1WrenchAttackProfile profile) => profile.Id switch
    {
        0 => 16,
        1 => 7,
        2 => 2,
        3 => 30,
        _ => 19,
    };

    private const float Dt = 1f / 60f;
    private const float DtSquared = 1f / 3600f;
    private static float Approach(float value, float target, float amount) =>
        value + Math.Clamp(target - value, -amount, amount);
}
