using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

public readonly record struct Rac1BombGloveNativePoint(double X, double Y, double Z);

public readonly record struct Rac1BombGloveProjectile(
    long ProjectileId,
    int NativeClassId,
    int CreationNativeState,
    int LaunchedNativeState,
    Rac1WeaponSpawnOwnership Ownership);

public sealed record Rac1BombGloveShot(
    Rac1BombGloveProjectile Projectile,
    int AmmoBefore,
    int AmmoAfter,
    int FireCooldownTicks,
    int ProjectileRearmTicks,
    Rac1WeaponUseAdmission Admission);

public sealed record Rac1BombGloveDamageResult(
    long ProjectileId,
    int TargetNativeClassId,
    double NativeDamage,
    uint NativeDamageFlags)
{
    public Rac1NativeDamageEnvelope DamageEnvelope => new(NativeDamage, NativeDamageFlags);
}

public sealed record Rac1BombGloveProbe(
    int Ammo,
    int FireCooldownTicksRemaining,
    int ProjectileRearmTicksRemaining,
    Rac1BombGloveProjectile? PrearmedProjectile,
    Rac1BombGloveShot? Shot,
    Rac1WeaponUseAdmission? UseAdmission);

/// <summary>
/// Retail-backed Bomb Glove facts recovered from the loaded Veldin executable.
/// Native class 0x4a is named only as the projectile carrier owned by the weapon path;
/// external model labels are deliberately not promoted as gameplay semantics.
/// </summary>
public static class Rac1BombGlove
{
    public const int NativeWeaponClassId = 0xc0;
    public const int NativeProjectileClassId = 0x4a;
    public const int ProjectileCreationNativeState = 0;
    public const int ProjectileLaunchedNativeState = 1;
    public const int AmmoCostPerShot = 1;
    public const int MaxAmmo = 40;
    public const int ProjectileRearmTicks = 10;
    public const int FireCooldownTicks = 20;
    public const int ProjectilePvarOwnerPointerOffset = 0x30;
    public const int WeaponPvarStagedProjectileOffset = 0x50;
    public const uint LaunchOriginYawOffsetBits = 0xbeb953df;
    public const uint LaunchOriginPlanarRadiusBits = 0x3f5be9fb;
    public const uint LaunchOriginHeightBits = 0x3efb4cc2;
    public static readonly double LaunchOriginYawOffsetRadians =
        BitConverter.Int32BitsToSingle(unchecked((int)LaunchOriginYawOffsetBits));
    public static readonly double LaunchOriginPlanarRadius =
        BitConverter.Int32BitsToSingle(unchecked((int)LaunchOriginPlanarRadiusBits));
    public static readonly double LaunchOriginHeight =
        BitConverter.Int32BitsToSingle(unchecked((int)LaunchOriginHeightBits));
    public const double NativeDamage = 1d;
    public const uint NativeDamageFlags = 0x00010000;

    /// <summary>
    /// The class-0xc0 update constructs and owns class-0x4a, stores the staged
    /// object at owner PVar +0x50, and the constructor writes the owner Moby
    /// pointer to projectile PVar +0x30. Launch receives a dedicated vector
    /// produced by the weapon update; it is not the recovered wrench-yaw rule.
    /// </summary>
    public static readonly Rac1WeaponSpawnOwnership SpawnOwnership = new(
        NativeWeaponClassId,
        NativeProjectileClassId,
        ProjectileCreationNativeState,
        ProjectileLaunchedNativeState,
        ProjectilePvarOwnerPointerOffset,
        WeaponPvarStagedProjectileOffset,
        UsesDedicatedWeaponLaunchFrame: true);

