using System.Numerics;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Bounded static-world pickup motion from loaded Oozla 0x376D58/0x378270
/// and reward emitter 0x3177C8. Vectors are native Z-up, per 60 Hz tick.
/// The host supplies contact, player attraction dimensions, target socket and
/// random samples. Moving supports, camera-side steering and sparkles remain
/// unavailable; host randomness does not reproduce the retail global call order.
/// </summary>
public sealed class Rac2BoltMotionSession
{
    public static float GravityPerTick => .003f;
    public static float ContactRadius => .25f;
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public int NativeState { get; private set; } = 1;
    public bool Collected { get; private set; }
    public Quaternion Orientation { get; private set; } = Quaternion.Identity;
    private readonly Func<float, float, float> _sample;
    private readonly float _launchHeight;
    private readonly int _nativeClass;
    private readonly bool _clockwise;
    private float _angleX, _angleZ;
    private Quaternion _spin, _groundBlend;
    private Quaternion _surface = Quaternion.Identity;
    private Vector3 _rest;
    private int _hopLength, _hopRemaining;
    private float _acceleration;
    private bool _activated;

    public Rac2BoltMotionSession(int nativeClass, Vector3 origin, Func<float, float, float> sample)
    {
        if (nativeClass is < 13 or > 19) throw new ArgumentOutOfRangeException(nameof(nativeClass));
        _nativeClass = nativeClass;
        _sample = sample ?? throw new ArgumentNullException(nameof(sample));
        float angle = sample(-MathF.PI, MathF.PI);
        Velocity = new(MathF.Cos(angle) * sample(0, 3) / 60, MathF.Sin(angle) * sample(0, 3) / 60,
            sample(3.7f, 6) / 60);
        Vector3 offset = new(Velocity.X, Velocity.Y, 0);
        if (offset.LengthSquared() > 0) offset = Vector3.Normalize(offset) * .5f;
        Position = origin + offset;
        _launchHeight = Position.Z;
        // 0x37850C..0x3785A8: one quaternion increment about the launch
        // velocity/owner-offset cross product, not independent Euler spins.
        _clockwise = sample(0, 2) >= 1;
        _angleX = sample(-MathF.PI, MathF.PI);
        _angleZ = sample(-MathF.PI, MathF.PI);
        _spin = AxisSpin(Vector3.Cross(Velocity, origin - Position), sample(0, 720) * MathF.PI / 180 / 60);
    }

    public void Step(Vector3 player, Vector3 socket, float radius, float height, bool attractEnabled) =>
        Step(player, socket, radius, height, attractEnabled, null);

    public void Step(Vector3 player, Vector3 socket, float radius, float height, bool attractEnabled,
        Rac2BoltContact? contact)
    {
        if (Collected) return;
        var delta = player - Position;
        bool nearby = delta.X * delta.X + delta.Y * delta.Y < radius * radius && MathF.Abs(delta.Z) < height;
        if (attractEnabled && nearby) _activated = true;
        switch (NativeState)
        {
            case 1: StepFlight(player, attractEnabled, contact); break;
            case 2: StepFirstHop(); break;
            case 3:
                if (attractEnabled && _activated) Attract(player);
                else { Orientation = StepGroundPose(); Hop(.0125f); }
                break;
            case 4 when attractEnabled: StepAttracted(socket); break;
            default: break;
        }
    }

    private void StepFlight(Vector3 player, bool attractEnabled, Rac2BoltContact? contact)
    {
        Velocity -= new Vector3(0, 0, GravityPerTick);
        Position += Velocity;
        StepSpin();
        if (Position.Z < _launchHeight - 2 && Position.Z < player.Z - 2 && attractEnabled)
            Attract(player);
        else if (contact is { } hit && Velocity.Z < 0 && hit.Normal.Z > .70710677f)
        {
            _rest = hit.Position;
            _surface = SurfaceOrientation(hit.Normal);
            Position = _rest;
            if (attractEnabled && _activated)
            {
                if (Velocity.LengthSquared() > 1e-12f) Velocity = Vector3.Normalize(Velocity) * .13333334f;
                Attract(player);
            }
            else
            {
                NativeState = 2;
                _hopLength = _hopRemaining = (int)_sample(30, 40);
                _groundBlend = Orientation;
                Velocity = Vector3.Zero;
            }
        }
    }

