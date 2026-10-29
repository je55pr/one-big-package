namespace OBP.RAC1.Gameplay;

/// <summary>
/// One staged class-0x4a child from the Mine Glove controller family.
/// Equipment identity binds controller class 0xbe to Mine Glove, while the
/// retained constructor/update chain binds its spawned child to class 0x4a.
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

public sealed record Rac1Class4aWeaponFamilyProbe(
    int FireCooldownTicksRemaining,
    int ProjectileRearmTicksRemaining,
    Rac1Class4aProjectile? PrearmedProjectile,
    Rac1Class4aLaunch? Launch);

/// <summary>
/// Retail facts for the Mine Glove class-0xbe / class-0x4a path. The class-0x4a
/// child is distinct from class-0x47 OmniWrench update 0x002a84c8, whose
/// direct-damage witness must not be projected onto Mine projectiles.
/// </summary>
public static class Rac1Class4aWeaponFamily
{
    public const int NativeWeaponClassId = 0xbe;
    public const int NativeWeaponUpdate = 0x002c1ad0;
    public const int NativeProjectileClassId = 0x4a;
    public const int NativeProjectileConstructor = 0x002a9ed0;
    public const int NativeProjectileUpdate = 0x002aa670;
    public const int ProjectileCreationNativeState = 0;
    public const int ProjectileLaunchedNativeState = 1;
    public const int ProjectileSourcePvarOffset = 0x30;
    public const int ProjectileRearmTicks = 10;
    public const int FireCooldownTicks = 20;
}

/// <summary>
/// Deterministic native-tick staging/cadence owner for the recovered Mine
/// class-0x4a child family. <c>stagingAdmitted</c> remains supplied by the caller
/// because this bounded contract does not model Mine ammo/admission policy.
/// </summary>
public sealed class Rac1Class4aWeaponFamilySession
{
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