    /// <summary>
    /// Replays the recovered class-0xc0 launch-origin construction. The loaded
    /// update reads Ratchet's native world position and the planar heading pair
    /// matching cos/sin(Moby+0x48), rotates that heading by the retained offset,
    /// applies the retained planar radius, then adds the retained native-Z lift.
    /// Projectile aim beyond this origin remains a separate weapon-update path.
    /// </summary>
    public static Rac1BombGloveNativePoint ResolveLaunchOrigin(
        Rac1BombGloveNativePoint playerPosition,
        double nativePlayerYaw)
    {
        if (!double.IsFinite(playerPosition.X) ||
            !double.IsFinite(playerPosition.Y) ||
            !double.IsFinite(playerPosition.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(playerPosition));
        }

        if (!double.IsFinite(nativePlayerYaw))
            throw new ArgumentOutOfRangeException(nameof(nativePlayerYaw));

        double launchYaw = nativePlayerYaw + LaunchOriginYawOffsetRadians;
        return new Rac1BombGloveNativePoint(
            playerPosition.X + (Math.Cos(launchYaw) * LaunchOriginPlanarRadius),
            playerPosition.Y + (Math.Sin(launchYaw) * LaunchOriginPlanarRadius),
            playerPosition.Z + LaunchOriginHeight);
    }

    public static bool IsGoal1ImpactTarget(RuntimeDynamicObject target, int targetNativeState) =>
        target.SourceGame == "rac1" &&
        target.NativeClassId == Rac1Class749Hostile.NativeClassId &&
        targetNativeState is not (Rac1Class749Hostile.TerminalNativeStateFd or Rac1Class749Hostile.TerminalNativeStateFe);
}

/// <summary>
/// Deterministic native-tick session for the bounded Bomb Glove fire loop.
/// The host resolves controller input and collision geometry; R&C1 owns ammo,
/// projectile readiness, fire gating and admitted impact consequences.
/// </summary>
public sealed class Rac1BombGloveSession
{
    private readonly HashSet<long> _launchedProjectileIds = [];
    private readonly Rac1WeaponInventory? _inventory;
    private int _ammo;
    private int _fireCooldownTicksRemaining;
    private int _projectileRearmTicksRemaining;
    private long _nextProjectileId = 1;
    private Rac1BombGloveProjectile? _prearmedProjectile;

    public Rac1BombGloveSession(int initialAmmo)
    {
        if (initialAmmo is < 0 or > Rac1BombGlove.MaxAmmo) throw new ArgumentOutOfRangeException(nameof(initialAmmo));
        _ammo = initialAmmo;
        if (CurrentAmmo > 0) _prearmedProjectile = CreateProjectile();
    }

    /// <summary>
    /// Bind Bomb Glove fire accounting to the RAC1 inventory that owns item 10.
    /// This keeps one authoritative ammo counter when the live host composes the
    /// recovered selection and projectile slices.
    /// </summary>
    public Rac1BombGloveSession(Rac1WeaponInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!inventory.Owns(Rac1WeaponId.FirstRanged))
            throw new ArgumentException("Bomb Glove item 10 must be owned by the supplied RAC1 inventory.", nameof(inventory));
        if (inventory.FirstRangedAmmo > Rac1BombGlove.MaxAmmo)
            throw new ArgumentOutOfRangeException(nameof(inventory),
                $"Bomb Glove ammo cannot exceed recovered capacity {Rac1BombGlove.MaxAmmo}.");

