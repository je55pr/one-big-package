using Godot;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal delegate bool GodotRac1AridiaSupport(Rac1HostedEnemy actor, Vector3 point, out float floor, out float normal);
internal sealed record GodotRac1AridiaDiagnosticBindings(Func<Rac1MobyRuntimeSession> Runtime,
    Func<Rac1AridiaEnemySession> Session, Action<Rac1AridiaEnemySession> ReplaceSession,
    Func<int> ClipCount, Func<int, NVector3> ClipCentre, Func<int, NVector3, bool> ClipContains,
    Func<int, Vector3?> Waypoint, Func<int, Vector3?> RoamingTarget, Func<int, int?> RoamExitFlags,
    Action<int> ClearRoamingTarget, Func<int, int> Lifts, Func<int, int> Sweeps, Func<int, float> VerticalStep,
    float HostUpdateRadius, Func<Rac1HostedEnemy, Vector3, float?> StateThreeFloor,
    Func<Rac1HostedEnemy, Vector3, float?> GroundLift, GodotRac1AridiaSupport Support,
    Func<Rac1HostedEnemy, Vector3, Rac1AridiaConstructorbotNative.WalkerPushoutFrame> SpherePushouts,
    Func<Rac1HostedEnemy, Vector3, bool> MoveContact, Action<Rac1HostedEnemy, int, int> SelectSequence,
    Action<int, Vector3> ActivateShark);

