using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Player;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Class-280 turning and animation rules, Novalis NTSC-U updater 0x002e0c68.
/// Host support/collision and engagement observations are separate from this controller.
/// </summary>
public sealed class Rac1BoltCrankSession
{
    public const int NativeAction = Rac1PlayerActionDomain.BoltCrank;
    public const int InsertSequence = 0x44;
    public const int IdleSequence = 0x3f;
    public const int SlowSequence = 0x40;
    public const int FastSequence = 0x41;
    public const int WrenchSequence = 0x0b;
    public const float Reach = 1.75f;
    public const float Radius = 1.05f;
    private const float Dt = 1f / 60f;
    private const float Acceleration = Dt * Dt * 5.5f;
    private const float MaximumSpeed = Dt * 3.7f;
    private readonly float _turns;
    private float _speed, _turned, _radialVelocity;
    private int _age, _idleTicks;
    private int _squarePressAge = 7;
    private long _generation;
    private Rac1MovementAnimationSelection _animation;

    public Rac1BoltCrankSession(ReadOnlySpan<byte> pvar, float initialYaw)
    {
        if (pvar.Length != 0x40 || !float.IsFinite(initialYaw))
            throw new InvalidDataException("Class-280 crank requires its 64-byte PVar and finite yaw.");
        _turns = BinaryPrimitives.ReadSingleLittleEndian(pvar.Slice(0x30));
        int reusable = BinaryPrimitives.ReadInt32LittleEndian(pvar.Slice(0x38));
        if (!float.IsFinite(_turns) || _turns <= 0 || reusable is not (0 or 1))
            throw new InvalidDataException("Unsupported crank turn count or reuse flag.");
        if (reusable != 0)
            throw new NotSupportedException("Reusable crank callbacks are outside the admitted Novalis crank pair.");
        Yaw = initialYaw;
    }

    public bool Active { get; private set; }
    public float Progress { get; private set; }
    public float Yaw { get; private set; }
    public float Lowering { get; private set; }
    // TickIdle clamps the native height offset to exact zero; this is a state sentinel.
    public bool CanEngage => !Active && Progress < 1 && _idleTicks > 30 && Lowering == 0;
    public bool SquareBuffered => _squarePressAge < 7;

    public void ObserveSquare(bool pressed) => _squarePressAge = pressed ? 0 : Math.Min(7, _squarePressAge + 1);

    /// <summary>Native distance, facing and hero-origin height gates; the caller supplies held-wrench/support facts.</summary>
    public bool CanEngageFrom(Vector3 center, Vector3 player, float facingYaw)
    {
        if (!CanEngage || !float.IsFinite(facingYaw) ||
            !Finite(new(center.X, center.Y)) || !Finite(new(player.X, player.Y)) ||
            !float.IsFinite(center.Z) || !float.IsFinite(player.Z)) return false;
        float targetYaw = MathF.Atan2(center.Y - player.Y, center.X - player.X);
        return Vector3.Distance(center, player) < Reach &&
            MathF.Abs(Wrap(targetYaw - facingYaw)) < MathF.PI / 2 &&
            MathF.Abs(player.Z - .3f - center.Z) < .2f;
    }

    /// <summary>Native right/forward input rotated by the supplied camera/control heading.</summary>
    public static Vector2 ControlRelativeStick(Vector2 stick, float heading)
    {
        if (!Finite(stick) || !float.IsFinite(heading)) throw new ArgumentOutOfRangeException(nameof(stick));
        return new(MathF.Cos(heading) * -stick.Y + MathF.Sin(heading) * stick.X,
            MathF.Sin(heading) * -stick.Y - MathF.Cos(heading) * stick.X);
    }

    public void TickIdle()
    {
        if (Active) return;
        if (Progress >= 1)
        {
            Lowering = MathF.Min(.5f, Lowering + Dt * .5f);
            return;
        }
        _idleTicks++;
        Progress = MathF.Max(0, Progress - 1f / 60f);
        if (Progress > 0) Yaw = Wrap(Yaw - _turns * MathF.Tau / 60f);
        Lowering = MathF.Max(0, Lowering - Dt * .5f);
    }

