using OBP.Core;
using System.Text.Json.Serialization;
using System.Numerics;

namespace OBP.RAC2.Gameplay;

public enum GcLancerFireStatus
{
    NotEquipped,
    Equipping,
    TriggerReleased,
    CoolingDown,
    NoAmmo,
    Fired,
}

public sealed record GcLancerFireResult(GcLancerFireStatus Status, GcLancerShot? Shot)
{
    [JsonConverter(typeof(SourceGameWireJsonConverter))] public ObpSourceGame SourceGame => GcLancerProfile.SourceGame;
    public int WeaponIndex => GcLancerProfile.WeaponIndex;
}

/// <summary>
/// A session-owned capability as well as immutable shot data. Hosts retain this
/// object when requesting travel ticks; copied ids alone cannot resolve a hit.
/// Position/direction use GC native units in a host-selected orthonormal frame.
/// </summary>
public sealed class GcLancerShot
{
    internal GcLancerShot(long generation, long sequence, Vector3 origin, Vector3 direction)
    {
        Generation = generation;
        Sequence = sequence;
        Origin = origin;
        Direction = direction;
    }

    [JsonConverter(typeof(SourceGameWireJsonConverter))] public ObpSourceGame SourceGame => GcLancerProfile.SourceGame;
    public int WeaponIndex => GcLancerProfile.WeaponIndex;
    public int NativeClassId => GcLancerProfile.ProjectileMobyClass;
    public long Generation { get; }
    public long Sequence { get; }
    public Vector3 Origin { get; }
    public Vector3 Direction { get; }
    public Vector3 Velocity => Direction * GcLancerProfile.ProjectileSpeed;
    public GcCollisionDamage Damage => GcLancerProfile.ProjectileDamage;
}

/// <summary>One collision segment. Complete it before requesting the next tick.</summary>
public sealed class GcLancerSweep
{
    internal GcLancerSweep(GcLancerShot shot, int tick, Vector3 from, Vector3 to)
    {
        Shot = shot;
        Tick = tick;
        From = from;
        To = to;
    }

    public GcLancerShot Shot { get; }
    public int Tick { get; }
    public Vector3 From { get; }
    public Vector3 To { get; }
}

/// <summary>
/// A host-owned target can belong to another game. Damage provenance remains
/// rac2 regardless of the destination world's own class/id conventions.
/// </summary>
public sealed record GcLancerTarget([property: JsonConverter(typeof(SourceGameWireJsonConverter)), JsonRequired] ObpSourceGame SourceGame, int NativeClassId, int RuntimeId);

/// <summary>First blocking contact along a sweep; a null target denotes world geometry.</summary>
public sealed record GcLancerContact(float Fraction, GcLancerTarget? Target = null);

public sealed record GcLancerDamageIntent(
    GcLancerShot Shot,
    GcLancerTarget Target,
    Vector3 Position)
{
    [JsonConverter(typeof(SourceGameWireJsonConverter))] public ObpSourceGame SourceGame => Shot.SourceGame;
    public int WeaponIndex => Shot.WeaponIndex;
    public GcCollisionDamage Damage => Shot.Damage;
}

public sealed record GcLancerImpact(
    GcLancerShot Shot,
    Vector3 Position,
    GcLancerDamageIntent? Damage)
{
    [JsonConverter(typeof(SourceGameWireJsonConverter))] public ObpSourceGame SourceGame => Shot.SourceGame;
}

/// <summary>
/// Deterministic base-Lancer slice: native equip gate, float countdown, ammo
/// admission, straight projectile travel and one terminal collision per shot.
/// The host supplies the muzzle pose, conditioned aim and first blocking
/// contacts. Native auto-aim/homing, near-muzzle overlap queries, world collision
/// shapes and presentation playback are outside this portable boundary.
/// </summary>
public sealed class GcLancerSession
{
    private sealed class Flight(GcLancerShot shot)
    {
        public GcLancerShot Shot { get; } = shot;
        public Vector3 Position { get; set; } = shot.Origin;
        public int Ticks { get; set; }
        public GcLancerSweep? Pending { get; set; }
    }

    private readonly Dictionary<long, Flight> _flights = [];
    private bool _equipping;
    private float _countdown;
    private long _generation;
    private long _sequence;

    /// <summary>
    /// Initial ammo is an explicit loadout input, not a recovered campaign grant.
    /// </summary>
    public GcLancerSession(int initialAmmo) => ResetWorld(initialAmmo);

    public int Ammo { get; private set; }
    public bool Equipped { get; private set; }
    public float CooldownSeconds => _countdown;
    public IReadOnlyList<GcLancerShot> ActiveShots =>
        _flights.Values.Select(flight => flight.Shot).OrderBy(shot => shot.Sequence).ToArray();

    public void Equip()
    {
        if (Equipped) return;
        Equipped = true;
        _equipping = true;
        _countdown = GcLancerProfile.EquipDelaySeconds;
    }

    /// <summary>Switching weapons does not destroy shots already in this world.</summary>
    public void Unequip()
    {
        Equipped = false;
        _equipping = false;
        _countdown = 0;
    }