    private void StepFirstHop()
    {
        // 0x3773CC..0x3774BC: settle the airborne quaternion toward the
        // continuously turning ground pose over the first rebound.
        var ground = StepGroundPose();
        float remaining = (float)_hopRemaining / _hopLength;
        Position = Vector3.Lerp(Position, _rest + Vector3.UnitZ * ContactRadius, remaining);
        _groundBlend = Quaternion.Normalize(Quaternion.Slerp(_groundBlend, ground, 1f / _hopRemaining));
        Orientation = _groundBlend;
        Hop(.025f);
        if (_hopRemaining == 0)
        {
            NativeState = 3;
            _hopLength = _hopRemaining = (int)_sample(10, 20);
        }
    }

    private void StepAttracted(Vector3 socket)
    {
        StepSpin();
        // Front-of-camera attraction branch. Contact/socket mapping is
        // supplied by the host; no borrowed world/player controller.
        float speed = Velocity.Length();
        if (speed > .006666667f) Velocity *= (speed - .006666667f) / speed;
        _acceleration = Math.Min(.53333336f, _acceleration + .006666667f);
        Position += Velocity;
        var to = socket - Position;
        if (to.Length() <= _acceleration) { Position = socket; Collected = true; }
        else Position += Vector3.Normalize(to) * _acceleration;
    }

    private void Attract(Vector3 target)
    {
        // 0x378174..0x3781F8: a resting pickup leaves along the support's
        // up axis, then spins at 720 degrees/second around the homing axis.
        if (NativeState == 3) Velocity = Vector3.Transform(Vector3.UnitZ, _surface) * .13333334f;
        _spin = AxisSpin(Vector3.Cross(Velocity, target - Position), .20943955f);
        NativeState = 4;
        _acceleration = 0;
    }

    private void StepSpin() => Orientation = Quaternion.Normalize(Quaternion.Concatenate(Orientation, _spin));

    private Quaternion StepGroundPose()
    {
        // Loaded 0x377D10: denomination-specific roll/tilt and the original
        // off-centre model pivot compensation, composed in the support frame.
        var (step, tilt, normalOffset, radialOffset) = _nativeClass switch
        {
            13 => (.04f, -.25f, .04875f, .325f),
            14 => (.03f, -.5f, .0245f, .35f),
            15 => (.025f, -.4f, .05625f, .375f),
            _ => (.0175f, -.35f, .135f, .3f)
        };
        _angleX = MathF.IEEERemainder(_angleX + (_clockwise ? step : -step), MathF.Tau);
        _angleZ = MathF.IEEERemainder(_angleZ + (_clockwise ? -.0125f : .0125f), MathF.Tau);
        var local = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationX(_angleX) *
            Matrix4x4.CreateRotationY(tilt) * Matrix4x4.CreateRotationZ(_angleZ));
        var pose = Quaternion.Normalize(Quaternion.Concatenate(local, _surface));
        Position = _rest + Vector3.Transform(Vector3.UnitZ, _surface) * normalOffset +
            Vector3.Transform(Vector3.UnitX, pose) * radialOffset;
        return pose;
    }

    private static Quaternion SurfaceOrientation(Vector3 normal) => Quaternion.CreateFromRotationMatrix(
        Matrix4x4.CreateRotationY(MathF.Atan2(MathF.Sqrt(normal.X * normal.X + normal.Y * normal.Y), normal.Z)) *
        Matrix4x4.CreateRotationZ(MathF.Atan2(normal.Y, normal.X)));

    private static Quaternion AxisSpin(Vector3 axis, float angle) => axis.LengthSquared() > 1e-12f
        ? Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle) : Quaternion.Identity;

    private void Hop(float amplitude)
    {
        if (_hopRemaining <= 0) return;
        float t = (float)_hopRemaining / _hopLength - .5f;
        Position += Vector3.UnitZ * ((.25f - t * t) * amplitude * _hopLength);
        _hopRemaining--;
    }
}

/// <summary>Host surface point and unit normal; the source session supplies the model pivot offset. This does not reproduce PS2 narrow phase.</summary>
public readonly record struct Rac2BoltContact(Vector3 Position, Vector3 Normal);
