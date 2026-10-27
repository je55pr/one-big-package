using System.Collections.Immutable;
using System.Numerics;
using OBP.Fusion;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

public interface ITrilogySessionBaseline { void Restore(); }
public interface ITrilogyPoseWitness { void Restore(); }
public sealed record TrilogyEquipmentDiagnostic(string Id, SourceWeaponKey? Item, bool Selectable, string? Reason, int Ammo);
public sealed record TrilogyPlayerDiagnostic(bool Ordinary, bool NativePresent, bool AssetsReady, bool AudioReady, bool WrenchHeld,
    long PhysicsTick, int Shots, bool UniversalPause, bool RetailPause, bool PyroEmitting, bool LiveType12, int RenderedParticles,
    double Health, Vector3 Position, double Yaw, int AcceptedHits, int Projectiles, int AudioPlayers);
public sealed record TrilogyAudioDiagnostic(string Source, string Build, int Level, int Class, int Sound, bool LevelSound, bool Loop, long Tick);
public interface ITrilogyPlayerDiagnosticFixture
{
    TrilogyPlayerDiagnostic Read();
    ITrilogySessionBaseline CaptureSession();
    IDisposable BeginVisit();
    void StageDeck();
    void StageEncounterDeck();
    ITrilogyPoseWitness CapturePose();
    void ResetAudio();
    Task CaptureTrialAsync(CancellationToken token);
    ImmutableArray<TrilogyAudioDiagnostic> ReadAudio();
    bool OpenPause();
    void ShowPauseSection(string section);
    void ClosePause();
    ImmutableArray<TrilogyEquipmentDiagnostic> ReadEquipment();
    void HoldEquipped(bool held);
    void SetPaused(bool paused);
    void StageEncounter();
    void StageAttack(Vector3 target);
}
public sealed record TrilogyActorDiagnostic(string Id, ActorInstanceKey Key, ActorObservation Facts);
public sealed record TrilogyActorPopulationDiagnostic(int Expected, int Placed, int Registered, int Bodies, int Libraries, int Assets,
    ImmutableArray<string> Problems, bool Rac2AnimationPresented);
public interface ITrilogyActorCacheWitness { bool Reused { get; } }
public interface ITrilogyActorRetirementWitness
{
    bool OldActorsRetired { get; }
    bool OldRootsFreed { get; }
    bool DamageTargetsRemoved { get; }
    bool ReadmittedWithNewIdentities { get; }
}
public interface ITrilogyActorInvalidation : IDisposable { ITrilogyActorRetirementWitness Retired { get; } bool PendingRejected { get; } }
public interface ITrilogyActorDiagnosticFixture
{
    TrilogyActorPopulationDiagnostic Read();
    ImmutableArray<TrilogyActorDiagnostic> ReadActors();
    Task PumpAsync(CancellationToken token);
    Task<bool> RejectUnknownContextsAsync();
    bool Reset(string id);
    bool HasRuntimeActor(ActorInstanceKey key);
    bool HasRewardOrAudio(ActorInstanceKey key);
    void ResetEncounter();
    ITrilogyActorCacheWitness CaptureCache();
    Task<bool> VerifyConstructionRollbackAsync();
    ITrilogyActorInvalidation Invalidate();
    void Readmit();
    ITrilogyActorRetirementWitness CaptureRetirement();
    void ResetSharedAssets();
}
