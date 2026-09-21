using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

public enum Rac1WrenchContactPath
{
    ForwardDirectRecord,
    ToolTipSphere,
}

public readonly record struct Rac1WrenchPoint(double X, double Y, double Z);

public readonly record struct Rac1WrenchDirection(double X, double Y, double Z);

public readonly record struct Rac1WrenchSphere(Rac1WrenchPoint Center, double Radius);

public readonly record struct Rac1WrenchContactTarget(
    int NativeClassId,
    uint MobyFlags,
    bool IsPlayerSelf);

public sealed record Rac1WrenchDamageResult(
    Rac1WrenchContactPath ContactPath,
    double NativeDamage,
    uint NativeDamageFlags,
    Rac1BoltCrateBreakResult? BoltCrateBreak = null)
{
    public Rac1NativeDamageEnvelope DamageEnvelope => new(NativeDamage, NativeDamageFlags);
}

/// <summary>
/// Bounded retail-backed contact rules for Ratchet's ordinary first wrench swing.
/// Collision queries remain host-owned; this controller decides when the recovered
/// contact paths are active and which contacted native Mobies may receive damage.
/// </summary>
public sealed class Rac1WrenchCombatController
{
    public const int OrdinaryActionId = 0x13;
    public const int OrdinaryProfileId = 0;
    public const double FirstSwingContactStartAge = 17d;
    public const double FirstSwingContactEndAge = 23d;
    public const uint DamageableMobyFlag = 0x00004000;
    public const double NativeDamage = 1d;
    public const uint NativeDamageFlags = 0x00010000;
    public const double ToolTipSphereRadius = 0.35d;
    public const double ToolTipInset = 0.085d;

    /// <summary>
    /// Wrench admission shares the semantic use-result envelope with ranged
    /// weapons, but has no recovered ammo or ranged cooldown gate. Its timing
    /// remains the action/profile/contact window below.
    /// </summary>
    public Rac1WeaponUseAdmission AdmitOrdinaryUse(bool weaponEquipped) =>
        weaponEquipped
            ? Rac1WeaponUseAdmission.AcceptWrench()
            : Rac1WeaponUseAdmission.Reject(
                Rac1WeaponId.Wrench,
                Rac1WeaponUseRejection.NotEquipped);

    /// <summary>
    /// Resolve the ordinary first-swing planar attack axis from Ratchet's live native yaw.
    /// Retail class-0 witnesses show the sequence-23 lunge preserving Moby +0x48 yaw
    /// while translating along this axis; camera orientation is not an admitted input here.
    /// </summary>
    public Rac1WrenchDirection ResolveFirstSwingFacing(double nativePlayerYaw)
    {
        if (!double.IsFinite(nativePlayerYaw))
            throw new ArgumentOutOfRangeException(nameof(nativePlayerYaw));

        return new Rac1WrenchDirection(
            Math.Cos(nativePlayerYaw),
            Math.Sin(nativePlayerYaw),
            0d);
    }

    /// <summary>
    /// Admit only the recovered Goal 1 live targets. This keeps native damageable-flag
    /// eligibility in R&amp;C1 rather than asking the Godot host to manufacture it.
    /// </summary>
    public Rac1WrenchContactTarget? AdmitGoal1RuntimeTarget(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac1") return null;
        if (source.NativeClassId is not (Rac1BoltCrate.NativeClassId or Rac1Class749Hostile.NativeClassId)) return null;
        return new Rac1WrenchContactTarget(source.NativeClassId, DamageableMobyFlag, IsPlayerSelf: false);
    }

    public bool IsContactActive(int actionId, int profileId, double nativeAge)
    {
        if (!double.IsFinite(nativeAge))
            throw new ArgumentOutOfRangeException(nameof(nativeAge));

        return actionId == OrdinaryActionId &&
               profileId == OrdinaryProfileId &&
               nativeAge >= FirstSwingContactStartAge &&
               nativeAge <= FirstSwingContactEndAge;
    }

    public Rac1WrenchSphere? GetClass500ToolTipSphere(
        int actionId,
        int profileId,
        double nativeAge,
        Rac1WrenchPoint root,
        Rac1WrenchPoint tip)
    {
        if (!IsContactActive(actionId, profileId, nativeAge)) return null;

        double dx = tip.X - root.X;
        double dy = tip.Y - root.Y;
        double dz = tip.Z - root.Z;
        double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (!(length > 0d) || !double.IsFinite(length))
            throw new ArgumentException("Wrench root and tool tip must define a finite direction.");

        double insetScale = ToolTipInset / length;
        var center = new Rac1WrenchPoint(
            tip.X - (dx * insetScale),
            tip.Y - (dy * insetScale),
            tip.Z - (dz * insetScale));
        return new Rac1WrenchSphere(center, ToolTipSphereRadius);
    }

    public Rac1WrenchDamageResult? ResolveForwardDirectRecord(
        int actionId,
        int profileId,
        double nativeAge,
        Rac1WrenchContactTarget target)
    {
        if (!IsContactActive(actionId, profileId, nativeAge)) return null;
        if (!CanDamage(target)) return null;
        if (target.NativeClassId == Rac1BoltCrate.NativeClassId) return null;

        return new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.ForwardDirectRecord,
            NativeDamage,
            NativeDamageFlags);
    }

    public Rac1WrenchDamageResult? ApplyClass500ToolTipContact(
        int actionId,
        int profileId,
        double nativeAge,
        Rac1WrenchContactTarget target,
        RuntimeDynamicObject source,
        RuntimeEntityState current,
        Rac1BoltCrateSession crateSession,
        int selectedTotal)
    {
        if (!IsContactActive(actionId, profileId, nativeAge)) return null;
        if (!CanDamage(target)) return null;
        if (target.NativeClassId != Rac1BoltCrate.NativeClassId) return null;
        if (source.NativeClassId != target.NativeClassId)
            throw new ArgumentException("Wrench target class does not match the supplied runtime entity.", nameof(source));

        var crateBreak = crateSession.ApplyDamage(source, current, NativeDamage, selectedTotal);
        return crateBreak is null
            ? null
            : new Rac1WrenchDamageResult(
                Rac1WrenchContactPath.ToolTipSphere,
                NativeDamage,
                NativeDamageFlags,
                crateBreak);
    }

    private static bool CanDamage(Rac1WrenchContactTarget target) =>
        !target.IsPlayerSelf && (target.MobyFlags & DamageableMobyFlag) != 0;
}
