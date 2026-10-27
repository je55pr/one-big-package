using System.Collections.Immutable;
using System.Numerics;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed record Rac1WrenchDiagnostic(Rac1WrenchAttackSession.Frame Frame, bool AnimationBlending,
    long Generation, bool ThrowActive, Vector3 WorldPosition, bool ModelPresent, bool Visible,
    bool GloveVisible, int NativeSequence, Vector3 HandLocal, Vector3? ExpectedHandLocal);
public sealed record Rac1WeaponTargetDiagnostic(ActorInstanceKey Key, Vector3 Position, bool Active,
    bool RewardEmitted, int? RewardCentre);
public interface IRac1WrenchDiagnosticFacts { Rac1WrenchDiagnostic ReadWrench(); }
public interface IRac1WeaponTargetFixture
{
    ImmutableArray<Rac1WeaponTargetDiagnostic> ReadCrates();
    ImmutableArray<Rac1WeaponTargetDiagnostic> ReadEnemies();
    Rac1WeaponTargetDiagnostic Read(ActorInstanceKey actor);
    void StageCrate(ActorInstanceKey actor, Vector3 position);
    void StageEnemy(ActorInstanceKey actor, Vector3 position);
}
public enum WeaponDiagnosticCaptureKind { Wrench, Bomb, Fusion, FusionProfile }
public interface IWeaponDiagnosticCapture
{
    Task CaptureAsync(WeaponDiagnosticCaptureKind kind, string name, Vector3 focus, bool close, CancellationToken token);
}
