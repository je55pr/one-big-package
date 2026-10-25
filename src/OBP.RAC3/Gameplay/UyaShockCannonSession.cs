using OBP.Core;
namespace OBP.RAC3.Gameplay;

/// <summary>
/// Fixed-update V5 weapon projection. Owns no world, renderer, global RNG or
/// target HP. Native player/pose admission and collision are explicit host facts.
/// Reset/equip policy is OBP session lifetime, not recovered UYA save behaviour.
/// </summary>
public sealed class UyaShockCannonSession
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
    private UyaShockCannonShot? _pending;
    private UyaShockCannonShot? _beam;
    private UyaShockCannonFrame? _lastFrame;
    private bool _contactsResolved;

    public UyaShockCannonSession(int ammo = UyaShockCannonProfile.AmmoCapacity)
    {
        ValidateAmmo(ammo);
        Ammo = ammo;
    }

    public int Ammo { get; private set; }
    public UyaShockCannonState State { get; private set; } = UyaShockCannonState.Unequipped;

    /// <summary>Only the recovered V5 definition is admitted, not V1 or Mega.</summary>
    public UyaShockCannonFrame Equip(int itemDefinitionIndex = 43)
    {
        if (itemDefinitionIndex != UyaShockCannonProfile.Identity.ItemDefinitionIndex)
            throw new ArgumentOutOfRangeException(nameof(itemDefinitionIndex), "This session supports original NTSC-U Shock Cannon V5 only.");
        return Clear(equipped: true, Ammo);
    }

    public UyaShockCannonFrame Unequip() => Clear(equipped: false, Ammo);

    public UyaShockCannonFrame Reset(int ammo = UyaShockCannonProfile.AmmoCapacity)
    {
        ValidateAmmo(ammo);
        return Clear(equipped: false, ammo);
    }

    /// <summary>Native indexed add/clamp service 0x0050D980.</summary>
    public int AddAmmo(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        int before = Ammo;
        Ammo = (int)Math.Min((long)Ammo + amount, UyaShockCannonProfile.AmmoCapacity);
        return Ammo - before;
    }

    public UyaShockCannonFrame Advance(UyaShockCannonInput input)
    {
        _update = checked(_update + 1);
        var shots = new List<UyaShockCannonShot>();
        var queries = new List<UyaShockCannonQuery>();
        var audio = new List<UyaShockCannonAudio>();
        var effects = new List<UyaShockCannonEffect>();

        if (State == UyaShockCannonState.Unequipped)
            return Frame(shots, queries, audio, effects);

        if (State == UyaShockCannonState.Recovering && --_recovery == 0)
        {
            State = UyaShockCannonState.Ready;
            audio.Add(Audio(2, UyaShockCannonAudioOperation.Play));
        }

        bool request;
        if (input.TriggerHeld && input.ChargeAdmitted && Ammo > 0)
        {
            _charge = Math.Min(_charge + 1, UyaShockCannonProfile.ChargeUpdates);
            request = _charge == 1 || _charge >= UyaShockCannonProfile.ChargeUpdates;
            if (!_chargeLoop)
            {
                _chargeLoop = true;
                audio.Add(Audio(3, UyaShockCannonAudioOperation.StartLoop, 4));
                effects.Add(Effect(UyaShockCannonEffectKind.ChargeStarted, null, 0x0046dcb8));
            }
            if (_charge >= UyaShockCannonProfile.ChargeUpdates && !_charged)
            {
                _charged = true;
                effects.Add(Effect(UyaShockCannonEffectKind.ChargeReady, null, 0x0046dcb8));
            }
        }
        else
        {
            request = _charge >= UyaShockCannonProfile.ChargeUpdates;
            _charge = 0;
            StopCharge(audio);
            if (input.TriggerHeld && !_previousTrigger && Ammo == 0)
                audio.Add(Audio(1, UyaShockCannonAudioOperation.Play));
        }
        _previousTrigger = input.TriggerHeld;

        if (request && input.ActionAdmitted && State == UyaShockCannonState.Ready && Ammo > 0)
        {
            // Native calls spend(1) ten times and ignores individual failures.
            // Thus a charged beam with 1..9 remaining ammo still fires.
            int cost = _charged ? Math.Min(Ammo, UyaShockCannonProfile.ChargedAmmoAttempts) : 1;
            Ammo -= cost;
            _pending = new(UyaShockCannonProfile.Identity,
                new(_generation, checked(++_shotSequence)),
                _charged ? UyaShockCannonShotKind.ChargedBeam : UyaShockCannonShotKind.Spread,
                _update, cost);
            shots.Add(_pending);
            _windup = input.PrimaryTiming ? UyaShockCannonProfile.PrimaryWindupUpdates : UyaShockCannonProfile.AlternateWindupUpdates;
            State = UyaShockCannonState.Windup;
            if (_charged)
            {
                if (_beam is not null)
                    effects.Add(Effect(UyaShockCannonEffectKind.BeamStopped, _beam.Id, null));
                _beam = _pending;
                _beamRemaining = UyaShockCannonProfile.BeamUpdates;
            }
        }

        // This native block precedes the held-Moby windup/discharge block.
        if (_beam is not null)
        {
            for (int i = 1; i <= UyaShockCannonProfile.BeamSampleCount; i++)
                queries.Add(new(UyaShockCannonProfile.Identity, _beam.Id,
                    UyaShockCannonQueryKind.BeamSphere, queries.Count,
                    Radius: UyaShockCannonProfile.BeamSampleRadius,
                    ForwardOffset: i * UyaShockCannonProfile.BeamSampleSpacing));
            // Damage queries precede counter decrement. The render callback
            // follows it and skips the transition-to-zero update.
            if (_beamRemaining > 1)
                effects.Add(Effect(UyaShockCannonEffectKind.Beam, _beam.Id, 0x003873f8));
            if (--_beamRemaining == 0)
            {
                effects.Add(Effect(UyaShockCannonEffectKind.BeamStopped, _beam.Id, null));
                _beam = null;
            }
        }

        if (State == UyaShockCannonState.Windup && --_windup == 0)
        {
            State = UyaShockCannonState.Discharge;
            effects.Add(Effect(UyaShockCannonEffectKind.Muzzle, _pending!.Id, 0x003877d8));
            audio.Add(Audio(_pending.Kind == UyaShockCannonShotKind.ChargedBeam ? 0 : 4,
                UyaShockCannonAudioOperation.Play));
        }
        else if (State == UyaShockCannonState.Discharge)
        {
            State = UyaShockCannonState.Recovering;
            _recovery = UyaShockCannonProfile.RecoveryUpdates;
            if (_pending!.Kind == UyaShockCannonShotKind.Spread)
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
    public IReadOnlyList<UyaShockCannonDamageIntent> ResolveContacts(
        UyaShockCannonFrame frame,
        IReadOnlyList<UyaShockCannonContact> contacts)
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

        var result = new Dictionary<UyaShockCannonTarget, UyaShockCannonDamageIntent>();
        foreach (var contact in contacts.OrderBy(c => c.QueryIndex))
        {
            if (!contact.CollisionAdmitted || contact.IsOwner) continue;
            UyaShockCannonQuery query = frame.Queries[contact.QueryIndex];
            bool beam = query.Kind == UyaShockCannonQueryKind.BeamSphere;
            if (beam && (contact.NativeMobyModeBits & UyaShockCannonProfile.BeamTargetModeMask) == 0) continue;
            float damage = beam ? UyaShockCannonProfile.ChargedDamage :
                query.Kind == UyaShockCannonQueryKind.CloseSphere ? UyaShockCannonProfile.BaseDamage :
                UyaShockCannonProfile.RayDamage(contact.Distance);
            uint flags = beam ? UyaShockCannonProfile.BeamDamageFlags : UyaShockCannonProfile.SpreadDamageFlags;
            var intent = new UyaShockCannonDamageIntent(UyaShockCannonProfile.Identity, query.Shot,
                contact.Target, damage, flags, UyaShockCannonProfile.DamageRecordKind, UyaShockCannonProfile.DamageOwnerKind);
            if (result.TryGetValue(contact.Target, out var previous))
                intent = (previous.NativeDamage > damage ? previous : intent) with { NativeFlags = previous.NativeFlags | flags };
            result[contact.Target] = intent;
        }
        _contactsResolved = true;
        return Array.AsReadOnly(result.Values.OrderBy(r => r.Target.SourceGame)
            .ThenBy(r => r.Target.NativeClassId).ThenBy(r => r.Target.RuntimeId).ToArray());
    }

    private static void AddSpreadQueries(List<UyaShockCannonQuery> queries, UyaShockCannonShotId shot, bool fullSweep)
    {
        for (int i = 1; i <= (fullSweep ? 4 : 1); i++)
            queries.Add(new(UyaShockCannonProfile.Identity, shot, UyaShockCannonQueryKind.CloseSphere,
                queries.Count, Radius: 1.5f + i * .25f, ForwardOffset: -1.35f * i, LateralOffset: -.175f * i));
        const float radians = .017453292f;
        for (int i = 0; i < UyaShockCannonProfile.RandomRayCount; i++)
            queries.Add(new(UyaShockCannonProfile.Identity, shot, UyaShockCannonQueryKind.RandomRay,
                queries.Count, Length: UyaShockCannonProfile.RayLength,
                Rotation1From: 0, Rotation1To: -17 * radians, Rotation2From: -170 * radians, Rotation2To: 170 * radians));
        for (int i = 0; i < UyaShockCannonProfile.FixedRayCount; i++)
        {
            float rotation2 = (i - 1) * .34906584f;
            queries.Add(new(UyaShockCannonProfile.Identity, shot, UyaShockCannonQueryKind.FixedRay,
                queries.Count, Length: UyaShockCannonProfile.RayLength,
                Rotation1From: -.2617994f, Rotation1To: -.2617994f, Rotation2From: rotation2, Rotation2To: rotation2));
        }
    }

    private UyaShockCannonFrame Clear(bool equipped, int ammo)
    {
        var audio = new List<UyaShockCannonAudio>();
        var effects = new List<UyaShockCannonEffect>();
        StopCharge(audio);
        if (_beam is not null)
            effects.Add(Effect(UyaShockCannonEffectKind.BeamStopped, _beam.Id, null));
        if (State != UyaShockCannonState.Unequipped)
            effects.Add(Effect(UyaShockCannonEffectKind.Stopped, _pending?.Id, null));
        _generation = checked(_generation + 1);
        _update = _shotSequence = 0;
        _charge = _windup = _recovery = _beamRemaining = 0;
        _previousTrigger = _charged = false;
        _pending = _beam = null;
        Ammo = ammo;
        State = equipped ? UyaShockCannonState.Ready : UyaShockCannonState.Unequipped;
        return Frame([], [], audio, effects);
    }

    private UyaShockCannonFrame Frame(List<UyaShockCannonShot> shots, List<UyaShockCannonQuery> queries,
        List<UyaShockCannonAudio> audio, List<UyaShockCannonEffect> effects)
    {
        _contactsResolved = false;
        _lastFrame = new(UyaShockCannonProfile.Identity, _generation, _update, State, Ammo, _charge, _beamRemaining,
            Array.AsReadOnly(shots.ToArray()), Array.AsReadOnly(queries.ToArray()),
            Array.AsReadOnly(audio.ToArray()), Array.AsReadOnly(effects.ToArray()));
        return _lastFrame;
    }

    private void StopCharge(List<UyaShockCannonAudio> audio)
    {
        if (!_chargeLoop) return;
        _chargeLoop = false;
        audio.Add(Audio(3, UyaShockCannonAudioOperation.StopLoop));
    }

    private static UyaShockCannonAudio Audio(int index, UyaShockCannonAudioOperation operation, int flags = 0) =>
        new(UyaShockCannonProfile.Identity, UyaShockCannonProfile.HeldMobyClass, index, operation, flags);

    private static UyaShockCannonEffect Effect(UyaShockCannonEffectKind kind, UyaShockCannonShotId? shot, int? function) =>
        new(UyaShockCannonProfile.Identity, kind, shot, function);

    private static void ValidateAmmo(int ammo)
    {
        if (ammo is < 0 or > UyaShockCannonProfile.AmmoCapacity)
            throw new ArgumentOutOfRangeException(nameof(ammo));
    }
}
