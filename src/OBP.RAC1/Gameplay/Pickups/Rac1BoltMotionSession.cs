using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Retail class13..16 update 0x2a5dd8, constructor 0x2a7438 and attract entry
/// 0x2a7210. All vectors are native Z-up; velocity is displacement per 60-Hz tick.
/// Collision queries and the target socket are supplied by the host. Moving
/// platforms, visibility culling and sparkle pools are not simulated here.
/// </summary>
public sealed class Rac1BoltMotionSession
{
    /// <summary>0x2219dc..0x221ac0 writes the player's live pickup dimensions.</summary>
    public static (float PlanarRadius, float Height) AttractionProfile(int nativePlayerState20A4, bool ownsBoltGrabber) =>
        ownsBoltGrabber ? (12, 4.5f) : nativePlayerState20A4 switch
        {
            1 => (2.125f, 1.25f),
            2 => (15, 3),
            _ => (3, 1.75f),
        };
    public const float Dt = 1f / 60f;
    public const float Gravity = 10.8f;
    private readonly Rac1PickupRandom _random;
    private Vector3 _restPosition;
    private int _bounceLength;
    private int _bounceLeft;
    private float _launchHeight;
    private Quaternion _spin;
    private Quaternion _surface = Quaternion.Identity;
    private bool _clockwise;
    private float _angleX;
    private float _angleZ;
    private float _attractYaw;
    private float _attractPitch;
    private bool _placedRest;
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public Quaternion Orientation { get; private set; } = Quaternion.Identity;
    public int NativeState { get; private set; } = 1;
    public bool Collected { get; private set; }
    public float CollectionStep { get; private set; }
    public int NativeClassId { get; }
    // Native Moby scale is class scale times this factor. Mesh decoding has
    // already applied class scale, so only the factor crosses to presentation.
    public float VisualScale => NativeClassId switch { 13 => .65f, 14 => .7f, 15 => .75f, _ => .6f };
    public float CollisionRadius => NativeClassId == 13 ? .15f : NativeClassId is 14 or 15 ? .23f : .25f;
    public bool NeedsWorldContact => NativeState == 1;

    public Rac1BoltMotionSession(int nativeClassId, Vector3 position, Vector3 velocity, Rac1PickupRandom random)
    {
        _ = Rac1BoltCrate.ValueForNativePickupClass(nativeClassId);
        NativeClassId = nativeClassId;
        Position = position;
        _launchHeight = position.Z;
        Velocity = velocity;
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _clockwise = random.Below(2) != 0;
        _angleX = random.Angle();
        _angleZ = random.Angle();
        _spin = AxisSpin(Vector3.Cross(velocity, Vector3.UnitZ), random.Range(0, 720) * MathF.PI / 180 * Dt);
    }

    public static Rac1BoltMotionSession FromCrate(int nativeClassId, Vector3 origin, Rac1PickupRandom random)
    {
        float angle = random.Angle();
        var velocity = new Vector3(MathF.Cos(angle) * random.Range(0, 3) * Dt,
            MathF.Sin(angle) * random.Range(0, 3) * Dt, random.Range(3.7f, 6) * Dt);
        return new(nativeClassId, origin + new Vector3(velocity.X * .5f, velocity.Y * .5f, .5f), velocity, random);
    }

    public static Rac1BoltMotionSession FromPlaced(Rac1PlacedBolt.Authored authored, Rac1PickupRandom random,
        Rac1PickupContact? floor = null)
    {
        bool snap = !authored.PreserveAuthoredHeight && floor is not null;
        Vector3 anchor = snap ? floor!.Value.Position : authored.Position;
        var motion = new Rac1BoltMotionSession(authored.NativeClassId, anchor, Vector3.Zero, random)
        {
            NativeState = 3,
            _restPosition = anchor,
            _placedRest = true,
            _surface = snap ? SurfaceOrientation(floor!.Value.Normal) : Quaternion.Identity,
        };
        motion.ApplyRestPose();
        return motion;
    }

    /// <summary>State4 entry is idempotent, including thrown-wrench attraction.</summary>
    public bool Attract(Vector3? target = null)
    {
        if (NativeState == 4 || Collected) return false;
        if (NativeState == 3)
            Velocity = Vector3.Transform(Vector3.UnitX, _surface) * (_random.Range(3, 7) * Dt);
        CollectionStep = 0;
        _attractYaw = _random.Range(-30, 30) * MathF.PI / 180;
        _attractPitch = _random.Range(0, 30) * MathF.PI / 180;
        _spin = AxisSpin(Vector3.Cross(Velocity, (target ?? Position + Vector3.UnitZ) - Position),
            _random.Range(240, 720) * MathF.PI / 180 * Dt);
        NativeState = 4;
        return true;
    }

