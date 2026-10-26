using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Player;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed class GodotMovementPlayer(Func<PlayerHost?> read, Func<RuntimeWorld?> world) : IDiagnosticMovementPlayer
{
    private PlayerHost Player => read() is { } player && GodotObject.IsInstanceValid(player) ? player
        : throw new DiagnosticUnavailableException("A live movement diagnostic player is required.");
    internal static NVector3 Copy(Vector3 value) => new(value.X, value.Y, value.Z);
    internal static Vector3 Engine(NVector3 value) => new(value.X, value.Y, value.Z);
    public DiagnosticMovementState Read()
    {
        var p = Player;
        var capsule = p.GetChildren().OfType<CollisionShape3D>().SingleOrDefault()?.Shape as CapsuleShape3D;
        return new()
        {
            GlobalPosition = Copy(p.GlobalPosition),
            Rotation = Copy(p.Rotation),
            Velocity = Copy(p.Velocity),
            PositionDelta = Copy(p.GetPositionDelta()),
            RealVelocity = Copy(p.GetRealVelocity()),
            SpawnPosition = Copy(p.DevelopmentControls.SpawnPosition),
            Grounded = p.IsOnFloor(),
            FlyEnabled = p.DevelopmentControls.FlyEnabled,
            CapsuleRadius = capsule?.Radius,
            CapsuleHeight = capsule?.Height,
            HasNativeLocomotion = p.HasNativeLocomotion,
            MovementControllerLabel = p.MovementControllerLabel,
            FloorSnapLength = p.FloorSnapLength,
            FloorConstantSpeed = p.FloorConstantSpeed,
            FloorMaxAngle = p.FloorMaxAngle,
            MaxSlides = p.MaxSlides,
            InitialGroundSnapUpwardReach = p.InitialGroundSnapUpwardReach,
            AnimationState = p.AnimationState,
            Rac1AnalogueMagnitude = p.Rac1AnalogueMagnitude,
            Rac1TargetPlanarStep = p.Rac1TargetPlanarStep,
            Rac1ControllerPlanarStep = p.Rac1ControllerPlanarStep,
            Rac1MovementTargetYaw = p.Rac1MovementTargetYaw,
            Rac1CurrentYaw = p.Rac1CurrentYaw,
            Rac1YawVelocity = p.Rac1YawVelocity,
            ControlHeading = p.Rac1RuntimeCameraState?.ControlHeadingRadians ?? p.Rac1ControlYaw,
            MovementReproControlHeading = p.MovementReproControlHeading,
            Rac1MaxObservedSlideCollisions = p.Rac1MaxObservedSlideCollisions,
            Rac1AnimationHelperAdmitted = p.Rac1AnimationHelperAdmitted,
            Rac1AnimationScalarAtSelection = p.Rac1AnimationScalarAtSelection,
            Rac1ContactProcessedMotion = p.Rac1ContactProcessedMotion,
            Rac1LocomotionState = p.Rac1LocomotionState,
            Rac1MovementPhase = p.Rac1MovementPhase,
            Rac1YawMode = p.Rac1YawMode,
            Rac1GameplayAlive = p.Rac1GameplayAlive,
            Rac1RecoveredSupportGrounded = p.Rac1RecoveredSupportGrounded,
            Rac1TraversalAction = p.Rac1TraversalAction,
            Rac1TraversalAuthoredAnimationTime = p.Rac1TraversalAuthoredAnimationTime,
            Rac1TraversalAnimationCycleCompleted = p.Rac1TraversalAnimationCycleCompleted,
            Rac1ActiveBackpack = p.Rac1ActiveBackpack,
            Rac1ClankAvailable = p.Rac1ClankAvailable,
            Rac1DevelopmentPackPreview = p.Rac1DevelopmentPackPreview,
            Rac1DevelopmentSwingshotPreview = p.Rac1DevelopmentSwingshotPreview,
            Rac1Hanging = p.Rac1Hanging,
            Rac1IceActive = p.Rac1IceActive,
            Rac1SwingshotActive = p.Rac1SwingshotActive,
            Rac1SwingshotTargetIndex = p.Rac1SwingshotTargetIndex,
            Rac1SwimmingActive = p.Rac1SwimmingActive,
            Rac1SwimState = p.Rac1SwimState,
            Rac1WaterAnimationFacts = p.Rac1WaterAnimationFacts,
            Rac1SmallStepDecision = p.Rac1SmallStepDecision,
            HasActiveRecoveredCamera = p.HasActiveRecoveredCamera,
        };
    }
    public void ResetCollisionDiagnostics() => Player.ResetRac1CollisionDiagnostics();
    public IDisposable PreserveDevelopmentSettings()
    {
        var player = Player;
        var pack = player.Rac1ActiveBackpack; bool clank = player.Rac1ClankAvailable;
        int? preview = player.Rac1DevelopmentPackPreview;
        bool swingshot = player.Rac1DevelopmentSwingshotPreview, fly = player.DevelopmentControls.FlyEnabled;
        float reach = player.InitialGroundSnapUpwardReach;
        return new MovementFixtureLease(() =>
        {
            if (!GodotObject.IsInstanceValid(player)) { return; }
            player.Rac1ActiveBackpack = pack; player.Rac1ClankAvailable = clank;
            for (int i = 0; i < 3 && player.Rac1DevelopmentPackPreview != preview; i++) player.CycleRac1DevelopmentPack();
            if (player.Rac1DevelopmentSwingshotPreview != swingshot) { player.ToggleRac1DevelopmentSwingshot(); }
            player.DevelopmentControls.SetFlyEnabled(fly); player.InitialGroundSnapUpwardReach = reach;
        });
    }
    public void ResetToSpawn() => Player.DevelopmentControls.ResetToSpawn();
    public void ResetAt(NVector3 position) => Player.ApplyDevelopmentReset(Engine(position));
    public void SetPosition(NVector3 position) => Player.GlobalPosition = Engine(position);
    public void SetRotation(NVector3 rotation) => Player.Rotation = Engine(rotation);
    public void SetYaw(double yaw) => Player.Rac1CurrentYaw = yaw;
    public void SetBackpack(Rac1TraversalEquipmentId? backpack) => Player.Rac1ActiveBackpack = backpack;
    public void SetClank(bool available) => Player.Rac1ClankAvailable = available;
    public void CyclePack() => Player.CycleRac1DevelopmentPack();
    public void ToggleSwingshot() => Player.ToggleRac1DevelopmentSwingshot();
    public void SetSnapReach(float reach) => Player.InitialGroundSnapUpwardReach = reach;
    public void StageWater(NVector3 position) => Player.StageDevelopmentWaterPosition(Engine(position));
    public void ConfigureTargets(ImmutableArray<RuntimeDynamicObject> targets) => Player.ConfigureRac1TraversalTargets(targets);
    public void RestoreWorldTargets() => Player.ConfigureRac1TraversalTargets(world()?.DynamicObjects ?? []);
    public IDisposable SubscribeDrowned(Action action)
    {
        var player = Player;
        player.Rac1NovalisDrowned += action;
        return new MovementFixtureLease(() => player.Rac1NovalisDrowned -= action);
    }
}

