using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

public enum Rac1WrenchContactPath
{
    HostPolicyAdmission,
}

public readonly record struct Rac1WrenchDirection(double X, double Y, double Z);

public readonly record struct Rac1WrenchContactTarget(
    int NativeClassId,
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
/// Retail-backed ordinary-wrench identity/facing and recovered victim consequences.
/// Spatial contact admission is deliberately absent here: Godot uses the explicitly
/// host-owned <see cref="Rac1WrenchHostContactPolicy"/> until retail geometry is recovered.
/// </summary>
public sealed class Rac1WrenchCombatController
{
    public const int OrdinaryActionId = 0x13;
    public const int OrdinaryProfileId = 0;

    // This is a representative positive native damage-record stimulus used to
    // exercise already recovered crate/class-749 consumers. It is not claimed
    // as the retail ordinary-wrench damage envelope.
    public const double RepresentativeDamage = 1d;
    public const uint RepresentativeDamageFlags = 0x00010000;

    /// <summary>
    /// Wrench admission has no recovered ammo or ranged cooldown gate.
    /// </summary>
    public Rac1WeaponUseAdmission AdmitOrdinaryUse(bool weaponEquipped) =>
        weaponEquipped
            ? Rac1WeaponUseAdmission.AcceptWrench()
            : Rac1WeaponUseAdmission.Reject(
                Rac1WeaponId.Wrench,
                Rac1WeaponUseRejection.NotEquipped);

    /// <summary>
    /// The ordinary first swing is action 0x13 using profile 0. The profile row's
    /// 17/23 values are intentionally not interpreted as a hit-active window.
    /// </summary>
    public static bool IsOrdinaryFirstSwing(int actionId, int profileId) =>
        actionId == OrdinaryActionId && profileId == OrdinaryProfileId;

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
    /// Goal-1 integration whitelist only. Retail ordinary-wrench target filtering
    /// remains unresolved, so this method does not manufacture a native flag predicate.
    /// </summary>
    public Rac1WrenchContactTarget? AdmitGoal1RuntimeTarget(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac1") return null;
        if (source.NativeClassId is not (
                Rac1BoltCrate.NativeClassId or
                Rac1Class749Hostile.NativeClassId))
            return null;

        return new Rac1WrenchContactTarget(
            source.NativeClassId,
            IsPlayerSelf: false);
    }

    public Rac1WrenchDamageResult? ResolveHostAdmittedDamage(
        Rac1WrenchContactTarget target)
    {
        if (!CanDamage(target)) return null;
        if (target.NativeClassId == Rac1BoltCrate.NativeClassId) return null;

        return new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.HostPolicyAdmission,
            RepresentativeDamage,
            RepresentativeDamageFlags);
    }

    public Rac1WrenchDamageResult? ApplyClass500HostAdmittedContact(
        Rac1WrenchContactTarget target,
        RuntimeDynamicObject source,
        RuntimeEntityState current,
        Rac1BoltCrateSession crateSession,
        int selectedTotal)
    {
        if (!CanDamage(target)) return null;
        if (target.NativeClassId != Rac1BoltCrate.NativeClassId) return null;
        if (source.NativeClassId != target.NativeClassId)
            throw new ArgumentException(
                "Wrench target class does not match the supplied runtime entity.",
                nameof(source));

        var crateBreak = crateSession.ApplyDamage(
            source,
            current,
            RepresentativeDamage,
            selectedTotal);
        return crateBreak is null
            ? null
            : new Rac1WrenchDamageResult(
                Rac1WrenchContactPath.HostPolicyAdmission,
                RepresentativeDamage,
                RepresentativeDamageFlags,
                crateBreak);
    }

    private static bool CanDamage(Rac1WrenchContactTarget target) =>
        !target.IsPlayerSelf;
}