    /// <param name="contact">A downward-flight world contact, with the corrected centre and native normal.</param>
    public void Step(Vector3 ratchetRoot, Vector3 targetSocket, Rac1PickupContact? contact = null,
        bool attractEnabled = true, bool thrownWrenchContact = false,
        float attractPlanarRadius = 3f, float attractHeight = 1.75f)
    {
        if (Collected) return;
        if (thrownWrenchContact) Attract(targetSocket);
        // Radius/height admission in states1 and3; constructor can also enter4.
        Vector3 toRoot = ratchetRoot - Position;
        if (attractEnabled && NativeState is 1 or 3 &&
            toRoot.X * toRoot.X + toRoot.Y * toRoot.Y < attractPlanarRadius * attractPlanarRadius &&
            MathF.Abs(toRoot.Z) < attractHeight)
            Attract(targetSocket);
        if (NativeState == 1)
        {
            Velocity -= new Vector3(0, 0, Gravity * Dt * Dt);
            Position += Velocity;
            Orientation = Quaternion.Normalize(Quaternion.Concatenate(Orientation, _spin));
            if (contact is { } hit && Vector3.Dot(Velocity, hit.Normal) < 0 && hit.Normal.Z > .70710677f)
            {
                Position = hit.Position;
                _restPosition = Position;
                _surface = SurfaceOrientation(hit.Normal);
                _bounceLength = _bounceLeft = (int)MathF.Floor(_random.Range(30, 40));
                NativeState = 2;
                Velocity = Vector3.Zero;
            }
            else if (Position.Z < _launchHeight - 2 && Position.Z < ratchetRoot.Z - 2)
                Attract(targetSocket); // 0x2a61b4: rescue bolts falling below their launch and Ratchet.
        }
        else if (NativeState == 2)
        {
            // 0x2a64bc: parabolic rebound; length also controls its height.
            float t = (float)_bounceLeft / _bounceLength - .5f;
            Position = _restPosition + new Vector3(0, 0, -(t * t - .25f) * _bounceLength * .025f);
            StepRestOrientation();
            if (--_bounceLeft == 0)
            {
                NativeState = 3;
                _bounceLength = _bounceLeft = (int)MathF.Floor(_random.Range(10, 20));
                Position = _restPosition;
            }
        }
        else if (NativeState == 3)
        {
            if (_bounceLeft > 0)
            {
                float t = (float)_bounceLeft / _bounceLength - .5f;
                Position = _restPosition + new Vector3(0, 0, -(t * t - .25f) * _bounceLength * .0125f);
                _bounceLeft--;
            }
            StepRestOrientation();
        }
        else if (NativeState == 4)
        {
            // 0x2a689c: shorten velocity by 10*dt², then advance its direction.
            float speed = Velocity.Length();
            if (speed > 10 * Dt * Dt)
                Velocity -= Unit(Velocity) * (10 * Dt * Dt);
            CollectionStep = MathF.Min(CollectionStep + 16 * Dt * Dt, 48 * Dt);
            Position += Velocity;
            Vector3 toSocket = targetSocket - Position;
            if (toSocket.Length() <= CollectionStep)
                Collected = true;
            else
            {
                // The homing entry's angular offsets bend the return path;
                // the host currently supplies an identity Ratchet orientation.
                var bend = Quaternion.CreateFromYawPitchRoll(0, _attractPitch, _attractYaw);
                Position += Vector3.Transform(Unit(toSocket), bend) * CollectionStep;
            }
            Orientation = Quaternion.Normalize(Quaternion.Concatenate(Orientation, _spin));
        }
    }

    private void StepRestOrientation()
    {
        float step = NativeClassId switch { 13 => .04f, 14 => .03f, 15 => .025f, _ => .0175f };
        _angleX += _clockwise ? step : -step;
        _angleZ += _clockwise ? -.0125f : .0125f;
        ApplyRestPose();
    }

    private void ApplyRestPose()
    {
        float tilt = NativeClassId switch { 13 => -.25f, 14 => -.5f, 15 => -.4f, _ => -.35f };
        var local = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationX(_angleX) *
            Matrix4x4.CreateRotationY(tilt) * Matrix4x4.CreateRotationZ(_angleZ));
        Orientation = Quaternion.Normalize(Quaternion.Concatenate(local, _surface));
        if (!_placedRest || NativeState != 3) return;
        // 0x2a7058..0x2a7098: normal offset plus rotating first-basis offset.
        float normalOffset = NativeClassId switch { 13 => .04875f, 14 => .0245f, 15 => .05625f, _ => .135f };
        float radialOffset = NativeClassId switch { 13 => .325f, 14 => .35f, 15 => .375f, _ => .3f };
        Position = _restPosition + Vector3.Transform(Vector3.UnitZ, _surface) * normalOffset +
            Vector3.Transform(Vector3.UnitX, Orientation) * radialOffset;
    }

    private static Quaternion SurfaceOrientation(Vector3 normal)
    {
        normal = Unit(normal);
        return AxisSpin(Vector3.Cross(Vector3.UnitZ, normal), MathF.Acos(Math.Clamp(normal.Z, -1, 1)));
    }

    private static Quaternion AxisSpin(Vector3 axis, float angle) => axis.LengthSquared() > 1e-12f
        ? Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle) : Quaternion.Identity;

    private static Vector3 Unit(Vector3 vector) => vector.LengthSquared() > 1e-12f ? Vector3.Normalize(vector) : Vector3.Zero;
}

public readonly record struct Rac1PickupContact(Vector3 Position, Vector3 Normal);
