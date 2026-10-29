using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Class79, LEVEL0 0x2c2488/2acfd8/2adb30. All vectors are native Z-up
/// and all steps are float32 at 60 Hz. Geometry queries remain the host's job.
/// Ordinary, unupgraded item10 only; moving-platform inheritance and upgraded
/// aim modes are not silently folded into this launch rule.
/// </summary>
public sealed class Rac1BombProjectileSession
{
    public const float NativeTickScale = 1f / 60;
    public const float HorizontalSpeed = 8.5f * NativeTickScale;
    public const float GravityStep = (float)Rac1BombGlove.NativeVerticalStepDeltaAtAuthorityScale;
    public const int BlastTicks = 15;
    public const float InitialBlastRadius = .5f;
    public const float FinalBlastRadius = 2.5f;
    public const float SpinX = 2.094395160675049f * NativeTickScale;
    public const float SpinY = 7.8539814949035645f * NativeTickScale;
    public const int ThrowSequence = 44;
    public const int ThrowEntryFrame = 9;
    public const int ThrowBlendTicks = 7;
    public const float ThrowFinishTime = 21;
    public const int GloveSemanticJoint = 6;
    public const int RatchetGloveJoint = 54;
    public const string GloveSocketKey = "rac1:bomb-glove-hand";

    private int _flightTicks;
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public int NativeState { get; private set; } = 1;
    public int BlastAge { get; private set; }
    public bool Finished => NativeState == Rac1BombGlove.ProjectileTerminalNativeState;
    public bool Flying => NativeState == 1;
    public float BlastRadius => InitialBlastRadius +
        (FinalBlastRadius - InitialBlastRadius) * (BlastAge / (float)BlastTicks);
    public Vector3 Rotation { get; private set; }

    public Rac1BombProjectileSession(Vector3 origin, Vector3 velocity, Vector3 rotation = default)
    {
        RequireFinite(origin); RequireFinite(velocity); RequireFinite(rotation);
        Position = origin; Velocity = velocity; Rotation = rotation;
    }

    /// <summary>0x2c2954..2c29c8, relative to P+80 (body origin, not host feet).</summary>
    public static Vector3 LaunchOrigin(Vector3 bodyOrigin, float yaw)
    {
        RequireFinite(bodyOrigin);
        if (!float.IsFinite(yaw)) throw new ArgumentOutOfRangeException(nameof(yaw));
        float angle = yaw + BitConverter.Int32BitsToSingle(unchecked((int)0xbeb953df));
        float radius = BitConverter.Int32BitsToSingle(0x3f5be9fb);
        return bodyOrigin + new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius,
            BitConverter.Int32BitsToSingle(0x3efb4cc2));
    }

    /// <summary>
    /// 0x2c2488 + 0x25abf0: cap target distance to 8.49, use native facing
    /// for a target closer than 1.5 or outside the 50-degree facing gate, solve
    /// elevation from planar travel time, then cap the upward step at 11/120.
    /// </summary>
    public static Vector3 LaunchVelocity(Vector3 origin, Vector3 target, float yaw)
    {
        RequireFinite(origin); RequireFinite(target);
        if (!float.IsFinite(yaw)) throw new ArgumentOutOfRangeException(nameof(yaw));
        Vector3 offset = target - origin;
        float distance = offset.Length();
        if (distance > 8.5f) target = origin + offset * ((8.5f - .01f) / distance);
        offset = target - origin;
        float direction = MathF.Atan2(offset.Y, offset.X);
        float difference = MathF.Abs(MathF.IEEERemainder(direction - yaw, MathF.Tau));
        if (difference > BitConverter.Int32BitsToSingle(0x3f5f66f3) ||
            new Vector2(offset.X, offset.Y).Length() < 1.5f)
        {
            direction = yaw;
            target.X = origin.X + MathF.Cos(direction) * 1.5f;
            target.Y = origin.Y + MathF.Sin(direction) * 1.5f;
        }
        offset = target - origin;
        float time = new Vector2(offset.X, offset.Y).Length() / HorizontalSpeed;
        float vertical = time == 0 ? 0 : -((origin.Z - target.Z) - GravityStep * (time * time) * .5f) / time;
        vertical = MathF.Min(vertical, GravityStep * (8.5f / HorizontalSpeed) * .5f);
        return new(MathF.Cos(direction) * HorizontalSpeed, MathF.Sin(direction) * HorizontalSpeed, vertical);
    }

    /// <summary>Advance by the old step before subtracting gravity, then query the swept segment.</summary>
    public Vector3 AdvanceFlight()
    {
        if (!Flying) throw new InvalidOperationException("Only a flying bomb can advance.");
        Position += Velocity;
        Velocity = new(Velocity.X, Velocity.Y, Velocity.Z - GravityStep);
        Rotation += new Vector3(SpinX, SpinY, 0);
        return Position;
    }

    public bool TickFlightCountdown() => ++_flightTicks >= Rac1BombGlove.ProjectileLongCountdownTicks;

    public void Impact(Vector3 position)
    {
        RequireFinite(position);
        if (!Flying) return;
        Position = position;
        Velocity = Vector3.Zero;
        NativeState = 2;
        BlastAge = 0;
    }

    /// <summary>0x2af30c..2af3f8: stationary expanding contact volume, ages 0 through 15 inclusive.</summary>
    public void AdvanceBlast()
    {
        if (NativeState != 2) return;
        if (++BlastAge > BlastTicks) NativeState = Rac1BombGlove.ProjectileTerminalNativeState;
    }

    private static void RequireFinite(Vector3 v)
    {
        if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
            throw new ArgumentOutOfRangeException(nameof(v));
    }
}
