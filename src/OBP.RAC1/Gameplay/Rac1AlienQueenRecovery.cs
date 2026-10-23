using System.Numerics;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Engine-neutral source-backed Alien Queen class-1051 health and recovery
/// submachine, from FUN_L06_002fa9c0. Boss navigation, combat hit detection,
/// camera takeover and objective triggers are NOT replaced by this adapter.
/// </summary>
public sealed class Rac1AlienQueenRecovery
{
    public const int QueenClassId = 1051;
    public const float InitialHealth = 30f;
    public const float FirstRecoveryHealth = 20f;
    public const float SecondRecoveryHealth = 10f;
    public const float FirstPostRecoveryHealth = 19.9f;
    public const float SecondPostRecoveryHealth = 9.9f;
    public const int RecoveryTicks = 30;
    public const int MinimumThresholdTimer = 10;
    public const int RecoveryAnimation = 6;
    public const int WaveRecoveryState = 3;
    public const int FinalRecoveryState = 5;

    public Rac1AlienQueenRecovery(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != QueenClassId)
            throw new ArgumentException("Original class1051 Alien Queen required.", nameof(source));
        if (source.NativePayloads is null || !source.NativePayloads.Any(p =>
            p.Format == Rac1BoltCrate.PVarPayloadFormat && p.Data.Length >= 0x180))
            throw new InvalidDataException("Alien Queen requires original native recovery PVar.");
        QueenInstance = source.InstanceIndex;
    }

    public int QueenInstance { get; }
    public int NativeState { get; private set; } = 1; // after native state0 initialization
    public int Sequence { get; private set; }
    public float Health { get; private set; } = InitialHealth;
    public int Timer { get; private set; } = 30;
    public bool Recovering { get; private set; }
    public float ProtectedThreshold { get; private set; }
    public bool DeathTriggered { get; private set; }

    public bool CanReceiveHostWeaponContact =>
        NativeState is 2 or 4 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 &&
        !Recovering && !DeathTriggered;

    /// <summary>
    /// Admit an existing transport event only if it targets the original Queen
    /// and carries source-owner-consistent provenance. Do not synthesize hit
    /// sources, infer boss entry, or route another enemy's damage to the Queen.
    /// A class-827 native damage producer is allowed, including its 1.5x
    /// modifier, rather than assuming all hits must originate from Ratchet.
    /// </summary>
    public Rac1AlienQueenHit ApplyNativeDamageEvent(Rac1GameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        if (!damage.Target.MatchesMoby(new(QueenClassId, QueenInstance)))
            return new(false, Health, false, DeathTriggered);
        if (damage.NativeDamage < 0 || damage.NativeDamageFlags is null ||
            damage.Owner is not { } owner || !OwnerMatchesSource(owner, damage.Source) ||
            !CanReceiveHostWeaponContact)
            return new(false, Health, false, DeathTriggered);
        return ApplyAdmittedHit((float)damage.NativeDamage, damage.Source.NativeClassId);
    }

    private static bool OwnerMatchesSource(Rac1GameplayDamageOwner owner,
        Rac1GameplayEntityRef source) =>
        owner.RuntimeEntity is { } named
            ? named == Rac1GameplayEntityRef.Player || named == source
            : owner.NativeClassId == source.NativeClassId ||
                owner.NativeClassId == Rac1GameplayEntityRef.Player.NativeClassId;

    /// <summary>
    /// Native Queen damage helper takes the already-admitted hit and multiplies
    /// selected source classes before checking threshold crossings. The
    /// complete common attack/hit solver remains outside this class.
    /// </summary>
    public Rac1AlienQueenHit ApplyAdmittedHit(float damage, int? sourceClass = null)
    {
        if (!float.IsFinite(damage) || damage < 0)
            throw new ArgumentOutOfRangeException(nameof(damage));
        if (NativeState is 0 or 3 or 5 or 14 or 15 || DeathTriggered)
            return new(false, Health, false, DeathTriggered);
        float multiplier = sourceClass switch
        {
            0x131 => .666f,
            0x79 or 0xb0 or 0x33b => 1.5f,
            _ => 1f
        };
        float amount = damage * multiplier;
        if (!float.IsFinite(amount))
            throw new ArgumentOutOfRangeException(nameof(damage));
        float before = Health;
        Health -= amount;
        bool recoveryStarted = false;
        if (!Recovering && Timer >= MinimumThresholdTimer)
        {
            float boundary = before > FirstRecoveryHealth && Health <= FirstRecoveryHealth
                ? FirstRecoveryHealth
                : before > SecondRecoveryHealth && Health <= SecondRecoveryHealth
                    ? SecondRecoveryHealth : 0;
            if (boundary != 0)
            {
                Health = ProtectedThreshold = boundary;
                Recovering = recoveryStarted = true;
                Timer = RecoveryTicks;
            }
        }
        else if (Recovering)
        {
            Health = MathF.Max(Health, ProtectedThreshold);
        }
        if (Health <= 0)
        {
            DeathTriggered = true;
            NativeState = 14;
            Sequence = 10;
        }
        return new(before != Health || recoveryStarted, Health, recoveryStarted, DeathTriggered);
    }

    /// <summary>FUN_L06_002fa9c0 decrements recovery countdown once per
    /// native update and returns to original spawn at its terminal tick.
    /// Source camera/path reset is left to the eventual Queen host.</summary>
    public Rac1AlienQueenRecoveryTick Step()
    {
        int before = NativeState;
        if (Recovering && Timer > 0)
        {
            Timer--;
            if (Timer == 0 && NativeState != 1)
            {
                bool high = Health > SecondRecoveryHealth;
                Health = high ? FirstPostRecoveryHealth : SecondPostRecoveryHealth;
                NativeState = high ? WaveRecoveryState : FinalRecoveryState;
                Sequence = RecoveryAnimation;
                Recovering = false;
                ProtectedThreshold = 0;
            }
        }
        else if (!Recovering && Timer < MinimumThresholdTimer)
            Timer++; // source fade/restore phase; not the native full timer solver
        return new(before, NativeState, Sequence, Health, Recovering, Timer);
    }

    /// <summary>A genuine Queen source transition can supply state and sequence,
    /// but cannot skip the damage-driven recovery when it is in progress.</summary>
    public void AdmitNativeState(int state, int sequence)
    {
        if (state < 0 || state > 15 || sequence < 0 || sequence > 15)
            throw new ArgumentOutOfRangeException(nameof(state));
        if (Recovering && state is 3 or 5)
            throw new InvalidOperationException("Native recovery animation cannot begin before its countdown.");
        NativeState = state;
        Sequence = sequence;
    }
}

public sealed record Rac1AlienQueenHit(bool Accepted, float Health,
    bool RecoveryStarted, bool DeathTriggered);
public sealed record Rac1AlienQueenRecoveryTick(int PreviousState, int NativeState,
    int Sequence, float Health, bool Recovering, int RemainingTicks);
