using OBP.Core;
using System.Numerics;
using OBP.RAC1.Gameplay;
using OBP.RAC2.Gameplay;
using OBP.RAC3.Gameplay;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

/// <summary>
/// OBP's session-only cross-game weapons. Native sessions retain ammo/cadence/charge;
/// this adapter supplies an explicit common collision and event boundary.
/// Foreign ammo never enters R&C1's inventory or source-game saves.
/// </summary>
public sealed class FusionWeaponSession
{
    public const string LoadoutId = "veldin-three-v1";
    public const string DefaultLoadoutId = "rac1-default-cross-game-v1";
    public static readonly SourceWeaponKey BombGlove = new(ObpSourceGame.Rac1, (int)Rac1WeaponId.FirstRanged);
    public static readonly SourceWeaponKey Lancer = new(ObpSourceGame.Rac2, Rac2LancerProfile.WeaponIndex);
    public static readonly SourceWeaponKey ShockCannon = new(ObpSourceGame.Rac3, Rac3ShockCannonProfile.Identity.ItemDefinitionIndex);
    public static IReadOnlyList<SourceWeaponKey> Weapons { get; } = Array.AsReadOnly(new[] { BombGlove, Lancer, ShockCannon });
    public Rac1WeaponInventory Rac1Inventory { get; private set; }
    public Rac2LancerSession LancerSession { get; } = new(initialAmmo: 40);
    public Rac3ShockCannonSession CannonSession { get; } = new(ammo: 20);
    public SourceWeaponKey Selected { get; private set; } = BombGlove;
    public PlayerHeldEquipmentSession Hand { get; }
    public Rac3ShockCannonFrame? CannonFrame { get; private set; }
    public int ShotsFired { get; private set; }
    public int Tick { get; private set; }
    private readonly Random _spreadRandom;
    private readonly Dictionary<int, Rac2LancerSweep> _sweeps = [];
    private readonly Dictionary<int, int> _cannonQueries = [];
    private readonly List<WeaponAudioIntent> _pendingAudio = [];
    private WeaponFrame? _lastFrame;
    private bool _resolved;
    private SourceWeaponKey? _activeForeignHand;

    public FusionWeaponSession(int seed, Rac1WeaponInventory? inventory = null, PlayerHeldEquipmentSession? hand = null)
    {
        _spreadRandom = new(seed);
        Rac1Inventory = inventory ?? new(true, Rac1WeaponId.FirstRanged, 10);
        Hand = hand ?? new();
        Selected = new(ObpSourceGame.Rac1, Rac1Inventory.CurrentItemId);
    }

    public void BindRac1Inventory(Rac1WeaponInventory inventory) => Rac1Inventory = inventory;

    /// <summary>Keep native ring order and owned arsenal, then append the two foreign weapons.</summary>
    public static IReadOnlyList<SourceWeaponKey> DefaultQuickSelect(Rac1WeaponInventory inventory)
    {
        var available = Rac1WeaponProfiles.Additional.Select(p => p.Item).Prepend(Rac1WeaponId.FirstRanged).Where(inventory.Owns).ToArray();
        return inventory.QuickSelect.Where(id => available.Contains((Rac1WeaponId)id)).Select(id => (Rac1WeaponId)id)
            .Concat(available).Distinct().Select(id => new SourceWeaponKey(ObpSourceGame.Rac1, (int)id)).Concat([Lancer, ShockCannon]).ToArray();
    }

    public bool Select(SourceWeaponKey weapon)
    {
        weapon.Validate();
        if (weapon.SourceGame == ObpSourceGame.Rac1 ? !Enum.IsDefined((Rac1WeaponId)weapon.NativeItemId) || !Rac1Inventory.OwnsNativeItem(weapon.NativeItemId) : weapon != Lancer && weapon != ShockCannon) return false;
        if (weapon.SourceGame == ObpSourceGame.Rac1 && weapon.NativeItemId == (int)Rac1WeaponId.Wrench) Hand.HoldWrench();
        else Hand.HoldEquipment(weapon);
        if (Selected == weapon) { SynchronizeHeldWeapon(); return true; }
        Selected = weapon;
        SynchronizeHeldWeapon();
        // Bomb Glove's existing host continues to own throwing and projectiles.
        if (weapon.SourceGame == ObpSourceGame.Rac1) Rac1Inventory.TryEquip((Rac1WeaponId)weapon.NativeItemId);
        _lastFrame = null;
        return true;
    }

