using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Ordinary launch, planar drag and gravity from 0x25c558/0x25c698.
/// Per-class parameters are installed by 0x2d5234 and 0x2e1744.
/// The caller supplies direction and the host resolves world contact;
/// native rebound/landing substate flags are not reconstructed here.
/// </summary>
public sealed class Rac1MobyReaction
{
    public sealed record Profile(float Gravity, float Drag, float PlanarLaunch, float VerticalLaunch);
    public static Profile For(int nativeClass, bool lethal) => nativeClass switch
    {
        749 when lethal => new(40f / 3600, 14f / 3600, 13f / 60, 14f / 60),
        1440 when lethal => new(28f / 3600, 5f / 3600, 10f / 60, 9.5f / 60),
        1440 => new(28f / 3600, 5f / 3600, 3.5f / 60, 4.75f / 60),
        // Seeker damage helper 0x2c6230 configures 14/28 fatal and 3/6 hurt.
        // The contact solver 0x25c698 is still approximated in Godot.
        Rac1SeekerSession.ClassId when lethal => new(28f / 3600, 14f / 3600, 14f / 60, 28f / 60),
        Rac1SeekerSession.ClassId => new(6f / 3600, 3f / 3600, 3f / 60, 6f / 60),
        // Amoeboid state-8 launch reads PVar +0x70/74/78/7c.
        // These authored defaults are identical across 227 retail variants;
        // Godot reads the particular child PVar for per-source behaviour.
        int cls when Rac1AmoeboidAuthored.IsFamily(cls) =>
            new(0.008f, 0.006944444f, 0.2f, 0.125f),
        _ => throw new ArgumentOutOfRangeException(nameof(nativeClass)),
    };

    private readonly Profile _profile;
    public Vector3 Velocity { get; private set; }
    public float FacingYaw { get; }

    public Rac1MobyReaction(int nativeClass, bool lethal, float directionYaw)
        : this(For(nativeClass, lethal), directionYaw) { }

    public Rac1MobyReaction(Profile profile, float directionYaw)
    {
        if (!float.IsFinite(directionYaw)) throw new ArgumentOutOfRangeException(nameof(directionYaw));
        if (profile is null || !float.IsFinite(profile.Gravity) || !float.IsFinite(profile.Drag) ||
            !float.IsFinite(profile.PlanarLaunch) || !float.IsFinite(profile.VerticalLaunch) ||
            profile.Gravity < 0 || profile.Drag < 0 || profile.PlanarLaunch < 0 || profile.VerticalLaunch < 0)
            throw new ArgumentOutOfRangeException(nameof(profile));
        _profile = profile;
        Velocity = new(MathF.Cos(directionYaw) * _profile.PlanarLaunch, MathF.Sin(directionYaw) * _profile.PlanarLaunch, _profile.VerticalLaunch);
        FacingYaw = Rac1MobySteering.Wrap(directionYaw + MathF.PI);
    }

    public Vector3 Step()
    {
        float speed = new Vector2(Velocity.X, Velocity.Y).Length();
        float next = Math.Max(0, speed - _profile.Drag);
        float factor = speed > 0 ? next / speed : 0;
        Velocity = new(Velocity.X * factor, Velocity.Y * factor, Math.Min(Velocity.Z - _profile.Gravity, _profile.VerticalLaunch));
        return Velocity;
    }

    public void AcceptHostContact(Vector3 normal)
    {
        // Godot wall sliding is an explicit adapter; do not claim the retail
        // common solver's rebound flags or animation-driven landing transition.
        if (Vector3.Dot(Velocity, normal) < 0) Velocity -= normal * Vector3.Dot(Velocity, normal);
    }
}