        _inventory = inventory;
        if (CurrentAmmo > 0) _prearmedProjectile = CreateProjectile();
    }

    public Rac1BombGloveProbe Probe() => Snapshot();

    public Rac1BombGloveProbe Step(bool fireRequested)
    {
        if (_fireCooldownTicksRemaining > 0) _fireCooldownTicksRemaining--;
        if (_projectileRearmTicksRemaining > 0) _projectileRearmTicksRemaining--;

        if (_prearmedProjectile is null &&
            CurrentAmmo > 0 &&
            _projectileRearmTicksRemaining == 0)
        {
            _prearmedProjectile = CreateProjectile();
        }

        Rac1BombGloveShot? shot = null;
        Rac1WeaponUseAdmission? admission = null;
        if (fireRequested)
        {
            int ammoBefore = CurrentAmmo;
            if (_inventory is not null && _inventory.Equipped != Rac1WeaponId.FirstRanged)
            {
                admission = Rac1WeaponUseAdmission.Reject(
                    Rac1WeaponId.FirstRanged,
                    Rac1WeaponUseRejection.NotEquipped,
                    ammoBefore);
            }
            else if (_fireCooldownTicksRemaining != 0)
            {
                admission = Rac1WeaponUseAdmission.Reject(
                    Rac1WeaponId.FirstRanged,
                    Rac1WeaponUseRejection.CadenceBlocked,
                    ammoBefore);
            }
            else if (ammoBefore < Rac1BombGlove.AmmoCostPerShot)
            {
                admission = Rac1WeaponUseAdmission.Reject(
                    Rac1WeaponId.FirstRanged,
                    Rac1WeaponUseRejection.NoAmmo,
                    ammoBefore);
            }
            else if (_prearmedProjectile is not { } projectile)
            {
                admission = Rac1WeaponUseAdmission.Reject(
                    Rac1WeaponId.FirstRanged,
                    Rac1WeaponUseRejection.SpawnNotReady,
                    ammoBefore);
            }
            else if (TryConsumeRound())
            {
                int ammoAfter = CurrentAmmo;
                admission = Rac1WeaponUseAdmission.AcceptFirstRanged(ammoBefore, ammoAfter);
                _prearmedProjectile = null;
                _projectileRearmTicksRemaining = Rac1BombGlove.ProjectileRearmTicks;
                _fireCooldownTicksRemaining = Rac1BombGlove.FireCooldownTicks;
                _launchedProjectileIds.Add(projectile.ProjectileId);
                shot = new Rac1BombGloveShot(
                    projectile,
                    ammoBefore,
                    ammoAfter,
                    Rac1BombGlove.FireCooldownTicks,
                    Rac1BombGlove.ProjectileRearmTicks,
                    admission);
            }
        }

        return Snapshot(shot, admission);
    }

    public Rac1BombGloveDamageResult? ResolveGoal1Impact(
        long projectileId,
        RuntimeDynamicObject target,
        int targetNativeState)
    {
        if (!_launchedProjectileIds.Contains(projectileId)) return null;
        if (!Rac1BombGlove.IsGoal1ImpactTarget(target, targetNativeState)) return null;

        _launchedProjectileIds.Remove(projectileId);
        return new Rac1BombGloveDamageResult(
            projectileId,
            target.NativeClassId,
            Rac1BombGlove.NativeDamage,
            Rac1BombGlove.NativeDamageFlags);
    }

    private int CurrentAmmo => _inventory?.FirstRangedAmmo ?? _ammo;

    private bool TryConsumeRound()
    {
        if (_inventory is not null)
        {
            return _inventory.Equipped == Rac1WeaponId.FirstRanged &&
                   _inventory.TryUseEquipped();
        }

        if (_ammo < Rac1BombGlove.AmmoCostPerShot) return false;
        _ammo -= Rac1BombGlove.AmmoCostPerShot;
        return true;
    }

    private Rac1BombGloveProjectile CreateProjectile() =>
        new(
            _nextProjectileId++,
            Rac1BombGlove.NativeProjectileClassId,
            Rac1BombGlove.ProjectileCreationNativeState,
            Rac1BombGlove.ProjectileLaunchedNativeState,
            Rac1BombGlove.SpawnOwnership);

    private Rac1BombGloveProbe Snapshot(
        Rac1BombGloveShot? shot = null,
        Rac1WeaponUseAdmission? useAdmission = null) =>
        new(
            CurrentAmmo,
            _fireCooldownTicksRemaining,
            _projectileRearmTicksRemaining,
            _prearmedProjectile,
            shot,
            useAdmission);
}
