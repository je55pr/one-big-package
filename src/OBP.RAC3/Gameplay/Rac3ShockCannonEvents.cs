using OBP.Core;
using System.Text.Json.Serialization;
namespace OBP.RAC3.Gameplay;

public enum Rac3ShockCannonState
{
    Unequipped,
    Ready,
    Windup,
    Discharge,
    Recovering,
}

public enum Rac3ShockCannonShotKind { Spread, ChargedBeam }
public enum Rac3ShockCannonQueryKind { CloseSphere, RandomRay, FixedRay, BeamSphere }
public enum Rac3ShockCannonAudioOperation { Play, StartLoop, StopLoop }
public enum Rac3ShockCannonEffectKind { ChargeStarted, ChargeReady, Muzzle, Beam, BeamStopped, Stopped }

/// <summary>
/// One native update, not elapsed wall time. ChargeAdmitted is the player's
/// +0x25ED charge lock. ActionAdmitted separately covers +0x1220 age >=23,
/// +0x28BC gate and other native player/controller admission.
/// Primary timing means sequence equals item +0x24, or the native alternate
/// player-pose predicates at 0x00386078..0x00386088 admit it.
/// FullCloseSweep is false on the native reduced-budget/special-level path.
/// </summary>
public readonly record struct Rac3ShockCannonInput(
    bool TriggerHeld,
    bool ActionAdmitted = true,
    bool PrimaryTiming = true,
    bool FullCloseSweep = true,
    bool ChargeAdmitted = true);

/// <summary>Generation prevents stale host contacts surviving reset/unequip.</summary>
public readonly record struct Rac3ShockCannonShotId(long Generation, long Sequence);

public sealed record Rac3ShockCannonShot(
    Rac3ShockCannonIdentity Weapon,
    Rac3ShockCannonShotId Id,
    Rac3ShockCannonShotKind Kind,
    long Update,
    int AmmoSpent);

/// <summary>
/// Source-local query in the native muzzle/aim basis. The host supplies collision
/// results, occlusion and native RNG samples; this is not an implemented Godot
/// trace. Random rays carry native angular intervals rather than an invented RNG.
/// Rotation1/2 are the ordered 0x0040CCA0 call arguments, not Godot Euler axes.
/// The two close-sphere offsets multiply native Moby +0xD0 / +0xC0 basis vectors.
/// </summary>
public sealed record Rac3ShockCannonQuery(
    Rac3ShockCannonIdentity Weapon,
    Rac3ShockCannonShotId Shot,
    Rac3ShockCannonQueryKind Kind,
    int Index,
    float Radius = 0,
    float ForwardOffset = 0,
    float LateralOffset = 0,
    float Length = 0,
    float Rotation1From = 0,
    float Rotation1To = 0,
    float Rotation2From = 0,
    float Rotation2To = 0);

public sealed record Rac3ShockCannonAudio(
    Rac3ShockCannonIdentity Weapon,
    int MobyClass,
    int LocalSoundIndex,
    Rac3ShockCannonAudioOperation Operation,
    int NativePlayFlags = 0);

public sealed record Rac3ShockCannonEffect(
    Rac3ShockCannonIdentity Weapon,
    Rac3ShockCannonEffectKind Kind,
    Rac3ShockCannonShotId? Shot,
    int? NativeFunctionAddress);

public sealed record Rac3ShockCannonFrame(
    Rac3ShockCannonIdentity Weapon,
    long Generation,
    long Update,
    Rac3ShockCannonState State,
    int Ammo,
    int ChargeUpdates,
    int BeamUpdatesRemaining,
    IReadOnlyList<Rac3ShockCannonShot> Shots,
    IReadOnlyList<Rac3ShockCannonQuery> Queries,
    IReadOnlyList<Rac3ShockCannonAudio> Audio,
    IReadOnlyList<Rac3ShockCannonEffect> Effects);

/// <summary>Targets remain source-qualified even in an OBP cross-game world.</summary>
public sealed record Rac3ShockCannonTarget([property: JsonConverter(typeof(SourceGameWireJsonConverter)), JsonRequired] ObpSourceGame SourceGame, int NativeClassId, long RuntimeId);

/// <summary>
/// An actual host result for one query. CollisionAdmitted means the host applied
/// the query's geometry, native collision filtering and obstruction (a ray may
/// not report an enemy through a wall). It is not inferred from a class name.
/// </summary>
public sealed record Rac3ShockCannonContact(
    int QueryIndex,
    Rac3ShockCannonTarget Target,
    float Distance,
    bool CollisionAdmitted,
    ushort NativeMobyModeBits = 0,
    bool IsOwner = false);

/// <summary>
/// Native damage intent, not a universal subtraction from target HP. The target's
/// source-specific controller still owns resistance, invulnerability and death.
/// Record kind 3, owner kind 2 and flags retain the native carrier semantics.
/// </summary>
public sealed record Rac3ShockCannonDamageIntent(
    Rac3ShockCannonIdentity Weapon,
    Rac3ShockCannonShotId Shot,
    Rac3ShockCannonTarget Target,
    float NativeDamage,
    uint NativeFlags,
    byte NativeRecordKind,
    byte NativeOwnerKind)
{
    public Rac3GameplayDamageEvent ToRac3DamageEvent(int heldMobyInstanceIndex)
    {
        if (Target.SourceGame != ObpSourceGame.Rac3)
            throw new InvalidOperationException("Cross-game damage requires the neutral host's explicit target adapter.");
        if (heldMobyInstanceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(heldMobyInstanceIndex));
        if (Target.NativeClassId < 0 || Target.RuntimeId is < 0 or > int.MaxValue)
            throw new InvalidOperationException("Target is not a valid UYA Moby instance.");
        return new Rac3GameplayDamageEvent(
            Rac3GameplayEntityRef.Moby(new(Rac3ShockCannonProfile.HeldMobyClass, heldMobyInstanceIndex)),
            Rac3GameplayEntityRef.Moby(new(Target.NativeClassId, (int)Target.RuntimeId)),
            NativeDamage, NativeFlags, nativeRecordKind: NativeRecordKind);
    }
}
