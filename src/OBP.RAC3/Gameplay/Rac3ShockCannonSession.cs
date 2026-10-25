using OBP.Core;
namespace OBP.RAC3.Gameplay;

/// <summary>
/// Fixed-update V5 weapon projection. Owns no world, renderer, global RNG or
/// target HP. Native player/pose admission and collision are explicit host facts.
/// Reset/equip policy is OBP session lifetime, not recovered UYA save behaviour.
/// </summary>
public sealed class Rac3ShockCannonSession
{
    private long _generation;
    private long _update;
    private long _shotSequence;
    private bool _previousTrigger;
    private bool _chargeLoop;
    private bool _charged;
    private int _charge;
    private int _windup;
    private int _recovery;
    private int _beamRemaining;
    private Rac3ShockCannonShot? _pending;
    private Rac3ShockCannonShot? _beam;
    private Rac3ShockCannonFrame? _lastFrame;
    private bool _contactsResolved;

    public Rac3ShockCannonSession(int ammo = Rac3ShockCannonProfile.AmmoCapacity)
    {
        ValidateAmmo(ammo);
        Ammo = ammo;
    }

    public int Ammo { get; private set; }
    public Rac3ShockCannonState State { get; private set; } = Rac3ShockCannonState.Unequipped;

    /// <summary>Only the recovered V5 definition is admitted, not V1 or Mega.</summary>
    public Rac3ShockCannonFrame Equip(int itemDefinitionIndex = 43)
    {
        if (itemDefinitionIndex != Rac3ShockCannonProfile.Identity.ItemDefinitionIndex)
            throw new ArgumentOutOfRangeException(nameof(itemDefinitionIndex), "This session supports original NTSC-U Shock Cannon V5 only.");
        return Clear(equipped: true, Ammo);
    }

    public Rac3ShockCannonFrame Unequip() => Clear(equipped: false, Ammo);

    public Rac3ShockCannonFrame Reset(int ammo = Rac3ShockCannonProfile.AmmoCapacity)
    {
        ValidateAmmo(ammo);
        return Clear(equipped: false, ammo);
    }

    /// <summary>Native indexed add/clamp service 0x0050D980.</summary>
    public int AddAmmo(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        int before = Ammo;
        Ammo = (int)Math.Min((long)Ammo + amount, Rac3ShockCannonProfile.AmmoCapacity);
        return Ammo - before;
    }

