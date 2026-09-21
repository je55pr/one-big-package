using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

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

    /// <summary>
    /// The retained item-10 representative uses the native contact-volume path:
    /// the projectile Moby remains the source and the common contact routine
    /// discovers victim Mobies while excluding the source itself.
    /// </summary>
    public Rac1NativeDamageHandoff DamageHandoff =>
        Rac1NativeDamageHandoff.ContactVolume(DamageEnvelope);
}

/// <summary>
/// Engine-neutral result of one host-supplied contact-volume query. Candidate
/// geometry remains host-owned; native target admission and contact-driven
/// projectile completion remain R&C1-owned.
/// </summary>
public sealed record Rac1BombGloveContactResolution(
    long ProjectileId,
    IReadOnlyList<Rac1BombGloveDamageResult> DamageResults,
    bool ProjectileCompleted)
{
    public int AdmittedContactCount => DamageResults.Count;
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
/// The controlled item-10 carrier is native class 0x79; external model labels are
/// deliberately not promoted as gameplay semantics.
/// </summary>
public static class Rac1BombGlove
{
    public const int NativeWeaponClassId = 0xc0;
    public const int NativeProjectileClassId = 0x79;
    public const int ProjectileCreationNativeState = 0;
    public const int ProjectileLaunchedNativeState = 1;
    public const int ProjectileContactNativeState = 2;
    public const int ProjectileTerminalNativeState = 0xfe;
    public const int AmmoCostPerShot = 1;
    public const int MaxAmmo = 40;
    public const int ProjectileRearmTicks = 0;
    public const int FireCooldownTicks = 20;
    public const int ProjectilePvarOwnerPointerOffset = 0x50;
    public const int WeaponPvarStagedProjectileOffset = 0x50;
    public const int ProjectileLongCountdownTicks = 300;
    public const int ProjectileShortCountdownTicks = 30;
    // float32(0x3991a2b4 * 11.0f) = float32 word 0x3b483fb8.
    public const double NativeVerticalStepDeltaAtAuthorityScale = 0.003055555745959282d;
    public const double NativeDamage = 2d;
    public const uint NativeDamageFlags = 0x00830000;

    /// <summary>
    /// The controlled item-10 class-0xc0 update constructs and owns class-0x79,
    /// stores the staged object at owner PVar +0x50, and the constructor writes
    /// the owner Moby pointer to projectile PVar +0x50. Launch owns a dedicated
    /// projectile step vector; it is not the recovered wrench-yaw rule.
    /// </summary>
    public static readonly Rac1WeaponSpawnOwnership SpawnOwnership = new(
        NativeWeaponClassId,
        NativeProjectileClassId,
        ProjectileCreationNativeState,
        ProjectileLaunchedNativeState,
        ProjectilePvarOwnerPointerOffset,
        WeaponPvarStagedProjectileOffset,
        UsesDedicatedWeaponLaunchFrame: true);

    public static bool IsGoal1ContactTarget(Rac1MobyContactFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        facts.Validate();

        return Rac1NativeHitSemantics.IsDistinctContactCandidate(facts) &&
               facts.Target.SourceGame == "rac1" &&
               facts.Target.NativeClassId == Rac1Class749Hostile.NativeClassId &&
               !Rac1MobyRuntime.IsTerminalState(facts.TargetNativeState);
    }
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
                _projectileRearmTicksRemaining = Rac1BombGlove.ProjectileRearmTicks;
                _fireCooldownTicksRemaining = Rac1BombGlove.FireCooldownTicks;
                _launchedProjectileIds.Add(projectile.ProjectileId);
                _prearmedProjectile = CurrentAmmo > 0 ? CreateProjectile() : null;
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

    public Rac1BombGloveDamageResult? ResolveGoal1Contact(
        long projectileId,
        Rac1MobyContactFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        facts.Validate();
        if (!_launchedProjectileIds.Contains(projectileId)) return null;
        if (!Rac1BombGlove.IsGoal1ContactTarget(facts)) return null;

        return new Rac1BombGloveDamageResult(
            projectileId,
            facts.Target.NativeClassId,
            Rac1BombGlove.NativeDamage,
            Rac1BombGlove.NativeDamageFlags);
    }

    public Rac1BombGloveContactResolution ResolveGoal1ContactVolume(
        long projectileId,
        IReadOnlyList<Rac1MobyContactFacts> contacts)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        if (!_launchedProjectileIds.Contains(projectileId))
        {
            return new Rac1BombGloveContactResolution(
                projectileId,
                Array.Empty<Rac1BombGloveDamageResult>(),
                ProjectileCompleted: false);
        }

        var damageResults = new List<Rac1BombGloveDamageResult>();
        foreach (var facts in contacts)
        {
            var damage = ResolveGoal1Contact(projectileId, facts);
            if (damage is not null) damageResults.Add(damage);
        }

        bool completed = damageResults.Count > 0 &&
                         _launchedProjectileIds.Remove(projectileId);
        return new Rac1BombGloveContactResolution(
            projectileId,
            damageResults.AsReadOnly(),
            completed);
    }

    /// <summary>
    /// Explicit host cleanup for a projectile removed for presentation-only
    /// reasons such as the current unresolved visible lifetime fallback.
    /// </summary>
    public bool CompleteProjectile(long projectileId) =>
        _launchedProjectileIds.Remove(projectileId);

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
