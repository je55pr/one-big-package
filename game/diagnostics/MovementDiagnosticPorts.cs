using System.Collections.Immutable;
using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Player;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed record DiagnosticMovementState
{
    public Vector3 GlobalPosition { get; init; }
    public Vector3 Rotation { get; init; }
    public Vector3 Velocity { get; init; }
    public Vector3 PositionDelta { get; init; }
    public Vector3 RealVelocity { get; init; }
    public Vector3 SpawnPosition { get; init; }
    public bool Grounded { get; init; }
    public bool FlyEnabled { get; init; }
    public float? CapsuleRadius { get; init; }
    public float? CapsuleHeight { get; init; }
    public bool HasNativeLocomotion { get; init; }
    public string MovementControllerLabel { get; init; } = "";
    public float FloorSnapLength { get; init; }
    public bool FloorConstantSpeed { get; init; }
    public float FloorMaxAngle { get; init; }
    public int MaxSlides { get; init; }
    public float InitialGroundSnapUpwardReach { get; init; }
    public PlayerAnimationState AnimationState { get; init; }
    public double Rac1AnalogueMagnitude { get; init; }
    public double Rac1TargetPlanarStep { get; init; }
    public double Rac1ControllerPlanarStep { get; init; }
    public double Rac1MovementTargetYaw { get; init; }
    public double Rac1CurrentYaw { get; init; }
    public double Rac1YawVelocity { get; init; }
    public double ControlHeading { get; init; }
    public double MovementReproControlHeading { get; init; }
    public int Rac1MaxObservedSlideCollisions { get; init; }
    public bool Rac1AnimationHelperAdmitted { get; init; }
    public float Rac1AnimationScalarAtSelection { get; init; }
    public Rac1NativeVector3? Rac1ContactProcessedMotion { get; init; }
    public Rac1RatchetLocomotionState Rac1LocomotionState { get; init; }
    public Rac1RatchetMovementPhase Rac1MovementPhase { get; init; }
    public Rac1RatchetYawMode Rac1YawMode { get; init; }
    public bool Rac1GameplayAlive { get; init; }
    public bool Rac1RecoveredSupportGrounded { get; init; }
    public int? Rac1TraversalAction { get; init; }
    public double Rac1TraversalAuthoredAnimationTime { get; init; }
    public bool Rac1TraversalAnimationCycleCompleted { get; init; }
    public Rac1TraversalEquipmentId? Rac1ActiveBackpack { get; init; }
    public bool Rac1ClankAvailable { get; init; }
    public int? Rac1DevelopmentPackPreview { get; init; }
    public bool Rac1DevelopmentSwingshotPreview { get; init; }
    public bool Rac1Hanging { get; init; }
    public bool Rac1IceActive { get; init; }
    public bool Rac1SwingshotActive { get; init; }
    public int? Rac1SwingshotTargetIndex { get; init; }
    public bool Rac1SwimmingActive { get; init; }
    public Rac1SwimSnapshot? Rac1SwimState { get; init; }
    public Rac1WaterAnimationFacts Rac1WaterAnimationFacts { get; init; }
    public string Rac1SmallStepDecision { get; init; } = "";
    public bool HasActiveRecoveredCamera { get; init; }
}

/// <summary>Copied movement facts and explicit development fixture actions; no native controller or engine node escapes.</summary>
public interface IDiagnosticMovementPlayer
{
    DiagnosticMovementState Read();
    IDisposable PreserveDevelopmentSettings();
    void ResetCollisionDiagnostics();
    void ResetToSpawn();
    void ResetAt(Vector3 position);
    void SetPosition(Vector3 position);
    void SetRotation(Vector3 rotation);
    void SetYaw(double yaw);
    void SetBackpack(Rac1TraversalEquipmentId? backpack);
    void SetClank(bool available);
    void CyclePack();
    void ToggleSwingshot();
    void SetSnapReach(float reach);
    void StageWater(Vector3 position);
    void ConfigureTargets(ImmutableArray<RuntimeDynamicObject> targets);
    void RestoreWorldTargets();
    IDisposable SubscribeDrowned(Action action);
}
public sealed record DiagnosticMovementPose(int FrameIndex, ImmutableArray<double> LocalPositions);
public sealed record DiagnosticMovementAnimation(bool AvatarPresent, bool NativeController, bool SinkPresent, bool Synchronized,
    int? Sequence, int? Frame, int? FrameCount, double? FramesPerSecond, bool? Loop, string? SourceStateKey,
    PlayerAnimationState? SemanticState, Rac1AnimationFrameClock.Sample? OrdinaryLocomotionSample, float? OrdinaryLocomotionMultiplier,
    Rac1AnimationFrameClock.Sample? OrdinaryStopSample, Rac1AnimationFrameClock.Sample? OrdinaryStandingSample,
    bool CrouchSamplePresent, DiagnosticMovementPose? Pose, bool SinkPosePresent, double StandingHeight);
public interface IDiagnosticMovementAnimation { DiagnosticMovementAnimation Read(); }
public interface IDiagnosticGeometryFixture
{
    IDiagnosticGeometryLease Create(string name, Vector3 position);
}
public interface IDiagnosticGeometryLease : IDisposable
{
    Vector3 GlobalPosition { get; }
    void AddMesh(string name, int rawFaceType, params (Vector3 A, Vector3 B, Vector3 C)[] triangles);
    IDisposable AddBox(string name, Vector3 size, Vector3 position, float yaw);
}
public sealed record DiagnosticMovementWorld(ObpSourceGame? Source, int? LevelId, float? DeathHeight, int CollisionBodies, bool WaterPresent);
public interface IDiagnosticMovementWorld { DiagnosticMovementWorld Read(); IDisposable LimitFrameRate(int rate); }
public interface IDiagnosticWaterFixture
{
    IDisposable IsolateCombat();
    float? SampleHeight(Vector3 position);
    bool AirMeterVisible { get; }
    Rac1RatchetNanotechSnapshot ReadNanotech();
}
public sealed record DiagnosticEnvironmentalDeath(long DeathGeneration, long RestartGeneration, Rac1RatchetNanotechSnapshot Nanotech,
    Rac1RatchetNanotechSnapshot? LastBoundary, Vector3 LastPosition, double LastSeparation, double ContactSeparation, double? CheckpointYaw);
public interface IDiagnosticEnvironmentalDeath { DiagnosticEnvironmentalDeath Read(); }
public sealed record DiagnosticControllerEquipment(Rac1WeaponId Selected, int BombAmmo, int? HeldNativeId, bool WrenchVisible,
    bool BombVisible, bool WrenchActive, int AudioMovementAction, SourceWeaponKey? Current, ImmutableArray<SourceWeaponKey> Order);
public interface IDiagnosticControllerEquipment
{
    DiagnosticControllerEquipment Read();
    void Select(Rac1WeaponId weapon);
    void Cycle(int direction);
}
