using System.Collections.Immutable;
using System.Numerics;
using OBP.Core;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game.Diagnostics;

/// <summary>A one-way observation of a fixture entity; its engine owner checks retirement on every read.</summary>
public interface IWeaponDiagnosticWitness<out T> { T Read(); }
public sealed record WeaponModelDiagnostic(bool Visible, bool HasMeshes, bool OriginalMeshes,
    int? NativeClass, int? Sequence, float? FootOffset, Vector3 Position, Vector3 Scale);
public sealed record Rac1EquipmentShotDiagnostic(SourceWeaponKey Weapon, long Id, Vector3 Position, bool Grounded, WeaponModelDiagnostic Model);
public sealed record Rac1DoomAgentDiagnostic(SourceWeaponKey Weapon, long Id, Vector3 Position, Vector3 NativeWander, WeaponModelDiagnostic Model);
public sealed record Rac1WeaponAudioDiagnostic(ObpSourceGame Source, ulong NativeTick, int Class, int Sound, bool Loop);
public sealed record Rac1AdditionalWeaponDiagnostic(bool Preview, bool CampaignOwnsBlaster, int Cargo,
    int UseTicks, int ShotsFired, bool PyroEmitting, (int Class, int Sound)? Loop, HudWeapon? Hud,
    Vector3 Forward, bool RemoteControl, bool GuidedCameraPresent, bool GuidedCameraCurrent, bool PlayerCameraCurrent,
    int RenderedParticles, int ParticleDrawCalls);
public sealed record Rac1ArsenalMissileDiagnostic(SourceWeaponKey Weapon, long Id, WeaponModelDiagnostic Model);
public sealed record Rac1DecoyDiagnostic(SourceWeaponKey Weapon, WeaponModelDiagnostic Model, bool Grounded, int RetiringTicks);
public interface IRac1DecoyDiagnosticFixture : IWeaponDiagnosticWitness<Rac1DecoyDiagnostic> { void StageExpiry(); }
public sealed record Rac1ChickenDiagnostic(ActorInstanceKey Actor, bool Active, bool RewardEmitted, WeaponModelDiagnostic Model);
public sealed record Rac1ArsenalDiagnostic(int SalvoRemaining, long LastId, int Drones, int ChickenCargo,
    ImmutableArray<bool> VisibleBeams, ImmutableArray<bool> ShaderBeams);
public interface IRac1AdditionalWeaponDiagnosticFixture
{
    Rac1AdditionalWeaponDiagnostic Read();
    WeaponModelDiagnostic ReadHeld(Rac1WeaponId weapon);
    Vector3 ReadMuzzle(Rac1WeaponId weapon);
    int ReadAmmo(int nativeItem);
    bool SourceVoiceAudible(int nativeClass, int sound);
    ImmutableArray<Rac1WeaponAudioDiagnostic> ReadSoundTrace();
    ImmutableArray<IWeaponDiagnosticWitness<Rac1EquipmentShotDiagnostic>> ReadShots();
    ImmutableArray<IWeaponDiagnosticWitness<Rac1DoomAgentDiagnostic>> ReadDoomAgents();
    IDisposable BeginPreview();
}
public interface IRac1ArsenalDiagnosticFixture
{
    Rac1ArsenalDiagnostic Read();
    ImmutableArray<IWeaponDiagnosticWitness<Rac1ArsenalMissileDiagnostic>> ReadMissiles();
    ImmutableArray<IRac1DecoyDiagnosticFixture> ReadDecoys();
    ImmutableArray<Rac1ChickenDiagnostic> ReadChickens();
    void DiscardDrones();
    void ResetWorldTick();
    Vector3 ReadEnemyGoal(ActorInstanceKey actor, Vector3 player);
}