/// <summary>Only the existing Aridia native owner and scene contact adapters can mutate fixture state.</summary>
internal sealed class GodotRac1AridiaDiagnosticFixture(GodotRac1EnemyDiagnosticFixture actors,
    GodotRac1AridiaDiagnosticBindings bindings) : IRac1AridiaDiagnosticFixture
{
    private Rac1HostedEnemy Actor(ActorInstanceKey key)
    {
        if (key.Target.NativeClassId != Rac1AridiaEnemies.Constructorbot)
            throw new DiagnosticUnavailableException("The Aridia constructorbot fixture requires class612.");
        return actors.Resolve(key);
    }
    public float HostUpdateRadius => bindings.HostUpdateRadius;
    public Rac1ConstructorbotDiagnosticSnapshot ReadConstructorbot(ActorInstanceKey key)
    {
        var actor = Actor(key); var native = bindings.Runtime().Require(actor.Node.Source); int id = key.Target.InstanceIndex;
        return new(native.State.NativeState, bindings.Session().Health(id),
            Rac1AridiaConstructorbotNative.ReadWaypointRecordIndex(native.PVar.Span, bindings.ClipCount()),
            Rac1AridiaConstructorbotNative.ReadActivationVolume(native.PVar.Span),
            Rac1AridiaConstructorbotNative.ReadEngagementRadius(native.PVar.Span),
            bindings.Lifts(id), bindings.Sweeps(id), bindings.VerticalStep(id));
    }
    public NVector3 ReadClipCentreNative(int index) => bindings.ClipCentre(index);
    public bool ClipContainsNative(int index, NVector3 point) => bindings.ClipContains(index, point);
    public bool TryReadAuthoredWaypoint(ActorInstanceKey key, out NVector3 point)
    { Actor(key); var value = bindings.Waypoint(key.Target.InstanceIndex); point = value is { } p ? GodotMovementPlayer.Copy(p) : default; return value.HasValue; }
    public bool TryReadRoamingTarget(ActorInstanceKey key, out NVector3 point)
    { Actor(key); var value = bindings.RoamingTarget(key.Target.InstanceIndex); point = value is { } p ? GodotMovementPlayer.Copy(p) : default; return value.HasValue; }
    public bool HasRoamingTarget(ActorInstanceKey key) { Actor(key); return bindings.RoamingTarget(key.Target.InstanceIndex).HasValue; }
    public bool TryReadRoamExitFlags(ActorInstanceKey key, out int flags)
    { Actor(key); var value = bindings.RoamExitFlags(key.Target.InstanceIndex); flags = value.GetValueOrDefault(); return value.HasValue; }
    public bool HasRoamExitFlags(ActorInstanceKey key) { Actor(key); return bindings.RoamExitFlags(key.Target.InstanceIndex).HasValue; }
    public void ClearRoamingTarget(ActorInstanceKey key) { Actor(key); bindings.ClearRoamingTarget(key.Target.InstanceIndex); }
    public void StageNativeState(ActorInstanceKey key, int state) => bindings.Runtime().SetNativeState(bindings.Runtime().Require(Actor(key).Node.Source), state);
    public void SelectSequence(ActorInstanceKey key, int sequence, int blendTicks) => bindings.SelectSequence(Actor(key), sequence, blendTicks);
    public float? ProbeStateThreeFloor(ActorInstanceKey key, NVector3 point) => bindings.StateThreeFloor(Actor(key), GodotMovementPlayer.Engine(point));
    public float? ProbeGroundLift(ActorInstanceKey key, NVector3 point) => bindings.GroundLift(Actor(key), GodotMovementPlayer.Engine(point));
    public bool ProbeSupport(ActorInstanceKey key, NVector3 point, out float floor, out float normal) => bindings.Support(Actor(key), GodotMovementPlayer.Engine(point), out floor, out normal);
    public Rac1AridiaConstructorbotNative.WalkerPushoutFrame ProbeSpherePushouts(ActorInstanceKey key, NVector3 point) => bindings.SpherePushouts(Actor(key), GodotMovementPlayer.Engine(point));
    public bool MoveWalkerContact(ActorInstanceKey key, NVector3 motion) => bindings.MoveContact(Actor(key), GodotMovementPlayer.Engine(motion));
    public IRac1SharkRecycleTrial BeginSharkRecycleTrial() => new SharkTrial(actors, bindings);

    private sealed class SharkTrial : IRac1SharkRecycleTrial
    {
        private readonly GodotRac1EnemyDiagnosticFixture _actors;
        private readonly GodotRac1AridiaDiagnosticBindings _bindings;
        private readonly Rac1AridiaEnemySession _original;
        private readonly Rac1AridiaEnemySession _fixture = new();
        private readonly Rac1HostedEnemy _shark;
        private readonly IDisposable _pose;
        private readonly Rac1MobyRuntimeSession _runtime;
        private readonly Rac1MobyRuntimeInstance _native;
        private readonly int _state, _sequence, _deathTicks;
        private bool _disposed;
        public Rac1SharkRecycleStart Start { get; }
        public SharkTrial(GodotRac1EnemyDiagnosticFixture actors, GodotRac1AridiaDiagnosticBindings bindings)
        {
            _actors = actors; _bindings = bindings;
            var keys = actors.ReadEnemies();
            var shark = keys.First(key => key.Target.NativeClassId == Rac1AridiaEnemies.Sandshark);
            var nest = keys.First(key => key.Target.NativeClassId == Rac1AridiaEnemies.Nest);
            _shark = actors.Resolve(shark); _original = bindings.Session();
            Start = new(shark, nest, GodotMovementPlayer.Copy(_shark.AuthoredHomePosition), GodotMovementPlayer.Copy(_shark.Node.Root.GlobalPosition));
            _runtime = bindings.Runtime(); _native = _runtime.Require(_shark.Node.Source);
            _state = _native.State.NativeState; _sequence = _shark.Animation.Sequence; _deathTicks = _shark.DeathTicks;
            _fixture.Register(nest.Target.InstanceIndex, Rac1AridiaEnemies.Nest, groupId: 7);
            _fixture.Register(shark.Target.InstanceIndex, Rac1AridiaEnemies.Sandshark, groupId: 7);
            _fixture.SetAuthoredGroupRoster(new Dictionary<int, int[]> { [7] = [shark.Target.InstanceIndex] });
            _pose = actors.PreserveActorPose(shark);
            bindings.ReplaceSession(_fixture);
        }
        private void RequireCurrent()
        { ObjectDisposedException.ThrowIf(_disposed, this); _actors.Resolve(Start.Shark); }
        public Rac1SharkRetirement StageDeathAndRetire(NVector3 point)
        {
            RequireCurrent(); _actors.StageRoot(Start.Shark, point); _actors.StageBody(Start.Shark, point);
            bool damaged = _fixture.Damage(Start.Shark.Target.InstanceIndex, 1);
            bool retired = damaged && _fixture.RetireToNestPool(Start.Shark.Target.InstanceIndex);
            return new(damaged, retired, retired ? _fixture.FindWaitingSharkForNest(Start.Nest.Target.InstanceIndex) : null);
        }
        public void ActivateFromNest(NVector3 point)
        { RequireCurrent(); _bindings.ActivateShark(Start.Nest.Target.InstanceIndex, GodotMovementPlayer.Engine(point)); }
        public Rac1SharkRecycleFrame Step(NVector3 nativePosition)
        {
            RequireCurrent();
            return new(_fixture.Step(Start.Shark.Target.InstanceIndex,
                new(nativePosition, nativePosition, PlayerAlive: false, LineOfSight: false)), _native.State.NativeState);
        }
        public NVector3 ReadAuthoredHomeScene() { RequireCurrent(); return GodotMovementPlayer.Copy(_shark.AuthoredHomePosition); }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            List<Action> restorations = [() => { if (ReferenceEquals(_bindings.Session(), _fixture)) _bindings.ReplaceSession(_original); }];
            bool live = true;
            try { _actors.Resolve(Start.Shark); } catch (DiagnosticUnavailableException) { live = false; }
            if (live)
            {
                restorations.Add(_pose.Dispose); restorations.Add(() => _shark.DeathTicks = _deathTicks);
                restorations.Add(() => _runtime.SetNativeState(_native, _state));
                restorations.Add(() => { if (_shark.Animation.Sequence != _sequence && _shark.Animation.SupportsSequence(_sequence)) _bindings.SelectSequence(_shark, _sequence, 1); });
            }
            DiagnosticCleanup.Run("Aridia shark fixture cleanup failed.", [.. restorations]);
        }
    }
}
