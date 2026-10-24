using System.Numerics;

namespace OBP.Runtime.Gameplay;

/// <summary>Native item numbers only have meaning inside their original game.</summary>
public readonly record struct SourceWeaponKey(string SourceGame, int NativeItemId)
{
    public SourceWeaponKey Validate()
    {
        if (SourceGame is not ("rac1" or "rac2" or "rac3") || NativeItemId < 0)
            throw new ArgumentException("A weapon requires an original game and a nonnegative native item id.");
        return this;
    }

    public override string ToString() => $"{SourceGame}/item:{NativeItemId}";
}

public readonly record struct WeaponTargetKey(string SourceGame, int NativeClassId, int InstanceIndex);
public readonly record struct WeaponShotKey(SourceWeaponKey Weapon, long Generation, long Sequence);
public readonly record struct WeaponUseInput(bool TriggerHeld, bool ActionAdmitted, Vector3 Muzzle, Vector3 Aim);
public enum WeaponQueryKind { Segment, Sphere, Ray }
public enum WeaponAudioOperation { Play, StartLoop, StopLoop }

/// <summary>
/// Host-coordinate collision request, in original game units. Geometry and aim
/// projection are explicit host policy; source sessions own shot admission.
/// </summary>
public sealed record WeaponQuery(int Index, WeaponShotKey Shot, WeaponQueryKind Kind,
    Vector3 From, Vector3 To, float Radius = 0);
public sealed record WeaponContact(int QueryIndex, WeaponTargetKey? Target, float Fraction, float Distance);
public sealed record WeaponAudioIntent(SourceWeaponKey Weapon, int LocalSound, WeaponAudioOperation Operation);
public sealed record WeaponEffectIntent(WeaponShotKey Shot, string Kind);
public sealed record WeaponDamageIntent(WeaponShotKey Shot, WeaponTargetKey Target,
    float SourceDamage, uint SourceFlags, string Kind);
public sealed record WeaponStatus(SourceWeaponKey Weapon, int Ammo, int Capacity, string State);
public sealed record WeaponFrame(WeaponStatus Equipped, IReadOnlyList<WeaponQuery> Queries,
    IReadOnlyList<WeaponAudioIntent> Audio, IReadOnlyList<WeaponEffectIntent> Effects);
