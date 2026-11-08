using System.Numerics;
using OBP.Core.Math;
using OBP.Runtime.Camera;

namespace OBP.RAC1.Camera;

/// <summary>
/// Native type-5 mode-3 crank camera, followed by the type-0
/// handoff publication blend. One step is one NTSC update, with native Z-up inputs.
/// </summary>
public sealed class Rac1BoltCrankCameraController
{
    public const int MountTicks = 180;
    public const float NovalisReturnRate = .01f;
    private readonly Rac1BoltCrankCameraDefinition _definition;
    private Vector3 _eye, _rotation, _origin, _spherical;
    private Vector3 _releaseEye;
    private Quaternion _releaseRotation;
    private int _mountAge;
    private float _returnFraction;

    public Rac1BoltCrankCameraController(Rac1BoltCrankCameraDefinition definition,
        RuntimeCameraState previous, Vec3 playerNative)
    {
        _definition = definition.Validate();
        previous.Validate();
        _eye = Native(previous.Eye);
        _rotation = Euler(previous);
        _origin = Vector(playerNative);
        _spherical = Spherical(_eye - _origin);
        Current = previous;
    }

    public RuntimeCameraState Current { get; private set; }
    public bool Returning { get; private set; }
    public bool ReturnCompleted { get; private set; }

    public RuntimeCameraState StepMounted(Vec3 playerNative)
    {
        if (Returning) throw new InvalidOperationException("Crank camera is returning to ordinary control.");
        Vector3 player = Vector(playerNative);
        _mountAge = Math.Min(MountTicks, _mountAge + 1);
        if (_mountAge == MountTicks)
        {
            _eye = _definition.EyeNative;
            _rotation = _definition.RotationNative;
        }
        else
        {
            float fraction = (float)_mountAge / MountTicks;
            float eased = Ease(fraction);
            Vector3 target = Spherical(_definition.EyeNative - player);
            float azimuthDelta = Wrap(target.X - _spherical.X);
            float viewDelta = Wrap(_definition.RotationNative.Z - _rotation.Z);
            const float twentyDegrees = .34906584f;
            if (azimuthDelta > twentyDegrees && viewDelta < -twentyDegrees ||
                azimuthDelta < -twentyDegrees && viewDelta > twentyDegrees)
            {
                float longDelta = target.X - _spherical.X;
                if (longDelta < 0 && viewDelta > 0 || longDelta > 0 && viewDelta < 0) longDelta = -longDelta;
                _spherical.X += longDelta * eased;
            }
            else _spherical.X = Angle(_spherical.X, target.X, eased);
            _spherical.Y = Angle(_spherical.Y, target.Y, eased);
            float start = _definition.RadiusStartReference;
            float end = _definition.RadiusEndReference;
            // Native cubic through the authored slope references and values 0,1.
            float cubic = end - 1 - start;
            float quadratic = start - cubic;
            float radiusWeight = cubic * fraction * fraction * fraction +
                quadratic * fraction * fraction + (1 - start) * fraction;
            _spherical.Z += (target.Z - _spherical.Z) * radiusWeight;
            _origin = Vector3.Lerp(_origin, player, eased);
            _eye = _origin + new Vector3(MathF.Cos(_spherical.Y) * MathF.Cos(_spherical.X),
                MathF.Cos(_spherical.Y) * MathF.Sin(_spherical.X), MathF.Sin(_spherical.Y)) * _spherical.Z;
            _rotation = new(Angle(_rotation.X, _definition.RotationNative.X, eased),
                Angle(_rotation.Y, _definition.RotationNative.Y, eased),
                Angle(_rotation.Z, _definition.RotationNative.Z, eased));
        }
        return Current = Publish(_eye, Rotation(_rotation), player);
    }

    public void BeginReturn()
    {
        if (Returning) return;
        Returning = true;
        _releaseEye = _eye;
        _releaseRotation = Rotation(_rotation);
        _returnFraction = 0;
    }

    public RuntimeCameraState StepReturn(RuntimeCameraState follow, Vec3 playerNative, Vec3 platformCarry = default)
    {
        if (!Returning) throw new InvalidOperationException("Crank camera has not begun its follow handoff.");
        follow.Validate();
        _releaseEye += Vector(platformCarry);
        float amount = Ease(_returnFraction);
        Vector3 eye = Vector3.Lerp(_releaseEye, Native(follow.Eye), amount);
        Quaternion rotation = Quaternion.Lerp(_releaseRotation, Rotation(Euler(follow)), amount);
        Current = Publish(eye, rotation, Vector(playerNative));
        if (_returnFraction >= 1) ReturnCompleted = true;
        _returnFraction = MathF.Min(1, _returnFraction + _definition.ReturnBlendRate);
        return Current;
    }

    internal static Vector3 Native(Vec3 value) => Vector(new(value.X, value.Z, value.Y));
    private static Vector3 Vector(Vec3 value)
    {
        var result = new Vector3((float)value.X, (float)value.Y, (float)value.Z);
        if (!Rac1BoltCrankCameraDefinition.Finite(result)) throw new ArgumentOutOfRangeException(nameof(value));
        return result;
    }

    private static Vector3 Spherical(Vector3 offset) =>
        new(MathF.Atan2(offset.Y, offset.X), MathF.Atan2(offset.Z, new Vector2(offset.X, offset.Y).Length()), offset.Length());
    private static float Wrap(float angle) => (float)Rac1CameraRecurrence.WrapPi(angle);
    private static float Ease(float fraction) => (1 - MathF.Cos(fraction * MathF.PI)) * .5f;
    private static float Angle(float from, float to, float weight) => Wrap(from + Wrap(to - from) * weight);
    private static Quaternion Rotation(Vector3 euler) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, euler.Z) *
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, euler.Y) *
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, euler.X);

    private static Vector3 Euler(RuntimeCameraState state)
    {
        Vector3 forward = Native(state.Forward);
        if (forward.LengthSquared() <= 1e-12f)
            throw new ArgumentException("Camera forward must be non-zero.", nameof(state));
        forward = Vector3.Normalize(forward);
        float pitch = MathF.Atan2(-forward.Z, new Vector2(forward.X, forward.Y).Length());
        float roll = 0;
        if (state.Up is { } up)
        {
            Vector3 right = Vector3.Cross(Native(up), forward);
            if (right.LengthSquared() <= 1e-12f)
                throw new ArgumentException("Camera up must be independent of forward.", nameof(state));
            right = Vector3.Normalize(right);
            Vector3 nativeUp = Vector3.Cross(forward, right);
            roll = MathF.Atan2(right.Z, nativeUp.Z);
        }
        return new(roll, pitch, MathF.Atan2(forward.Y, forward.X));
    }

    private static RuntimeCameraState Publish(Vector3 eye, Quaternion rotation, Vector3 player)
    {
        Vector3 forward = Vector3.Transform(Vector3.UnitX, rotation);
        Vector3 up = Vector3.Transform(Vector3.UnitZ, rotation);
        float distance = Vector3.Distance(eye, player);
        return new RuntimeCameraState(new(eye.X, eye.Z, eye.Y), new(forward.X, forward.Z, forward.Y),
            MathF.Atan2(forward.Y, forward.X), distance, distance)
        {
            Up = new(up.X, up.Z, up.Y),
            VerticalFieldOfViewRadians = Rac1CameraProjection.VerticalFieldOfViewRadians,
        }.Validate();
    }
}
