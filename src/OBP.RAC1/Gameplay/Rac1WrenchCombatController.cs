using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

public enum Rac1WrenchContactPath
{
    HostPolicyAdmission,
}

public readonly record struct Rac1WrenchDirection(double X, double Y, double Z);

/// <summary>
/// Recovered ordinary action-0x13 first-swing motion. The player action handler
/// owns P+0x190 (target) and P+0x194 (current): profile 0 targets 4.4/60 while
/// its native timer is below 18, helper 0x212088 rises by 37/3600 and decays by
/// 28/3600, and the vector is aligned to the attack-facing yaw at P+0xa5c.
/// </summary>
public sealed class Rac1WrenchMotionSession
{
    public const int FirstSwingTargetTicks = 18;
    public const double FirstSwingTargetStep = 4.4d / 60d;
    public const double RisePerTick = 37d / 3600d;
    public const double DecayPerTick = 28d / 3600d;

    private int _tick;
    private double _magnitude;
    private double _nativeYaw;

    public bool Active { get; private set; }
    public int Tick => _tick;
    public double Magnitude => _magnitude;
    public double NativeYaw => _nativeYaw;

    public void Begin(double nativeYaw)
    {
        if (!double.IsFinite(nativeYaw))
            throw new ArgumentOutOfRangeException(nameof(nativeYaw));

        _tick = 0;
        _magnitude = 0d;
        _nativeYaw = nativeYaw;
        Active = true;
    }

    public Rac1WrenchDirection Step()
    {
        if (!Active)
            return default;

        double target = _tick < FirstSwingTargetTicks ? FirstSwingTargetStep : 0d;
        double amount = target > _magnitude ? RisePerTick : DecayPerTick;
        _magnitude = MoveToward(_magnitude, target, amount);
        _tick++;

        var direction = new Rac1WrenchDirection(
            Math.Cos(_nativeYaw) * _magnitude,
            Math.Sin(_nativeYaw) * _magnitude,
            0d);

        if (_tick >= FirstSwingTargetTicks && _magnitude <= 1e-12d)
            Active = false;
        return direction;
    }

    public void Reset()
    {
        _tick = 0;
        _magnitude = 0d;
        _nativeYaw = 0d;
        Active = false;
    }

    private static double MoveToward(double value, double target, double amount)
    {
        double delta = target - value;
        return Math.Abs(delta) <= amount
            ? target
            : value + Math.Sign(delta) * amount;
    }
}

public readonly record struct Rac1WrenchContactTarget(
    int NativeClassId,
    bool IsPlayerSelf);

public sealed record Rac1WrenchHostCandidate(
    RuntimeDynamicObject Source,
    Rac1WrenchHostPoint Center);

public sealed record Rac1WrenchDamageResult(
    Rac1WrenchContactPath ContactPath,
    double NativeDamage,
    uint NativeDamageFlags)
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

    public RuntimeDynamicObject? SelectNearestGoal1HostTarget(
        Rac1WrenchHostPoint root,
        Rac1WrenchHostDirection forward,
        IEnumerable<Rac1WrenchHostCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate =>
                candidate.Source is not null &&
                AdmitGoal1RuntimeTarget(candidate.Source) is not null &&
                Rac1WrenchHostContactPolicy.Admits(
                    root,
                    forward,
                    candidate.Center))
            .OrderBy(candidate => PlanarDistanceSquared(root, candidate.Center))
            .ThenBy(candidate => candidate.Source.NativeClassId)
            .ThenBy(candidate => candidate.Source.InstanceIndex)
            .Select(candidate => candidate.Source)
            .FirstOrDefault();
    }

    public Rac1WrenchDamageResult? ResolveHostAdmittedDamage(
        Rac1WrenchContactTarget target)
    {
        if (!CanDamage(target)) return null;

        return new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.HostPolicyAdmission,
            RepresentativeDamage,
            RepresentativeDamageFlags);
    }

    private static double PlanarDistanceSquared(
        Rac1WrenchHostPoint a,
        Rac1WrenchHostPoint b)
    {
        double dx = b.X - a.X;
        double dz = b.Z - a.Z;
        return (dx * dx) + (dz * dz);
    }

    private static bool CanDamage(Rac1WrenchContactTarget target) =>
        !target.IsPlayerSelf;
}
