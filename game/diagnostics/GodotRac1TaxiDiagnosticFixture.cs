using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal delegate bool Rac1TaxiLandingProbe(PlayerHost player, Vector3 point, out Vector3 landing);
internal sealed class GodotRac1TaxiDiagnosticBindings
{
    internal required Func<Rac1MetropolisTaxiSession?> Session { get; init; }
    internal required Func<IReadOnlyDictionary<int, RuntimeWorldScene.DynamicObjectNode>> Nodes { get; init; }
    internal required Func<IReadOnlyDictionary<int, StaticBody3D>> Hulls { get; init; }
    internal required Func<int> Hovers { get; init; }
    internal required Func<PlayerHost?> Player { get; init; }
    internal required Func<Rac1MetropolisTaxiStop?> Pending { get; init; }
    internal required Func<Rac1TaxiHeroTargetSession?> HeroTarget { get; init; }
    internal required Func<RuntimeWorldScene.DynamicObjectNode?> Flying { get; init; }
    internal required Func<Rac1TaxiPassengerFrame?> Rider { get; init; }
    internal required Func<bool> CameraOwned { get; init; }
    internal required Func<string> Hud { get; init; }
    internal required Func<InputEvent, bool> Input { get; init; }
    internal required Action Abort { get; init; }
    internal required Rac1TaxiLandingProbe Landing { get; init; }
    internal required Func<Vector3, Rac1TaxiPoint> ToNative { get; init; }
    internal required Func<Rac1TaxiPoint, Vector3> ToScene { get; init; }
    internal required Func<Basis, Rac1TaxiCabBasis> Basis { get; init; }
}
internal sealed class GodotRac1TaxiDiagnosticFixture(GodotRac1TaxiDiagnosticBindings bindings,
    IDiagnosticWorldObservations worlds) : IRac1TaxiDiagnosticFixture
{
    private readonly GodotRac1DiagnosticWorldScope _scope = new(worlds);
    private PlayerHost Player => bindings.Player() ?? throw new DiagnosticUnavailableException("The taxi player is unavailable.");
    private Rac1MetropolisTaxiSession Session => bindings.Session() ?? throw new DiagnosticUnavailableException("The retail taxi session is unavailable.");
    private ActorInstanceKey Key(Rac1MetropolisTaxiStop stop)
    {
        var world = _scope.RequireCurrent();
        return new(world.WorldInstanceId!, world.Generation, new(ObpSourceGame.Rac1, stop.NativeClassId, stop.InstanceIndex));
    }
    private Rac1MetropolisTaxiStop Resolve(ActorInstanceKey actor)
    {
        Rac1DiagnosticActorScope.RequireCurrent(actor, _scope.RequireCurrent());
        return Session.Stops.Single(stop => stop.InstanceIndex == actor.Target.InstanceIndex && stop.NativeClassId == actor.Target.NativeClassId);
    }
    public ImmutableArray<Rac1TaxiStopDiagnostic> ReadStops()
    {
        _scope.RequireCurrent();
        return [.. Session.Stops.Select(stop =>
        {
            var dock = stop.NativeShuttleDock; var hud = stop.NativeHudElementLease?.FocusCommand();
            return new Rac1TaxiStopDiagnostic(Key(stop), GodotMovementPlayer.Copy(bindings.ToScene(stop.Position)),
                stop.OutboundPath.Count > 0 ? GodotMovementPlayer.Copy(bindings.ToScene(stop.OutboundPath[^1])) : null,
                stop.ApproachTrigger is { } trigger ? GodotMovementPlayer.Copy(bindings.ToScene(trigger.Center)) : null,
                stop.ApproachPath?.Count ?? 0, stop.NativeHoverPhases.HasValue, dock is not null, dock?.OptionalCuboidSelector,
                dock is null ? [] : [new(0, GodotMovementPlayer.Copy(bindings.ToScene(dock.SideZeroPath[0]))), new(1, GodotMovementPlayer.Copy(bindings.ToScene(dock.SideOnePath[0])))],
                stop.NativeHudSelectors?.MessageForCurrentSide(1), hud is { } command ? new(command.Kind, command.SlotId, command.Key, command.InitializeAddress) : null);
        })];
    }
    public Rac1TaxiPassengerDiagnostic Read()
    {
        var world = _scope.RequireCurrent(); var player = Player; var session = bindings.Session();
        var cab = bindings.Flying(); var rider = bindings.Rider(); NVector3? expected = null; bool orthonormal = false;
        if (cab is not null)
        {
            var point = cab.Root.GlobalPosition; var basis = bindings.Basis(cab.Root.GlobalTransform.Basis); orthonormal = basis.IsOrthonormal();
            if (rider is not null) { var target = rider.PositionAt(new(point.X, point.Y, point.Z), basis); expected = new((float)target.X, (float)target.Y, (float)target.Z); }
        }
        return new(world, bindings.Nodes().Count, bindings.Hulls().Count, bindings.Hovers(), session is not null,
            session?.IsRiding == true, session?.IsReturning == true, bindings.Pending()?.InstanceIndex, bindings.HeroTarget()?.NativeState,
            cab is not null, rider is not null, bindings.CameraOwned(), player.ProcessMode == Node.ProcessModeEnum.Disabled,
            player.Rac1TaxiWalking, player.VisualRoot.Visible, player.Camera.TopLevel,
            GodotMovementPlayer.Copy(player.Camera.GlobalPosition - player.GlobalPosition), expected, orthonormal,
            session?.FindBoardable(bindings.ToNative(player.GlobalPosition)) is not null, bindings.Hud());
    }
    public Rac1TaxiCabDiagnostic ReadCab(ActorInstanceKey actor)
    {
        var stop = Resolve(actor); var node = bindings.Nodes()[stop.InstanceIndex];
        return new(GodotMovementPlayer.Copy(node.Root.GlobalPosition), GodotMovementPlayer.Copy(-node.Root.GlobalTransform.Basis.X),
            Session.PhaseOf(stop.InstanceIndex), bindings.Hulls()[stop.InstanceIndex].CollisionLayer);
    }
    public double SourceDistanceSquared(ActorInstanceKey actor) => bindings.ToNative(Player.GlobalPosition).DistanceSquared(Resolve(actor).Position);
    public Rac1TaxiLandingDiagnostic ProbeLanding(NVector3 waypoint)
    {
        _scope.RequireCurrent(); bool supported = bindings.Landing(Player, GodotMovementPlayer.Engine(waypoint), out var landing);
        return new(supported, GodotMovementPlayer.Copy(landing));
    }
    public string ReadRetailText(int id) { _scope.RequireCurrent(); return Rac1RetailUiAssets.Text(id, "Taxi: T / Triangle to ride"); }
    public void SetTimeScale(double value) { _scope.RequireCurrent(); Engine.TimeScale = value; }
    public void SuspendAtApproach(ActorInstanceKey actor)
    {
        var stop = Resolve(actor); Player.ProcessMode = Node.ProcessModeEnum.Disabled;
        Player.GlobalPosition = bindings.ToScene(stop.ApproachTrigger?.Center ?? throw new DiagnosticUnavailableException("The taxi approach trigger is unavailable."));
    }
    public void ResumeProcess() { _scope.RequireCurrent(); Player.ProcessMode = Node.ProcessModeEnum.Inherit; }
    public void StageAtSource(ActorInstanceKey actor, bool reverseGroundingLift)
    {
        var stop = Resolve(actor);
        Player.ApplyRac1TaxiArrival(new RuntimeSpawn(stop.Position.X,
            stop.Position.Y - (reverseGroundingLift ? RuntimeSpawnSceneAdapter.GroundingLift : 0), stop.Position.Z, Player.Rac1CurrentYaw));
    }
    public bool RequestBoarding()
    {
        _scope.RequireCurrent(); using var input = new InputEventKey { Keycode = global::Godot.Key.T, Pressed = true }; return bindings.Input(input);
    }
    public void Abort() { _scope.RequireCurrent(); bindings.Abort(); }
    public IDisposable PreservePresentation()
    {
        _scope.RequireCurrent(); var player = Player; var process = player.ProcessMode; double scale = Engine.TimeScale;
        return new Restore(() =>
        {
            try { _scope.RequireCurrent(); bindings.Abort(); }
            catch (DiagnosticUnavailableException) { /* The world owner already retired its taxi. */ }
            finally { if (GodotObject.IsInstanceValid(player)) player.ProcessMode = process; Engine.TimeScale = scale; }
        });
    }
    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
}