    public bool TryEngage()
    {
        if (!CanEngage) return false;
        Active = true;
        _age = _idleTicks = 0;
        // Native entry preserves PVar +0x1c speed; neutral re-entry decelerates it normally.
        _radialVelocity = 0;
        _turned = Progress * (_turns * MathF.Tau);
        _animation = new(NativeAction, InsertSequence, ++_generation, BlendTicks: 8);
        return true;
    }

    public void Release() => Active = false;

    public readonly record struct Frame(Vector2 PlayerPosition, float PlayerYaw,
        Rac1MovementAnimationSelection Animation);

    /// <summary>One native 60 Hz update. Stick is already in native world XY.</summary>
    public Frame Step(Vector2 center, Vector2 position, float facingYaw, Vector2 stick,
        bool squarePressed, bool animationCompleted, bool animationBlending)
    {
        if (!Active) throw new InvalidOperationException("Crank is not engaged.");
        if (!Finite(center) || !Finite(position) || !Finite(stick) || !float.IsFinite(facingYaw))
            throw new ArgumentOutOfRangeException(nameof(position));
        if (Progress >= 1 || squarePressed && _age > 60)
        {
            Release();
            return new(position, facingYaw, _animation);
        }
        _age++;
        if (stick.LengthSquared() > 1) stick = Vector2.Normalize(stick);
        Vector2 facing = new(MathF.Cos(facingYaw), MathF.Sin(facingYaw));
        float demand = Vector2.Dot(facing, stick);
        _speed = Math.Clamp(_speed + Math.Clamp(demand - _speed, -Acceleration, Acceleration), 0, MaximumSpeed);
        if (_speed < Acceleration * .25f) _speed = 0;

        if (_animation.Sequence == InsertSequence)
        {
            if (animationCompleted) Select(IdleSequence, 7);
        }
        else if (!animationBlending)
        {
            switch (_animation.Sequence)
            {
                case SlowSequence when _speed == 0: Select(IdleSequence, 14); break;
                case IdleSequence when _speed > 0: Select(SlowSequence, 14); break;
                case SlowSequence when _speed > MaximumSpeed * .6f: Select(FastSequence, 14); break;
                case FastSequence when _speed < MaximumSpeed * .4f: Select(SlowSequence, 16); break;
                case IdleSequence or SlowSequence or FastSequence: break;
                default: throw new InvalidOperationException("Unsupported crank animation sequence.");
            }
        }

        float oldAngle = MathF.Atan2(position.Y - center.Y, position.X - center.X);
        position += facing * _speed;
        float angle = MathF.Atan2(position.Y - center.Y, position.X - center.X);
        Vector2 target = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Radius;
        Vector2 correction = target - position;
        float distance = correction.Length();
        // Native scalar spring 0x25b8c0, with spring position = -distance.
        _radialVelocity += .015f * distance - .3f * _radialVelocity;
        _radialVelocity = Math.Clamp(_radialVelocity, -MathF.Min(Dt * 7, distance), MathF.Min(Dt * 7, distance));
        if (distance > 0) position += correction / distance * _radialVelocity;
        facingYaw = ApproachAngle(facingYaw, angle + MathF.PI / 2, Dt * MathF.Tau);
        if (_animation.Sequence != InsertSequence)
        {
            float handleYaw = Wrap(angle + MathF.PI / 12);
            Yaw = ApproachAngle(Yaw, handleYaw, Dt * MathF.PI);
            float difference = Wrap(Yaw - handleYaw);
            if (difference > MathF.PI / 3) Yaw = Wrap(Yaw - MathF.PI / 3);
            else if (difference < -MathF.PI / 3) Yaw = Wrap(Yaw + MathF.PI / 3);
        }
        _turned += MathF.Abs(Wrap(angle - oldAngle));
        Progress = Math.Clamp(_turned / (_turns * MathF.Tau), 0, 1);
        return new(position, facingYaw, _animation);
    }

    private void Select(int sequence, int blendTicks) =>
        _animation = new(NativeAction, sequence, _generation, BlendTicks: blendTicks, Loop: true);

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static float Wrap(float value) => (float)Rac1RatchetYawController.WrapPi(value);
    private static float ApproachAngle(float value, float target, float maximum) =>
        Wrap(value + Math.Clamp(Wrap(target - value), -maximum, maximum));
}