    /// <summary>
    /// One ordinary 60 Hz weapon update. Pass held trigger state for repeat fire.
    /// Origin is the already transformed muzzle joint; direction is already
    /// conditioned by the host. Both are validated before any state changes.
    /// </summary>
    public GcLancerFireResult Tick(bool triggerHeld, Vector3 muzzleOrigin, Vector3 aimDirection)
    {
        ValidateVector(muzzleOrigin, nameof(muzzleOrigin));
        ValidateVector(aimDirection, nameof(aimDirection));
        float length = aimDirection.Length();
        if (!float.IsFinite(length) || length <= 0)
            throw new ArgumentOutOfRangeException(nameof(aimDirection));

        if (!Equipped) return new(GcLancerFireStatus.NotEquipped, null);
        bool ready = TickCountdown();
        if (_equipping)
        {
            if (ready) _equipping = false;
            // Retail state 2 only changes to state 1 here; fire is a later update.
            return new(GcLancerFireStatus.Equipping, null);
        }

        if (!triggerHeld) return new(GcLancerFireStatus.TriggerReleased, null);
        if (!ready) return new(GcLancerFireStatus.CoolingDown, null);

        // The empty-fire branch installs the same delay as a successful shot.
        _countdown = GcLancerProfile.FireIntervalSeconds;
        if (Ammo < GcLancerProfile.AmmoPerShot)
            return new(GcLancerFireStatus.NoAmmo, null);

        var shot = new GcLancerShot(_generation, checked(++_sequence),
            muzzleOrigin, aimDirection / length);
        Ammo -= GcLancerProfile.AmmoPerShot;
        _flights.Add(shot.Sequence, new Flight(shot));
        return new(GcLancerFireStatus.Fired, shot);
    }

    /// <summary>
    /// Open one native-size travel segment. Null means the shot is no longer
    /// owned by this session, including after ResetWorld or in another session.
    /// </summary>
    public GcLancerSweep? BeginProjectileTick(GcLancerShot shot)
    {
        ArgumentNullException.ThrowIfNull(shot);
        if (!_flights.TryGetValue(shot.Sequence, out var flight)
            || !ReferenceEquals(flight.Shot, shot)) return null;
        if (flight.Pending is not null)
            throw new InvalidOperationException("Complete the outstanding Lancer sweep first.");

        var sweep = new GcLancerSweep(shot, flight.Ticks + 1, flight.Position,
            flight.Position + shot.Direction * (GcLancerProfile.ProjectileSpeed * GcLancerProfile.TickSeconds));
        flight.Pending = sweep;
        return sweep;
    }

    /// <summary>
    /// Resolve the host's first blocking contact, or null for a clear segment.
    /// Duplicate, foreign and reset-era sweeps return false without emitting
    /// damage. World hits end the projectile with no target damage intent.
    /// A clear final segment is tested before flight expiry, as in retail.
    /// </summary>
    public bool TryCompleteProjectileTick(
        GcLancerSweep sweep, GcLancerContact? contact, out GcLancerImpact? impact)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        impact = null;
        if (!_flights.TryGetValue(sweep.Shot.Sequence, out var flight)
            || !ReferenceEquals(flight.Pending, sweep)) return false;

        if (contact is not null)
        {
            if (!float.IsFinite(contact.Fraction) || contact.Fraction < 0 || contact.Fraction > 1)
                throw new ArgumentOutOfRangeException(nameof(contact));
            if (contact.Target is { } target && !Enum.IsDefined(target.SourceGame))
                throw new ArgumentException("Contact target must retain its source game.", nameof(contact));

            Vector3 position = Vector3.Lerp(sweep.From, sweep.To, contact.Fraction);
            impact = new(sweep.Shot, position,
                contact.Target is { } hitTarget ? new(sweep.Shot, hitTarget, position) : null);
            _flights.Remove(sweep.Shot.Sequence);
            return true;
        }

        flight.Position = sweep.To;
        flight.Ticks = sweep.Tick;
        flight.Pending = null;
        if (flight.Ticks >= GcLancerProfile.ProjectileFlightTicks)
            _flights.Remove(sweep.Shot.Sequence);
        return true;
    }

    public int AddAmmo(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        int admitted = Math.Min(amount, GcLancerProfile.AmmoCapacity - Ammo);
        Ammo += admitted;
        return admitted;
    }

    /// <summary>
    /// OBP world-lifetime boundary. Clears equip, timers, flights and pending
    /// contacts, invalidating all old shot/sweep objects before a new world.
    /// Native save/progression and weapon XP are deliberately not synthesized.
    /// </summary>
    public void ResetWorld(int ammo)
    {
        if (ammo < 0 || ammo > GcLancerProfile.AmmoCapacity)
            throw new ArgumentOutOfRangeException(nameof(ammo));
        _generation = checked(_generation + 1);
        _sequence = 0;
        _flights.Clear();
        Unequip();
        Ammo = ammo;
    }

    private bool TickCountdown()
    {
        // 0x002E3170 clamps when old timer <= frame delta. Positive remainders
        // wait another tick; fractional shot periods do not accumulate catch-up.
        if (_countdown <= GcLancerProfile.TickSeconds)
        {
            _countdown = 0;
            return true;
        }

        _countdown -= GcLancerProfile.TickSeconds;
        return false;
    }

    private static void ValidateVector(Vector3 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(name);
    }
}