    public Rac3ShockCannonFrame Advance(Rac3ShockCannonInput input)
    {
        _update = checked(_update + 1);
        var shots = new List<Rac3ShockCannonShot>();
        var queries = new List<Rac3ShockCannonQuery>();
        var audio = new List<Rac3ShockCannonAudio>();
        var effects = new List<Rac3ShockCannonEffect>();

        if (State == Rac3ShockCannonState.Unequipped)
            return Frame(shots, queries, audio, effects);

        if (State == Rac3ShockCannonState.Recovering && --_recovery == 0)
        {
            State = Rac3ShockCannonState.Ready;
            audio.Add(Audio(2, Rac3ShockCannonAudioOperation.Play));
        }

        bool request;
        if (input.TriggerHeld && input.ChargeAdmitted && Ammo > 0)
        {
            _charge = Math.Min(_charge + 1, Rac3ShockCannonProfile.ChargeUpdates);
            request = _charge == 1 || _charge >= Rac3ShockCannonProfile.ChargeUpdates;
            if (!_chargeLoop)
            {
                _chargeLoop = true;
                audio.Add(Audio(3, Rac3ShockCannonAudioOperation.StartLoop, 4));
                effects.Add(Effect(Rac3ShockCannonEffectKind.ChargeStarted, null, 0x0046dcb8));
            }
            if (_charge >= Rac3ShockCannonProfile.ChargeUpdates && !_charged)
            {
                _charged = true;
                effects.Add(Effect(Rac3ShockCannonEffectKind.ChargeReady, null, 0x0046dcb8));
            }
        }
        else
        {
            request = _charge >= Rac3ShockCannonProfile.ChargeUpdates;
            _charge = 0;
            StopCharge(audio);
            if (input.TriggerHeld && !_previousTrigger && Ammo == 0)
                audio.Add(Audio(1, Rac3ShockCannonAudioOperation.Play));
        }
        _previousTrigger = input.TriggerHeld;

        if (request && input.ActionAdmitted && State == Rac3ShockCannonState.Ready && Ammo > 0)
        {
            // Native calls spend(1) ten times and ignores individual failures.
            // Thus a charged beam with 1..9 remaining ammo still fires.
            int cost = _charged ? Math.Min(Ammo, Rac3ShockCannonProfile.ChargedAmmoAttempts) : 1;
            Ammo -= cost;
            _pending = new(Rac3ShockCannonProfile.Identity,
                new(_generation, checked(++_shotSequence)),
                _charged ? Rac3ShockCannonShotKind.ChargedBeam : Rac3ShockCannonShotKind.Spread,
                _update, cost);
            shots.Add(_pending);
            _windup = input.PrimaryTiming ? Rac3ShockCannonProfile.PrimaryWindupUpdates : Rac3ShockCannonProfile.AlternateWindupUpdates;
            State = Rac3ShockCannonState.Windup;
            if (_charged)
            {
                if (_beam is not null)
                    effects.Add(Effect(Rac3ShockCannonEffectKind.BeamStopped, _beam.Id, null));
                _beam = _pending;
                _beamRemaining = Rac3ShockCannonProfile.BeamUpdates;
            }
        }

        // This native block precedes the held-Moby windup/discharge block.
        if (_beam is not null)
        {
            for (int i = 1; i <= Rac3ShockCannonProfile.BeamSampleCount; i++)
                queries.Add(new(Rac3ShockCannonProfile.Identity, _beam.Id,
                    Rac3ShockCannonQueryKind.BeamSphere, queries.Count,
                    Radius: Rac3ShockCannonProfile.BeamSampleRadius,
                    ForwardOffset: i * Rac3ShockCannonProfile.BeamSampleSpacing));
            // Damage queries precede counter decrement. The render callback
            // follows it and skips the transition-to-zero update.
            if (_beamRemaining > 1)
                effects.Add(Effect(Rac3ShockCannonEffectKind.Beam, _beam.Id, 0x003873f8));
            if (--_beamRemaining == 0)
            {
                effects.Add(Effect(Rac3ShockCannonEffectKind.BeamStopped, _beam.Id, null));
                _beam = null;
            }
        }

        if (State == Rac3ShockCannonState.Windup && --_windup == 0)
        {
            State = Rac3ShockCannonState.Discharge;
            effects.Add(Effect(Rac3ShockCannonEffectKind.Muzzle, _pending!.Id, 0x003877d8));
            audio.Add(Audio(_pending.Kind == Rac3ShockCannonShotKind.ChargedBeam ? 0 : 4,
                Rac3ShockCannonAudioOperation.Play));
        }
        else if (State == Rac3ShockCannonState.Discharge)
        {
            State = Rac3ShockCannonState.Recovering;
            _recovery = Rac3ShockCannonProfile.RecoveryUpdates;
            if (_pending!.Kind == Rac3ShockCannonShotKind.Spread)
                AddSpreadQueries(queries, _pending.Id, input.FullCloseSweep);
            _pending = null;
            _charged = false;
            StopCharge(audio);
        }

        return Frame(shots, queries, audio, effects);
    }

    /// <summary>
    /// Consume contacts for the latest returned frame exactly once. This models
    /// max-damage/OR-flags coalescing in the native per-update damage queue; it
    /// does not multiply damage by intersecting rays/spheres. A later update may
    /// emit a fresh intent; target controllers decide whether to accept it.
    /// </summary>
    public IReadOnlyList<Rac3ShockCannonDamageIntent> ResolveContacts(
        Rac3ShockCannonFrame frame,
        IReadOnlyList<Rac3ShockCannonContact> contacts)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(contacts);
        if (!ReferenceEquals(frame, _lastFrame) || frame.Generation != _generation || _contactsResolved)
            throw new InvalidOperationException("Shock Cannon contacts require the current, unconsumed session frame.");
        foreach (var contact in contacts)
        {
            ArgumentNullException.ThrowIfNull(contact);
            ArgumentNullException.ThrowIfNull(contact.Target);
            if ((uint)contact.QueryIndex >= frame.Queries.Count || !float.IsFinite(contact.Distance) || contact.Distance < 0 ||
                !Enum.IsDefined(contact.Target.SourceGame) || contact.Target.NativeClassId < 0 || contact.Target.RuntimeId < 0)
                throw new ArgumentException("Invalid Shock Cannon contact identity, query index or native distance.", nameof(contacts));
        }

