using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// One staged projectile from the separately recovered class-0x4a weapon family.
/// This family intentionally has no Rac1WeaponId until retail evidence binds it
/// to a native item slot.
/// </summary>
public readonly record struct Rac1Class4aProjectile(
    long ProjectileId,
    int NativeWeaponClassId,
    int NativeProjectileClassId,
    int CreationNativeState,
    int LaunchedNativeState,
    int ProjectileSourcePvarOffset);

public sealed record Rac1Class4aLaunch(
    Rac1Class4aProjectile Projectile,
    int FireCooldownTicks,
    int ProjectileRearmTicks);

public sealed record Rac1Class4aDirectDamageResult(
    long ProjectileId,
    int TargetNativeClassId,
    double NativeDamage,
    uint NativeDamageFlags)
{
    public Rac1NativeDamageEnvelope DamageEnvelope =>
        new(NativeDamage, NativeDamageFlags);
    public Rac1NativeDamageHandoff DamageHandoff =>
        Rac1NativeDamageHandoff.DirectVictim(DamageEnvelope);
}

public sealed record Rac1Class4aWeaponFamilyProbe(
    int FireCooldownTicksRemaining,
    int ProjectileRearmTicksRemaining,
    Rac1Class4aProjectile? PrearmedProjectile,
    Rac1Class4aLaunch? Launch);

/// <summary>
/// Retail facts for the class-0xc0 / class-0x4a path that survived correction
/// of the item-10 Bomb Glove attribution. Its native item identity, capacity and
/// complete target-selection rules remain unresolved.
/// </summary>
public static class Rac1Class4aWeaponFamily
{
    public const int NativeWeaponClassId = 0xc0;
    public const int NativeProjectileClassId = 0x4a;
    public const int ProjectileCreationNativeState = 0;
    public const int ProjectileLaunchedNativeState = 1;
    public const int ProjectileSourcePvarOffset = 0x30;
    public const int ProjectileRearmTicks = 10;
    public const int FireCooldownTicks = 20;
    public const double NativeDamage = 1d;
    public const uint NativeDamageFlags = 0x00010000;
    public static readonly Rac1NativeDamageHandoff DirectDamageHandoff =
        Rac1NativeDamageHandoff.DirectVictim(
            new Rac1NativeDamageEnvelope(NativeDamage, NativeDamageFlags));
}

/// <summary>
/// Deterministic native-tick staging/cadence owner for the recovered class-0x4a
/// family. <c>stagingAdmitted</c> is deliberately supplied by the caller because
/// the family has not yet been bound to a native item/ammo rule.
/// Target discovery is likewise outside this type: damage may only be committed
/// after a caller has obtained a preselected victim.
/// </summary>
public sealed class Rac1Class4aWeaponFamilySession
{
    private readonly HashSet<long> _launchedProjectileIds = [];
    private int _fireCooldownTicksRemaining;
    private int _projectileRearmTicksRemaining;
    private long _nextProjectileId = 1;
    private Rac1Class4aProjectile? _prearmedProjectile;

    public Rac1Class4aWeaponFamilyProbe Probe() => Snapshot();

    public Rac1Class4aWeaponFamilyProbe Step(
        bool fireRequested,
        bool stagingAdmitted)
    {
        if (_fireCooldownTicksRemaining > 0)
            _fireCooldownTicksRemaining--;
        if (_projectileRearmTicksRemaining > 0)
            _projectileRearmTicksRemaining--;
        Rac1Class4aLaunch? launch = null;
        if (fireRequested &&
            _fireCooldownTicksRemaining == 0 &&
            _prearmedProjectile is { } projectile)
        {
            _prearmedProjectile = null;
            _projectileRearmTicksRemaining =
                Rac1Class4aWeaponFamily.ProjectileRearmTicks;
            _fireCooldownTicksRemaining =
                Rac1Class4aWeaponFamily.FireCooldownTicks;
            _launchedProjectileIds.Add(projectile.ProjectileId);
            launch = new Rac1Class4aLaunch(
                projectile,
                Rac1Class4aWeaponFamily.FireCooldownTicks,
                Rac1Class4aWeaponFamily.ProjectileRearmTicks);
        }

        // Retail stages at the weapon-update tail. Keeping staging after firing
        // prevents a newly admitted replacement from launching on the same tick.
        if (_prearmedProjectile is null &&
            stagingAdmitted &&
            _projectileRearmTicksRemaining == 0)
        {
            _prearmedProjectile = CreateProjectile();
        }

        return Snapshot(launch);
    }

    public Rac1Class4aDirectDamageResult? ResolvePreselectedVictim(
        long projectileId,
        RuntimeDynamicObject victim)
    {
        ArgumentNullException.ThrowIfNull(victim);
        if (!_launchedProjectileIds.Contains(projectileId))
            return null;

        return new Rac1Class4aDirectDamageResult(
            projectileId,
            victim.NativeClassId,
            Rac1Class4aWeaponFamily.NativeDamage,
            Rac1Class4aWeaponFamily.NativeDamageFlags);
    }

    /// <summary>
    /// Explicit completion seam. The retained family evidence does not yet prove
    /// a reusable post-impact terminal state, so damage transport does not retire
    /// the projectile implicitly.
    /// </summary>
    public bool CompleteProjectile(long projectileId) =>
        _launchedProjectileIds.Remove(projectileId);

    private Rac1Class4aProjectile CreateProjectile() =>
        new(
            _nextProjectileId++,
            Rac1Class4aWeaponFamily.NativeWeaponClassId,
            Rac1Class4aWeaponFamily.NativeProjectileClassId,
            Rac1Class4aWeaponFamily.ProjectileCreationNativeState,
            Rac1Class4aWeaponFamily.ProjectileLaunchedNativeState,
            Rac1Class4aWeaponFamily.ProjectileSourcePvarOffset);

    private Rac1Class4aWeaponFamilyProbe Snapshot(
        Rac1Class4aLaunch? launch = null) =>
        new(
            _fireCooldownTicksRemaining,
            _projectileRearmTicksRemaining,
            _prearmedProjectile,
            launch);
}
