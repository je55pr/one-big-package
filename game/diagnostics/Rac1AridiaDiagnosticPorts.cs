using System.Numerics;
using OBP.Fusion;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed record Rac1ConstructorbotDiagnosticSnapshot(int NativeState, int Health, int WaypointRecord,
    int ActivationVolume, float EngagementRadius, int LiftCount, int SweepCount, float VerticalStep);
public interface IRac1AridiaDiagnosticFixture
{
    float HostUpdateRadius { get; }
    Rac1ConstructorbotDiagnosticSnapshot ReadConstructorbot(ActorInstanceKey actor);
    Vector3 ReadClipCentreNative(int index);
    bool ClipContainsNative(int index, Vector3 nativePoint);
    bool TryReadAuthoredWaypoint(ActorInstanceKey actor, out Vector3 sceneWaypoint);
    bool TryReadRoamingTarget(ActorInstanceKey actor, out Vector3 sceneTarget);
    bool HasRoamingTarget(ActorInstanceKey actor);
    bool TryReadRoamExitFlags(ActorInstanceKey actor, out int flags);
    bool HasRoamExitFlags(ActorInstanceKey actor);
    void ClearRoamingTarget(ActorInstanceKey actor);
    void StageNativeState(ActorInstanceKey actor, int state);
    void SelectSequence(ActorInstanceKey actor, int sequence, int blendTicks);
    float? ProbeStateThreeFloor(ActorInstanceKey actor, Vector3 sceneCandidate);
    float? ProbeGroundLift(ActorInstanceKey actor, Vector3 sceneCandidate);
    bool ProbeSupport(ActorInstanceKey actor, Vector3 sceneCandidate, out float floorY, out float normalY);
    Rac1AridiaConstructorbotNative.WalkerPushoutFrame ProbeSpherePushouts(ActorInstanceKey actor, Vector3 sceneCandidate);
    bool MoveWalkerContact(ActorInstanceKey actor, Vector3 sceneMotion);
    IRac1SharkRecycleTrial BeginSharkRecycleTrial();
}
public sealed record Rac1SharkRecycleStart(ActorInstanceKey Shark, ActorInstanceKey Nest, Vector3 AuthoredHomeScene, Vector3 OriginalRootScene);
public sealed record Rac1SharkRetirement(bool Damaged, bool Retired, int? WaitingShark);
public sealed record Rac1SharkRecycleFrame(Rac1AridiaEnemyFrame Native, int NativeState);
public interface IRac1SharkRecycleTrial : IDisposable
{
    Rac1SharkRecycleStart Start { get; }
    Rac1SharkRetirement StageDeathAndRetire(Vector3 scenePosition);
    void ActivateFromNest(Vector3 sceneNestPosition);
    Rac1SharkRecycleFrame Step(Vector3 nativePosition);
    Vector3 ReadAuthoredHomeScene();
}