        var result = new Dictionary<Rac3ShockCannonTarget, Rac3ShockCannonDamageIntent>();
        foreach (var contact in contacts.OrderBy(c => c.QueryIndex))
        {
            if (!contact.CollisionAdmitted || contact.IsOwner) continue;
            Rac3ShockCannonQuery query = frame.Queries[contact.QueryIndex];
            bool beam = query.Kind == Rac3ShockCannonQueryKind.BeamSphere;
            if (beam && (contact.NativeMobyModeBits & Rac3ShockCannonProfile.BeamTargetModeMask) == 0) continue;
            float damage = beam ? Rac3ShockCannonProfile.ChargedDamage :
                query.Kind == Rac3ShockCannonQueryKind.CloseSphere ? Rac3ShockCannonProfile.BaseDamage :
                Rac3ShockCannonProfile.RayDamage(contact.Distance);
            uint flags = beam ? Rac3ShockCannonProfile.BeamDamageFlags : Rac3ShockCannonProfile.SpreadDamageFlags;
            var intent = new Rac3ShockCannonDamageIntent(Rac3ShockCannonProfile.Identity, query.Shot,
                contact.Target, damage, flags, Rac3ShockCannonProfile.DamageRecordKind, Rac3ShockCannonProfile.DamageOwnerKind);
            if (result.TryGetValue(contact.Target, out var previous))
                intent = (previous.NativeDamage > damage ? previous : intent) with { NativeFlags = previous.NativeFlags | flags };
            result[contact.Target] = intent;
        }
        _contactsResolved = true;
        return Array.AsReadOnly(result.Values.OrderBy(r => r.Target.SourceGame)
            .ThenBy(r => r.Target.NativeClassId).ThenBy(r => r.Target.RuntimeId).ToArray());
    }

    private static void AddSpreadQueries(List<Rac3ShockCannonQuery> queries, Rac3ShockCannonShotId shot, bool fullSweep)
    {
        for (int i = 1; i <= (fullSweep ? 4 : 1); i++)
            queries.Add(new(Rac3ShockCannonProfile.Identity, shot, Rac3ShockCannonQueryKind.CloseSphere,
                queries.Count, Radius: 1.5f + i * .25f, ForwardOffset: -1.35f * i, LateralOffset: -.175f * i));
        const float radians = .017453292f;
        for (int i = 0; i < Rac3ShockCannonProfile.RandomRayCount; i++)
            queries.Add(new(Rac3ShockCannonProfile.Identity, shot, Rac3ShockCannonQueryKind.RandomRay,
                queries.Count, Length: Rac3ShockCannonProfile.RayLength,
                Rotation1From: 0, Rotation1To: -17 * radians, Rotation2From: -170 * radians, Rotation2To: 170 * radians));
        for (int i = 0; i < Rac3ShockCannonProfile.FixedRayCount; i++)
        {
            float rotation2 = (i - 1) * .34906584f;
            queries.Add(new(Rac3ShockCannonProfile.Identity, shot, Rac3ShockCannonQueryKind.FixedRay,
                queries.Count, Length: Rac3ShockCannonProfile.RayLength,
                Rotation1From: -.2617994f, Rotation1To: -.2617994f, Rotation2From: rotation2, Rotation2To: rotation2));
        }
    }

    private Rac3ShockCannonFrame Clear(bool equipped, int ammo)
    {
        var audio = new List<Rac3ShockCannonAudio>();
        var effects = new List<Rac3ShockCannonEffect>();
        StopCharge(audio);
        if (_beam is not null)
            effects.Add(Effect(Rac3ShockCannonEffectKind.BeamStopped, _beam.Id, null));
        if (State != Rac3ShockCannonState.Unequipped)
            effects.Add(Effect(Rac3ShockCannonEffectKind.Stopped, _pending?.Id, null));
        _generation = checked(_generation + 1);
        _update = _shotSequence = 0;
        _charge = _windup = _recovery = _beamRemaining = 0;
        _previousTrigger = _charged = false;
        _pending = _beam = null;
        Ammo = ammo;
        State = equipped ? Rac3ShockCannonState.Ready : Rac3ShockCannonState.Unequipped;
        return Frame([], [], audio, effects);
    }

    private Rac3ShockCannonFrame Frame(List<Rac3ShockCannonShot> shots, List<Rac3ShockCannonQuery> queries,
        List<Rac3ShockCannonAudio> audio, List<Rac3ShockCannonEffect> effects)
    {
        _contactsResolved = false;
        _lastFrame = new(Rac3ShockCannonProfile.Identity, _generation, _update, State, Ammo, _charge, _beamRemaining,
            Array.AsReadOnly(shots.ToArray()), Array.AsReadOnly(queries.ToArray()),
            Array.AsReadOnly(audio.ToArray()), Array.AsReadOnly(effects.ToArray()));
        return _lastFrame;
    }

    private void StopCharge(List<Rac3ShockCannonAudio> audio)
    {
        if (!_chargeLoop) return;
        _chargeLoop = false;
        audio.Add(Audio(3, Rac3ShockCannonAudioOperation.StopLoop));
    }

    private static Rac3ShockCannonAudio Audio(int index, Rac3ShockCannonAudioOperation operation, int flags = 0) =>
        new(Rac3ShockCannonProfile.Identity, Rac3ShockCannonProfile.HeldMobyClass, index, operation, flags);

    private static Rac3ShockCannonEffect Effect(Rac3ShockCannonEffectKind kind, Rac3ShockCannonShotId? shot, int? function) =>
        new(Rac3ShockCannonProfile.Identity, kind, shot, function);

    private static void ValidateAmmo(int ammo)
    {
        if (ammo is < 0 or > Rac3ShockCannonProfile.AmmoCapacity)
            throw new ArgumentOutOfRangeException(nameof(ammo));
    }
}