    public WeaponStatus Status(SourceWeaponKey weapon) => weapon.SourceGame == ObpSourceGame.Rac1
        ? new(weapon, Rac1Inventory.GetAmmo(weapon.NativeItemId), Rac1ItemAmmoDescriptors.Get(weapon.NativeItemId).MaxAmmo, "ready")
        : weapon == Lancer ? new(weapon, LancerSession.Ammo, Rac2LancerProfile.AmmoCapacity,
            LancerSession.CooldownSeconds > 0 ? "equipping / recovering" : "ready")
        : weapon == ShockCannon ? new(weapon, CannonSession.Ammo, Rac3ShockCannonProfile.AmmoCapacity,
            CannonFrame?.ChargeUpdates > 0 ? $"charging {CannonFrame.ChargeUpdates}/{Rac3ShockCannonProfile.ChargeUpdates}" : CannonSession.State.ToString())
        : throw new ArgumentException("Weapon is outside the fusion loadout.", nameof(weapon));

    public WeaponFrame Advance(WeaponUseInput input)
    {
        if (_lastFrame is not null && !_resolved) throw new InvalidOperationException("Resolve the previous weapon frame first.");
        if (!float.IsFinite(input.Muzzle.LengthSquared()) || !float.IsFinite(input.Aim.LengthSquared()) || input.Aim.LengthSquared() < 1e-8f)
            throw new ArgumentException("Weapon pose must be finite with a nonzero aim.", nameof(input));
        Tick++;
        SynchronizeHeldWeapon();
        var queries = new List<WeaponQuery>();
        var audio = new List<WeaponAudioIntent>(_pendingAudio);
        var effects = new List<WeaponEffectIntent>();
        _pendingAudio.Clear(); _sweeps.Clear(); _cannonQueries.Clear();
        Vector3 forward = Vector3.Normalize(input.Aim);
        bool held = Hand.IsHolding(Selected);
        var fire = LancerSession.Tick(input.TriggerHeld && input.ActionAdmitted && held && Selected == Lancer, input.Muzzle, forward);
        if (fire.Shot is { } shot)
        {
            ShotsFired++;
            audio.Add(new(Lancer, Rac2LancerProfile.FireSound, WeaponAudioOperation.Play));
            effects.Add(new(new(Lancer, shot.Generation, shot.Sequence), "shot"));
        }
        else if (fire.Status == Rac2LancerFireStatus.NoAmmo)
            audio.Add(new(Lancer, Rac2LancerProfile.EmptySound, WeaponAudioOperation.Play));
        foreach (var flight in LancerSession.ActiveShots)
        {
            var sweep = LancerSession.BeginProjectileTick(flight)!;
            _sweeps.Add(queries.Count, sweep);
            queries.Add(new(queries.Count, new(Lancer, flight.Generation, flight.Sequence), WeaponQueryKind.Segment, sweep.From, sweep.To, .08f));
        }
        if (Selected == ShockCannon)
        {
            CannonFrame = CannonSession.Advance(new(input.TriggerHeld && held, ActionAdmitted: input.ActionAdmitted && held,
                ChargeAdmitted: input.ActionAdmitted && held));
            ShotsFired += CannonFrame.Shots.Count;
            audio.AddRange(CannonFrame.Audio.Select(CannonAudio));
            foreach (var effect in CannonFrame.Effects)
                effects.Add(new(new(ShockCannon, effect.Shot?.Generation ?? CannonFrame.Generation, effect.Shot?.Sequence ?? 0), effect.Kind.ToString()));
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Math.Abs(forward.Y) > .99f ? Vector3.UnitZ : Vector3.UnitY));
            Vector3 up = Vector3.Cross(right, forward);
            foreach (var query in CannonFrame.Queries)
            {
                _cannonQueries.Add(queries.Count, query.Index);
                // OBP collision projection: native polar intervals form a cone
                // around the host aim. This does not synthesize native RNG or
                // claim the unresolved native Euler/muzzle basis is recovered.
                bool sphere = query.Kind is Rac3ShockCannonQueryKind.CloseSphere or Rac3ShockCannonQueryKind.BeamSphere;
                Vector3 origin = input.Muzzle;
                Vector3 end = origin;
                if (sphere)
                {
                    float offset = query.Kind == Rac3ShockCannonQueryKind.CloseSphere ? -query.ForwardOffset : query.ForwardOffset;
                    origin += forward * offset + right * query.LateralOffset;
                    end = origin;
                }
                else
                {
                    float polar = Sample(query.Rotation1From, query.Rotation1To);
                    float azimuth = Sample(query.Rotation2From, query.Rotation2To);
                    Vector3 direction = forward * MathF.Cos(polar) + (right * MathF.Cos(azimuth) + up * MathF.Sin(azimuth)) * MathF.Sin(polar);
                    end += direction * query.Length;
                }
                queries.Add(new(queries.Count, new(ShockCannon, query.Shot.Generation, query.Shot.Sequence),
                    sphere ? WeaponQueryKind.Sphere : WeaponQueryKind.Ray, origin, end, query.Radius));
            }
        }
        _resolved = false;
        return _lastFrame = new(Status(Selected), queries.AsReadOnly(), audio.AsReadOnly(), effects.AsReadOnly());
    }

    public IReadOnlyList<WeaponDamageIntent> ResolveContacts(WeaponFrame frame, IReadOnlyList<WeaponContact> contacts)
    {
        if (!ReferenceEquals(frame, _lastFrame) || _resolved) throw new InvalidOperationException("Contacts require the current unconsumed fusion frame.");
        foreach (var contact in contacts)
            if ((uint)contact.QueryIndex >= frame.Queries.Count || !float.IsFinite(contact.Fraction) || contact.Fraction is < 0 or > 1 ||
                !float.IsFinite(contact.Distance) || contact.Distance < 0 || contact.Target is { } target &&
                (target.SourceGame is not (ObpSourceGame.Rac1 or ObpSourceGame.Rac2 or ObpSourceGame.Rac3) || target.NativeClassId < 0 || target.InstanceIndex < 0))
                throw new ArgumentException("Invalid fusion contact.", nameof(contacts));
        var damage = new List<WeaponDamageIntent>();
        foreach (var (index, sweep) in _sweeps)
        {
            var hit = contacts.Where(c => c.QueryIndex == index).OrderBy(c => c.Fraction).FirstOrDefault();
            Rac2LancerContact? native = hit is null ? null : new(hit.Fraction, hit.Target is { } target
                ? new(target.SourceGame, target.NativeClassId, target.InstanceIndex) : null);
            if (LancerSession.TryCompleteProjectileTick(sweep, native, out var impact) && impact?.Damage is { } intent)
                damage.Add(new(frame.Queries[index].Shot, new(intent.Target.SourceGame, intent.Target.NativeClassId, intent.Target.RuntimeId),
                    intent.Damage.DamageHp, intent.Damage.DamageFlags, "projectile"));
        }
        if (Selected == ShockCannon && CannonFrame is { } cannon)
        {
            var nativeContacts = contacts.Where(c => _cannonQueries.ContainsKey(c.QueryIndex) && c.Target.HasValue)
                .Select(c => new Rac3ShockCannonContact(_cannonQueries[c.QueryIndex],
                    new(c.Target!.Value.SourceGame, c.Target.Value.NativeClassId, c.Target.Value.InstanceIndex), c.Distance, true,
                    // Explicit OBP admission for eligible foreign targets. This
                    // mask is not read from, or attributed to, an R&C1 Moby.
                    NativeMobyModeBits: Rac3ShockCannonProfile.BeamTargetModeMask)).ToArray();
            damage.AddRange(CannonSession.ResolveContacts(cannon, nativeContacts).Select(intent => new WeaponDamageIntent(
                new(ShockCannon, intent.Shot.Generation, intent.Shot.Sequence),
                new(intent.Target.SourceGame, intent.Target.NativeClassId, checked((int)intent.Target.RuntimeId)),
                intent.NativeDamage, intent.NativeFlags, (intent.NativeFlags & Rac3ShockCannonProfile.BeamDamageFlags) == Rac3ShockCannonProfile.BeamDamageFlags ? "beam" : "spread")));
        }
        _resolved = true;
        return damage.AsReadOnly();
    }

    public void ClearTransientState()
    {
        LancerSession.ResetWorld(LancerSession.Ammo);
        CannonSession.Reset(CannonSession.Ammo);
        CannonFrame = null; _lastFrame = null;
        _pendingAudio.Clear(); _sweeps.Clear(); _cannonQueries.Clear();
        _activeForeignHand = null;
        SynchronizeHeldWeapon();
    }

    private void SynchronizeHeldWeapon()
    {
        SourceWeaponKey? next = Selected.SourceGame != ObpSourceGame.Rac1 && Hand.IsHolding(Selected) ? Selected : null;
        if (next == _activeForeignHand) return;
        LancerSession.Unequip();
        foreach (var audio in CannonSession.Unequip().Audio) _pendingAudio.Add(CannonAudio(audio));
        CannonFrame = null;
        _activeForeignHand = next;
        if (next == Lancer) LancerSession.Equip();
        if (next == ShockCannon) CannonSession.Equip();
    }

    private float Sample(float from, float to) => from == to ? from : from + (to - from) * _spreadRandom.NextSingle();
    private static WeaponAudioIntent CannonAudio(Rac3ShockCannonAudio audio) => new(ShockCannon, audio.LocalSoundIndex,
        audio.Operation switch
        {
            Rac3ShockCannonAudioOperation.StartLoop => WeaponAudioOperation.StartLoop,
            Rac3ShockCannonAudioOperation.StopLoop => WeaponAudioOperation.StopLoop,
            _ => WeaponAudioOperation.Play
        });
}
