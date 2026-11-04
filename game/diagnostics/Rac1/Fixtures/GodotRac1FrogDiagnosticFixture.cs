using System.Collections.Immutable;
using Godot;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed class GodotRac1DiagnosticWorldScope(IDiagnosticWorldObservations worlds)
{
    private readonly DiagnosticWorldSnapshot _world = worlds.ReadWorld();
    internal DiagnosticWorldSnapshot RequireCurrent()
    {
        var live = worlds.ReadWorld();
        if (string.IsNullOrWhiteSpace(_world.WorldInstanceId) || live.WorldInstanceId != _world.WorldInstanceId || live.Generation != _world.Generation)
            throw new DiagnosticUnavailableException("The source encounter fixture belongs to a retired world.");
        return live;
    }
}

internal sealed class GodotRac1FrogDiagnosticBindings
{
    internal required Func<Rac1FrogSession?> Frogs { get; init; }
    internal required Func<Rac1FrogShuttleSwitch?> Shuttle { get; init; }
    internal required Func<Rac1FrogWaveTriggers?> Waves { get; init; }
    internal required Func<ImmutableArray<int>> WaveControllers { get; init; }
    internal required Func<ImmutableDictionary<int, Node3D>> Carriers { get; init; }
    internal required Func<int, bool> HasPickupModel { get; init; }
    internal required Func<(int Crates, int Pickups, int Debris)> Counts { get; init; }
    internal required Action<Rac1HostedEnemy> TickFrog { get; init; }
    internal required Action<Rac1HostedEnemy> TickCarrier { get; init; }
    internal required Func<Rac1HostedEnemy, bool> MeleeTouchesPlayer { get; init; }
    internal required Action<Vector3, Vector3, Vector3, Vector3, float, float> ResolveWrench { get; init; }
    internal required Action TickShuttle { get; init; }
    internal required Action ApplyShuttle { get; init; }
    internal required Action TickWaves { get; init; }
    internal required GodotRac1QueenDiagnosticBindings Queen { get; init; }
}