internal sealed record GodotMovementAnimationBindings(Func<PlayerAvatarView?> View,
    Func<IPlayerAnimationPresentationController?> Controller, Func<IPlayerAnimationPresentationSink?> Sink,
    Func<int?> Sequence, Func<bool> Synchronized, Func<double> StandingHeight);
internal sealed class GodotMovementAnimation(GodotMovementAnimationBindings bindings) : IDiagnosticMovementAnimation
{
    public DiagnosticMovementAnimation Read()
    {
        var view = bindings.View(); var controller = bindings.Controller(); var native = controller as Rac1PlayerAnimationPresentationController;
        var current = controller?.Current; var pose = current?.SampledPose;
        return new(view is not null && GodotObject.IsInstanceValid(view), native is not null, bindings.Sink() is not null,
            bindings.Synchronized(), bindings.Sequence(), view?.CurrentFrame, view?.FrameCount, view?.FramesPerSecond,
            current?.Loop, current?.SourceStateKey, current?.SemanticState, native?.OrdinaryLocomotionSample,
            native?.OrdinaryLocomotionMultiplier, native?.OrdinaryStopSample, native?.OrdinaryStandingSample,
            native?.CrouchSample is not null, pose is null ? null : new(pose.FrameIndex, [.. pose.LocalPositions]),
            bindings.Sink()?.CurrentAnimationPresentation.SampledPose is not null, bindings.StandingHeight());
    }
}

internal sealed class GodotMovementGeometry(Node owner) : IDiagnosticGeometryFixture
{
    public IDiagnosticGeometryLease Create(string name, NVector3 position)
    {
        var node = new Node3D { Name = name, Position = GodotMovementPlayer.Engine(position) };
        owner.AddChild(node); return new GeometryLease(node);
    }
    private sealed class GeometryLease(Node3D node) : IDiagnosticGeometryLease
    {
        public NVector3 GlobalPosition => GodotMovementPlayer.Copy(node.GlobalPosition);
        public void AddMesh(string name, int rawFaceType, params (NVector3 A, NVector3 B, NVector3 C)[] triangles) =>
            GodotRac1DiagnosticSurface.Add(node, name, rawFaceType,
                triangles.Select(t => (GodotMovementPlayer.Engine(t.A), GodotMovementPlayer.Engine(t.B), GodotMovementPlayer.Engine(t.C))).ToArray());
        public IDisposable AddBox(string name, NVector3 size, NVector3 position, float yaw)
        {
            var box = new StaticBody3D { Name = name };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = GodotMovementPlayer.Engine(size) } });
            node.AddChild(box); box.GlobalPosition = GodotMovementPlayer.Engine(position); box.Rotation = new(0, yaw, 0);
            return new MovementFixtureLease(() => { if (GodotObject.IsInstanceValid(box) && !box.IsQueuedForDeletion()) { box.QueueFree(); } });
        }
        public void Dispose() { if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion()) { node.QueueFree(); } }
    }
}
internal sealed class MovementFixtureLease(Action release) : IDisposable
{
    private Action? _release = release;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
