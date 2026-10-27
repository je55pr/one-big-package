using System.Collections.Immutable;
using OBP.Core.Math;
using OBP.Runtime.Gameplay;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed record MovementStateEnemyDiagnostic(WeaponTargetKey Target, bool Active, bool Visible, uint CollisionLayer, bool RewardEmitted);
public sealed record MovementStateCrateDiagnostic(WeaponTargetKey Target, bool Active, bool HasBody);
public sealed record MovementStateWorldDiagnostic(ImmutableArray<MovementStateEnemyDiagnostic> Enemies, ImmutableArray<MovementStateCrateDiagnostic> Crates,
    int DestroyedCrates, Rac1WeaponId Equipped, int Ammo, int Bolts, bool EmittedPickups);
public sealed record MovementStateReplayDiagnostic(Vec3 Position, double ControlHeading);
public sealed record MovementStateSaveDiagnostic(bool HasWorld, int InactiveObjects, Vec3 Position, double ControlHeading);
public interface IMovementStateSaveWitness
{
    MovementStateSaveDiagnostic Read();
    IMovementStateSaveWitness WriteFallback();
    void RestorePlayer(double verticalOffset = 0);
}
public interface IMovementStateDiagnosticFixture
{
    IDisposable Begin();
    bool SavedFileExists { get; }
    bool Loading { get; }
    string EvidenceLabel { get; }
    ImmutableArray<WeaponTargetKey> StageLethalReactions();
    WeaponTargetKey BreakCrate();
    bool SelectAndConsumeBomb();
    void Credit(int bolts);
    MovementStateWorldDiagnostic ReadWorld();
    IMovementStateSaveWitness ReadSaved();
    Task<ImmutableArray<MovementStateReplayDiagnostic>> LoadAsync(IMovementStateSaveWitness save, int? frames, CancellationToken token);
    Task<ImmutableArray<MovementStateReplayDiagnostic>> ForwardAsync(int frames, CancellationToken token);
}