internal sealed class GodotRac1FrogDiagnosticFixture(GodotRac1EnemyDiagnosticFixture actors,
    GodotRac1DiagnosticWorldScope scope, GodotRac1FrogDiagnosticBindings bindings) : IRac1FrogDiagnosticFixture
{
    private GodotRac1QueenDiagnosticFixture? _queen;
    private Rac1FrogSession Frogs
    {
        get { scope.RequireCurrent(); return bindings.Frogs() ?? throw new DiagnosticUnavailableException("The native Frog controller is unavailable."); }
    }
    private Rac1HostedEnemy Frog(ActorInstanceKey key)
    {
        scope.RequireCurrent();
        if (key.Target.NativeClassId != Rac1FrogAuthored.ClassId)
            throw new DiagnosticUnavailableException("The Frog fixture requires its source class827 actor.");
        return actors.Resolve(key);
    }
    public Rac1FrogDiagnosticCounts ReadCounts()
    {
        var frogs = Frogs; var counts = bindings.Counts();
        return new(frogs.RegisteredCount, frogs.PresentedCount, bindings.Carriers().Count, counts.Crates, counts.Pickups, counts.Debris);
    }
    public Rac1FrogFrame Get(int nativeInstance) => Frogs.Get(nativeInstance);
    public Rac1FrogAuthoredDiagnostic GetAuthored(int nativeInstance)
    {
        var authored = Frogs.GetAuthored(nativeInstance); return new(authored.SwitchSelector, authored.TargetInstance);
    }
    internal static Rac1DiagnosticVisual Visual(Node3D node)
    {
        if (!GodotObject.IsInstanceValid(node)) throw new DiagnosticUnavailableException("The source encounter visual was retired.");
        var meshes = node.GetChildren().OfType<MeshInstance3D>().ToArray();
        var mesh = meshes.FirstOrDefault();
        return new(node.Visible, meshes.Any(value => value.Mesh is not null), GodotMovementPlayer.Copy(node.GlobalPosition),
            mesh is null ? null : GodotMovementPlayer.Copy((mesh.GlobalTransform * mesh.GetAabb()).GetCenter()));
    }
    public Rac1DiagnosticVisual? ReadCarrier(int nativeInstance)
    {
        scope.RequireCurrent();
        return bindings.Carriers().TryGetValue(nativeInstance, out var node) && GodotObject.IsInstanceValid(node) ? Visual(node) : null;
    }
    public bool HasPickupModel(int nativeClass) { scope.RequireCurrent(); return bindings.HasPickupModel(nativeClass); }
    public void TickFrog(ActorInstanceKey key) => bindings.TickFrog(Frog(key));
    public void TickCarrier(ActorInstanceKey key) => bindings.TickCarrier(Frog(key));
    public bool MeleeTouchesPlayer(ActorInstanceKey key) => bindings.MeleeTouchesPlayer(Frog(key));
    public void ResolveAnimatedWrenchContact(Rac1DiagnosticWrenchPath path, float nativeDamage)
    {
        scope.RequireCurrent(); bindings.ResolveWrench(GodotMovementPlayer.Engine(path.GripStart), GodotMovementPlayer.Engine(path.TipStart),
            GodotMovementPlayer.Engine(path.GripEnd), GodotMovementPlayer.Engine(path.TipEnd), path.Radius, nativeDamage);
    }
    public IRac1FrogShuttleDiagnosticFixture? Shuttle
    {
        get { scope.RequireCurrent(); return bindings.Shuttle() is null ? null : new ShuttleFixture(scope, bindings); }
    }
    public IRac1FrogWaveDiagnosticFixture? Waves
    {
        get { scope.RequireCurrent(); return bindings.Waves() is null ? null : new WaveFixture(scope, bindings); }
    }
    public IRac1QueenDiagnosticFixture? Queen
    {
        get { scope.RequireCurrent(); return bindings.Queen.Group() is null ? null : _queen ??= new(scope, bindings.Queen); }
    }
    private sealed class ShuttleFixture(GodotRac1DiagnosticWorldScope scope, GodotRac1FrogDiagnosticBindings bindings) : IRac1FrogShuttleDiagnosticFixture
    {
        private Rac1FrogShuttleSwitch Source
        { get { scope.RequireCurrent(); return bindings.Shuttle() ?? throw new DiagnosticUnavailableException("The Frog shuttle source is unavailable."); } }
        public NVector3 StopVolumeCentre(int stop) => Source.StopVolumeCentre(stop);
        public bool SwitchEnabled => Source.SwitchEnabled;
        public bool AcceptShuttleArrival(int stop) => Source.AcceptShuttleArrival(stop);
        public void TickSwitch() { scope.RequireCurrent(); bindings.TickShuttle(); }
        public void ApplySelection() { scope.RequireCurrent(); bindings.ApplyShuttle(); }
    }
    private sealed class WaveFixture(GodotRac1DiagnosticWorldScope scope, GodotRac1FrogDiagnosticBindings bindings) : IRac1FrogWaveDiagnosticFixture
    {
        private Rac1FrogWaveTriggers Source
        { get { scope.RequireCurrent(); return bindings.Waves() ?? throw new DiagnosticUnavailableException("The Frog wave source is unavailable."); } }
        public int RegisteredCount => Source.RegisteredCount;
        public ImmutableArray<int> Controllers { get { scope.RequireCurrent(); return bindings.WaveControllers(); } }
        public int Group(int instance) => Source.Group(instance);
        public int State(int instance) => Source.State(instance);
        public ImmutableArray<int> Members(int instance) => [.. Source.Members(instance)];
        public NVector3 ZoneCentre(int instance) => Source.ZoneCentre(instance);
        public NVector3 NativePosition(int instance) => Source.NativePosition(instance);
        public void Tick() { scope.RequireCurrent(); bindings.TickWaves(); }
    }
}
