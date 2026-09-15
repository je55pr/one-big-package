using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

public readonly record struct Rac1BombGloveProjectile(
    long ProjectileId,
    int NativeClassId,
    int CreationNativeState,
    int LaunchedNativeState);

public sealed record Rac1BombGloveShot(
    Rac1BombGloveProjectile Projectile,
    int AmmoBefore,
    int AmmoAfter,
    int FireCooldownTicks,
    int ProjectileRearmTicks);

public sealed record Rac1BombGloveDamageResult(
    long ProjectileId,
    int TargetNativeClassId,
    double NativeDamage,
    uint NativeDamageFlags);

public sealed record Rac1BombGloveProbe(
    int Ammo,
    int FireCooldownTicksRemaining,
    int ProjectileRearmTicksRemaining,
    Rac1BombGloveProjectile? PrearmedProjectile,
    Rac1BombGloveShot? Shot);

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
    public const double NativeDamage = 1d;
    public const uint NativeDamageFlags = 0x00010000;

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
        if (fireRequested &&
            _fireCooldownTicksRemaining == 0 &&
            _prearmedProjectile is { } projectile &&
            CurrentAmmo >= Rac1BombGlove.AmmoCostPerShot &&
            TryConsumeRound())
        {
            int ammoAfter = CurrentAmmo;
            int ammoBefore = ammoAfter + Rac1BombGlove.AmmoCostPerShot;
            _prearmedProjectile = null;
            _projectileRearmTicksRemaining = Rac1BombGlove.ProjectileRearmTicks;
            _fireCooldownTicksRemaining = Rac1BombGlove.FireCooldownTicks;
            _launchedProjectileIds.Add(projectile.ProjectileId);
            shot = new Rac1BombGloveShot(
                projectile,
                ammoBefore,
                ammoAfter,
                Rac1BombGlove.FireCooldownTicks,
                Rac1BombGlove.ProjectileRearmTicks);
        }

        return Snapshot(shot);
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
            Rac1BombGlove.ProjectileLaunchedNativeState);

    private Rac1BombGloveProbe Snapshot(Rac1BombGloveShot? shot = null) =>
        new(
            CurrentAmmo,
            _fireCooldownTicksRemaining,
            _projectileRearmTicksRemaining,
            _prearmedProjectile,
            shot);
}
