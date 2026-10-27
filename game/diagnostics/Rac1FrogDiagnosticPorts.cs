using System.Collections.Immutable;
using System.Numerics;
using OBP.Fusion;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed record Rac1FrogDiagnosticCounts(int Registered, int Presented, int Carriers, int Crates, int Pickups, int Debris);
public sealed record Rac1FrogAuthoredDiagnostic(int SwitchSelector, int TargetInstance);
public sealed record Rac1DiagnosticVisual(bool Visible, bool HasMesh, Vector3 Position, Vector3? MeshCentre);
public sealed record Rac1DiagnosticWrenchPath(Vector3 GripStart, Vector3 TipStart, Vector3 GripEnd, Vector3 TipEnd, float Radius);

public interface IRac1FrogDiagnosticFixture
{
    Rac1FrogDiagnosticCounts ReadCounts();
    Rac1FrogFrame Get(int nativeInstance);
    Rac1FrogAuthoredDiagnostic GetAuthored(int nativeInstance);
    Rac1DiagnosticVisual? ReadCarrier(int nativeInstance);
    bool HasPickupModel(int nativeClass);
    void TickFrog(ActorInstanceKey actor);
    void TickCarrier(ActorInstanceKey actor);
    bool MeleeTouchesPlayer(ActorInstanceKey actor);
    void ResolveAnimatedWrenchContact(Rac1DiagnosticWrenchPath path, float nativeDamage);
    IRac1FrogShuttleDiagnosticFixture? Shuttle { get; }
    IRac1FrogWaveDiagnosticFixture? Waves { get; }
    IRac1QueenDiagnosticFixture? Queen { get; }
}
public interface IRac1FrogShuttleDiagnosticFixture
{
    Vector3 StopVolumeCentre(int stop);
    bool SwitchEnabled { get; }
    bool AcceptShuttleArrival(int selectedStop);
    void TickSwitch();
    void ApplySelection();
}
public interface IRac1FrogWaveDiagnosticFixture
{
    int RegisteredCount { get; }
    ImmutableArray<int> Controllers { get; }
    int Group(int instance);
    int State(int instance);
    ImmutableArray<int> Members(int instance);
    Vector3 ZoneCentre(int instance);
    Vector3 NativePosition(int instance);
    void Tick();
}

public sealed record Rac1QueenGroupDiagnostic(ActorInstanceKey Queen, int GroupIndex, ImmutableArray<int> Members)
{
    public int QueenInstance => Queen.Target.InstanceIndex;
}
public sealed record Rac1QueenRecoveryDiagnostic(int NativeState, int Sequence, float Health, int Timer, bool Recovering, bool CanReceiveHostWeaponContact);
public sealed record Rac1QueenEntranceDiagnostic(int NativeState, int FirstLinkedInstance, int SecondLinkedInstance,
    int LinkedEventSelector, int CameraSelector, int NativePathSelector, bool LinkedActivationRequired);
public sealed record Rac1QueenFlightDiagnostic(int NativeState, int Sequence, float Z, float CameraBlend, bool CameraCompletionCandidate);
public sealed record Rac1QueenBodyDiagnostic(ActorInstanceKey Key, Vector3 Position, Vector3 MeshCentre, float AuthoredNativeZ);
public sealed record Rac1QueenLinkedParentDiagnostic(int AuthoredInstance, int NativeState, bool Visible, Vector3 NativePosition,
    ImmutableArray<Rac1AlienQueenLinkedChildFrame> Children);
public enum Rac1DiagnosticCameraOwner { None, Player, Queen, Guided, Conflict, Other }
public sealed record Rac1QueenCameraDiagnostic(bool QueenPresent, bool QueenCurrent, bool GuidedPresent, bool GuidedCurrent,
    bool PlayerCurrent, bool RemoteControl, Rac1DiagnosticCameraOwner Active, Rac1DiagnosticCameraOwner Viewport);

/// <summary>Queen fixture controls call the existing source owners; reads never expose their mutable controllers or scene nodes.</summary>
public interface IRac1QueenDiagnosticFixture
{
    Rac1QueenGroupDiagnostic ReadGroup();
    Rac1QueenRecoveryDiagnostic ReadRecovery();
    Rac1QueenEntranceDiagnostic ReadEntrance();
    Rac1QueenBodyDiagnostic ReadBody();
    Rac1QueenFlightDiagnostic? ReadFlight();
    int? ReadAnimationSequence();
    Rac1AlienQueenFlightCamera? ReadPendingCamera();
    Rac1AlienQueenGateNative ReadGateTuning();
    int OriginalParentCount { get; }
    int RuntimeChildCount { get; }
    ImmutableArray<int> OriginalParentIds { get; }
    Rac1QueenLinkedParentDiagnostic ReadLinkedParent(int parent);
    Rac1QueenLinkedParentDiagnostic StepLinkedParent(int parent, Vector3 nativePlayer, bool globallyEnabled, float angularAdvance);
    void StepLinkedChild(int child, float angularAdvance);
    int ReadLinkedChildState(int child);
    bool ActivateLinkedParent(int parent);
    Rac1DiagnosticVisual ReadLinkedParentVisual(int parent);
    Rac1DiagnosticVisual? ReadLinkedChildVisual(int child);
    ImmutableArray<Rac1DiagnosticVisual> ReadLinkedChildVisuals();
    void TickLinkedChildren();
    void SynchronizeLinkedParents();
    Rac1AlienQueenEntranceTick StepWaiting(bool targetIsNativeClassZero, bool nativeModeGate,
        bool linkedActorAndCameraCallbacksCommitted = false);
    Rac1AlienQueenEntranceTick ObserveLanding(bool nativeLandingConfirmed, bool firstCondition);
    void AdmitRecoveryState(int state, int sequence);
    int ObserveFrogEvent(int state, int sequence, bool completed);
    long ReadDamageSequence();
    void ForgetWrenchContact();
    void ResolveWrenchContact(Rac1DiagnosticWrenchPath path, Rac1WrenchDamageResult damage);
    void InvalidateEquipmentGeometry();
    bool GeometryContainsQueen { get; }
    bool AimTargetAdmitted { get; }
    void TickFlight();
    void TickRecovery();
    Rac1QueenCameraDiagnostic ReadCamera();
    IDisposable PreserveCameraOwnership();
    IDisposable CreateConflictingCamera();
    void MakePlayerCameraCurrent();
    void BeginGuidedCamera();
    void EndGuidedCamera();
    void EndQueenCamera();
}
