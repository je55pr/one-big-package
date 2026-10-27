using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed class GodotRac1QueenDiagnosticBindings
{
    internal required Func<Rac1FrogQueenGroup?> Group { get; init; }
    internal required Func<Rac1AlienQueenRecovery?> Recovery { get; init; }
    internal required Func<Rac1AlienQueenEntrance?> Entrance { get; init; }
    internal required Func<Rac1AlienQueenFlight?> Flight { get; init; }
    internal required Func<Rac1AlienQueenFlightCamera?> PendingCamera { get; init; }
    internal required Func<Rac1AlienQueenLinkedGate?> Linked { get; init; }
    internal required Func<Rac1AlienQueenGateNative?> Tuning { get; init; }
    internal required Func<Rac1MobySequenceClock?> Animation { get; init; }
    internal required Func<RuntimeWorldScene.DynamicObjectNode?> Node { get; init; }
    internal required Func<float> AuthoredNativeZ { get; init; }
    internal required Func<ImmutableDictionary<int, RuntimeWorldScene.DynamicObjectNode>> Parents { get; init; }
    internal required Func<ImmutableDictionary<int, Node3D>> Children { get; init; }
    internal required Func<Node3D?> WorldRoot { get; init; }
    internal required Func<PlayerHost?> Player { get; init; }
    internal required Func<Camera3D?> ActiveCamera { get; init; }
    internal required Func<Camera3D?> QueenCamera { get; init; }
    internal required Func<Camera3D?> GuidedCamera { get; init; }
    internal required Func<long> DamageSequence { get; init; }
    internal required Action<Rac1MobyRuntimeKey> ForgetWrenchContact { get; init; }
    internal required Action<Vector3, Vector3, Vector3, Vector3, float, Rac1WrenchDamageResult> ResolveWrench { get; init; }
    internal required Action InvalidateEquipmentGeometry { get; init; }
    internal required Func<bool> GeometryContainsQueen { get; init; }
    internal required Func<bool> AimTargetAdmitted { get; init; }
    internal required Action TickFlight { get; init; }
    internal required Action TickRecovery { get; init; }
    internal required Action TickLinked { get; init; }
    internal required Action SynchronizeLinked { get; init; }
    internal required Func<int, int, bool, int> ObserveFrogEvent { get; init; }
    internal required Action BeginGuided { get; init; }
    internal required Action EndGuided { get; init; }
    internal required Action EndQueen { get; init; }
}

/// <summary>Source Queen controllers and camera nodes stay in the engine adapter. Fixture leases never release another camera owner's lock.</summary>
internal sealed class GodotRac1QueenDiagnosticFixture(GodotRac1DiagnosticWorldScope scope, GodotRac1QueenDiagnosticBindings bindings)
    : IRac1QueenDiagnosticFixture
{
    private Camera3D? _conflict;
    private bool _ownedGuided;
    private static T? Live<T>(T? value) where T : GodotObject => value is not null && GodotObject.IsInstanceValid(value) ? value : null;
    private T Require<T>(Func<T?> read, string role) where T : class
    { scope.RequireCurrent(); return read() ?? throw new DiagnosticUnavailableException("The Queen " + role + " is unavailable."); }
    private RuntimeWorldScene.DynamicObjectNode Node
    {
        get
        {
            var node = Require(bindings.Node, "body");
            if (!GodotObject.IsInstanceValid(node.Root) || node.Source.SourceGame != ObpSourceGame.Rac1 || node.Source.NativeClassId != Rac1FrogQueenGroup.QueenClassId)
                throw new DiagnosticUnavailableException("The source Queen body was retired or changed family.");
            return node;
        }
    }
    private ActorInstanceKey Key
    {
        get { var world = scope.RequireCurrent(); var source = Node.Source; return new(world.WorldInstanceId!, world.Generation, new(source.SourceGame, source.NativeClassId, source.InstanceIndex)); }
    }
    private PlayerHost Player => Live(bindings.Player()) ?? throw new DiagnosticUnavailableException("The Queen fixture player is unavailable.");
    private Rac1AlienQueenLinkedGate Linked => Require(bindings.Linked, "linked native gate");
    public Rac1QueenGroupDiagnostic ReadGroup()
    {
        var group = Require(bindings.Group, "Frog group"); return new(Key, group.GroupIndex, [.. group.Members]);
    }
    public Rac1QueenRecoveryDiagnostic ReadRecovery()
    {
        var source = Require(bindings.Recovery, "recovery");
        return new(source.NativeState, source.Sequence, source.Health, source.Timer, source.Recovering, source.CanReceiveHostWeaponContact);
    }
    public Rac1QueenEntranceDiagnostic ReadEntrance()
    {
        var source = Require(bindings.Entrance, "entrance");
        return new(source.NativeState, source.FirstLinkedInstance, source.SecondLinkedInstance, source.LinkedEventSelector,
            source.CameraSelector, source.NativePathSelector, source.LinkedActivationRequired);
    }
    public Rac1QueenBodyDiagnostic ReadBody()
    {
        var node = Node; var visual = GodotRac1FrogDiagnosticFixture.Visual(node.Root);
        return new(Key, visual.Position, visual.MeshCentre ?? throw new DiagnosticUnavailableException("The original Queen mesh is unavailable."), bindings.AuthoredNativeZ());
    }
    public Rac1QueenFlightDiagnostic? ReadFlight()
    {
        scope.RequireCurrent(); var source = bindings.Flight();
        return source is null ? null : new(source.NativeState, source.Sequence, source.Z, source.CameraBlend, source.CameraCompletionCandidate);
    }
    public int? ReadAnimationSequence() { scope.RequireCurrent(); return bindings.Animation()?.Sequence; }
    public Rac1AlienQueenFlightCamera? ReadPendingCamera() { scope.RequireCurrent(); return bindings.PendingCamera(); }
    public Rac1AlienQueenGateNative ReadGateTuning() => Require(bindings.Tuning, "retail SDA tuning");
    public int OriginalParentCount => Linked.OriginalParentCount;
    public int RuntimeChildCount => Linked.RuntimeChildCount;
    public ImmutableArray<int> OriginalParentIds => [.. Linked.OriginalParentIds];
    private static Rac1QueenLinkedParentDiagnostic Copy(Rac1AlienQueenGateFrame frame) => new(frame.AuthoredInstance, frame.NativeState, frame.Visible, frame.NativePosition, [.. frame.Children]);
    public Rac1QueenLinkedParentDiagnostic ReadLinkedParent(int parent) => Copy(Linked.Get(parent));
    public Rac1QueenLinkedParentDiagnostic StepLinkedParent(int parent, NVector3 nativePlayer, bool globallyEnabled, float angularAdvance) => Copy(Linked.Step(parent, nativePlayer, globallyEnabled, angularAdvance));
    public void StepLinkedChild(int child, float angularAdvance) => Linked.StepChild(child, angularAdvance);
    public int ReadLinkedChildState(int child) => Linked.GetChildState(child);
    public bool ActivateLinkedParent(int parent) => Linked.ActivateFromQueen(parent);
    public Rac1DiagnosticVisual ReadLinkedParentVisual(int parent)
    {
        scope.RequireCurrent();
        return GodotRac1FrogDiagnosticFixture.Visual(bindings.Parents().TryGetValue(parent, out var node) ? node.Root
            : throw new DiagnosticUnavailableException("The original Queen linked parent is unavailable."));
    }
    public Rac1DiagnosticVisual? ReadLinkedChildVisual(int child)
    {
        scope.RequireCurrent();
        return bindings.Children().TryGetValue(child, out var node) && GodotObject.IsInstanceValid(node) ? GodotRac1FrogDiagnosticFixture.Visual(node) : null;
    }
    public ImmutableArray<Rac1DiagnosticVisual> ReadLinkedChildVisuals()
    { scope.RequireCurrent(); return [.. bindings.Children().Values.Select(GodotRac1FrogDiagnosticFixture.Visual)]; }
    public void TickLinkedChildren() { scope.RequireCurrent(); bindings.TickLinked(); }
    public void SynchronizeLinkedParents() { scope.RequireCurrent(); bindings.SynchronizeLinked(); }
    public Rac1AlienQueenEntranceTick StepWaiting(bool targetIsNativeClassZero, bool nativeModeGate, bool linkedActorAndCameraCallbacksCommitted = false) =>
        Require(bindings.Entrance, "entrance").StepWaiting(targetIsNativeClassZero, nativeModeGate, linkedActorAndCameraCallbacksCommitted);
    public Rac1AlienQueenEntranceTick ObserveLanding(bool nativeLandingConfirmed, bool firstCondition) => Require(bindings.Entrance, "entrance").ObserveLanding(nativeLandingConfirmed, firstCondition);
    public void AdmitRecoveryState(int state, int sequence) => Require(bindings.Recovery, "recovery").AdmitNativeState(state, sequence);
    public int ObserveFrogEvent(int state, int sequence, bool completed) { scope.RequireCurrent(); return bindings.ObserveFrogEvent(state, sequence, completed); }
    public long ReadDamageSequence() { scope.RequireCurrent(); return bindings.DamageSequence(); }
    public void ForgetWrenchContact() { var key = Key.Target; bindings.ForgetWrenchContact(new(key.NativeClassId, key.InstanceIndex)); }
    public void ResolveWrenchContact(Rac1DiagnosticWrenchPath path, Rac1WrenchDamageResult damage)
    {
        scope.RequireCurrent(); bindings.ResolveWrench(GodotMovementPlayer.Engine(path.GripStart), GodotMovementPlayer.Engine(path.TipStart),
            GodotMovementPlayer.Engine(path.GripEnd), GodotMovementPlayer.Engine(path.TipEnd), path.Radius, damage);
    }
    public void InvalidateEquipmentGeometry() { scope.RequireCurrent(); bindings.InvalidateEquipmentGeometry(); }
    public bool GeometryContainsQueen { get { scope.RequireCurrent(); return bindings.GeometryContainsQueen(); } }
    public bool AimTargetAdmitted { get { scope.RequireCurrent(); return bindings.AimTargetAdmitted(); } }
    public void TickFlight() { scope.RequireCurrent(); bindings.TickFlight(); }
    public void TickRecovery() { scope.RequireCurrent(); bindings.TickRecovery(); }
    private Rac1DiagnosticCameraOwner Owner(Camera3D? camera, Camera3D player, Camera3D? queen, Camera3D? guided)
    {
        if (camera is null) return Rac1DiagnosticCameraOwner.None;
        if (ReferenceEquals(camera, player)) return Rac1DiagnosticCameraOwner.Player;
        if (ReferenceEquals(camera, queen)) return Rac1DiagnosticCameraOwner.Queen;
        if (ReferenceEquals(camera, guided)) return Rac1DiagnosticCameraOwner.Guided;
        return ReferenceEquals(camera, Live(_conflict)) ? Rac1DiagnosticCameraOwner.Conflict : Rac1DiagnosticCameraOwner.Other;
    }
    public Rac1QueenCameraDiagnostic ReadCamera()
    {
        scope.RequireCurrent(); var player = Player;
        var camera = Live(player.Camera) ?? throw new DiagnosticUnavailableException("The Queen fixture player camera is unavailable.");
        var queen = Live(bindings.QueenCamera()); var guided = Live(bindings.GuidedCamera());
        var root = Live(bindings.WorldRoot()) ?? throw new DiagnosticUnavailableException("The Queen fixture world scene is unavailable.");
        return new(queen is not null, queen?.Current == true, guided is not null, guided?.Current == true,
            camera.Current, player.Rac1RemoteControlActive, Owner(Live(bindings.ActiveCamera()), camera, queen, guided),
            Owner(root.GetViewport().GetCamera3D(), camera, queen, guided));
    }
    public IDisposable PreserveCameraOwnership()
    {
        var saved = ReadCamera();
        return new MovementFixtureLease(() =>
        {
            try { scope.RequireCurrent(); } catch (DiagnosticUnavailableException) { return; }
            List<Exception>? failures = null;
            void Restore(Action action) { try { action(); } catch (Exception error) { (failures ??= []).Add(error); } }
            Restore(() => { if (!saved.GuidedPresent && _ownedGuided) EndGuidedCamera(); });
            Restore(() => { if (!saved.QueenPresent && Live(bindings.QueenCamera()) is not null) EndQueenCamera(); });
            if (failures is not null) throw new AggregateException("Queen camera fixture cleanup failed.", failures);
        });
    }
    public IDisposable CreateConflictingCamera()
    {
        scope.RequireCurrent();
        if (Live(_conflict) is not null) throw new InvalidOperationException("A conflicting fixture camera is already owned.");
        var root = Live(bindings.WorldRoot()) ?? throw new DiagnosticUnavailableException("The Queen fixture world is unavailable.");
        var camera = new Camera3D { Name = "QueenCameraOwnerConflictTest" };
        root.AddChild(camera); camera.MakeCurrent(); _conflict = camera;
        return new MovementFixtureLease(() =>
        {
            if (!GodotObject.IsInstanceValid(camera)) return;
            if (ReferenceEquals(camera.GetViewport().GetCamera3D(), camera) && Live(bindings.Player()) is { } player && Live(player.Camera) is { } playerCamera)
                playerCamera.MakeCurrent();
            camera.QueueFree(); if (ReferenceEquals(_conflict, camera)) _conflict = null;
        });
    }
    public void MakePlayerCameraCurrent() { scope.RequireCurrent(); Player.Camera.MakeCurrent(); }
    public void BeginGuidedCamera()
    {
        scope.RequireCurrent();
        if (Live(bindings.GuidedCamera()) is not null) throw new DiagnosticUnavailableException("Another guided-camera owner is already active.");
        _ownedGuided = true; bindings.BeginGuided();
    }
    public void EndGuidedCamera() { scope.RequireCurrent(); bindings.EndGuided(); _ownedGuided = false; }
    public void EndQueenCamera() { scope.RequireCurrent(); bindings.EndQueen(); }
}
