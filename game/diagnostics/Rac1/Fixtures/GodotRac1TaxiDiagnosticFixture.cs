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

/// <summary>
/// Diagnostics observe immutable cab/passenger presentation snapshots and the
/// native route session separately. They never borrow a private motion roster.
/// </summary>
internal sealed class GodotRac1TaxiDiagnosticBindings
{
    internal required Func<Rac1MetropolisTaxiHost?> Host { get; init; }
    internal required Func<PlayerHost?> Player { get; init; }
}

internal sealed class GodotRac1TaxiDiagnosticFixture(GodotRac1TaxiDiagnosticBindings bindings,
    IDiagnosticWorldObservations worlds) : IRac1TaxiDiagnosticFixture
{
    private readonly GodotRac1DiagnosticWorldScope _scope = new(worlds);
    private PlayerHost Player => bindings.Player() ?? throw new DiagnosticUnavailableException("The taxi player is unavailable.");
    private Rac1MetropolisTaxiHost Host => bindings.Host() ?? throw new DiagnosticUnavailableException("The taxi world host is unavailable.");
    private Rac1MetropolisTaxiSession Session => Host.NativeSession ?? throw new DiagnosticUnavailableException("The retail taxi session is unavailable.");

    private ActorInstanceKey Key(Rac1MetropolisTaxiStop stop)
    {
        var world = _scope.RequireCurrent();
        return new(world.WorldInstanceId!, world.Generation, new(ObpSourceGame.Rac1, stop.NativeClassId, stop.InstanceIndex));
    }

    private Rac1MetropolisTaxiStop Resolve(ActorInstanceKey actor)
    {
        Rac1DiagnosticActorScope.RequireCurrent(actor, _scope.RequireCurrent());
        return Session.Stops.Single(stop => stop.InstanceIndex == actor.Target.InstanceIndex &&
            stop.NativeClassId == actor.Target.NativeClassId);
    }

    public ImmutableArray<Rac1TaxiStopDiagnostic> ReadStops()
    {
        _scope.RequireCurrent();
        return [.. Session.Stops.Select(stop =>
        {
            var dock = stop.NativeShuttleDock; var hud = stop.NativeHudElementLease?.FocusCommand();
            return new Rac1TaxiStopDiagnostic(Key(stop), GodotMovementPlayer.Copy(Rac1MetropolisTaxiHost.FromTaxiPoint(stop.Position)),
                stop.OutboundPath.Count > 0 ? GodotMovementPlayer.Copy(Rac1MetropolisTaxiHost.FromTaxiPoint(stop.OutboundPath[^1])) : null,
                stop.ApproachTrigger is { } trigger ? GodotMovementPlayer.Copy(Rac1MetropolisTaxiHost.FromTaxiPoint(trigger.Center)) : null,
                stop.ApproachPath?.Count ?? 0, stop.NativeHoverPhases.HasValue, dock is not null, dock?.OptionalCuboidSelector,
                dock is null ? [] : [new(0, GodotMovementPlayer.Copy(Rac1MetropolisTaxiHost.FromTaxiPoint(dock.SideZeroPath[0]))),
                    new(1, GodotMovementPlayer.Copy(Rac1MetropolisTaxiHost.FromTaxiPoint(dock.SideOnePath[0])))],
                stop.NativeHudSelectors?.MessageForCurrentSide(1),
                hud is { } command ? new(command.Kind, command.SlotId, command.Key, command.InitializeAddress) : null);
        })];
    }

    public Rac1TaxiPassengerDiagnostic Read()
    {
        var world = _scope.RequireCurrent();
        var player = Player;
        var taxi = Host;
        var snapshot = taxi.ReadSnapshot();
        return new(world, snapshot.Nodes, snapshot.Hulls, snapshot.Hovers, snapshot.SessionPresent,
            snapshot.Riding, snapshot.Returning, snapshot.PendingStop, snapshot.HeroTargetState,
            snapshot.Flying, snapshot.RiderFrame, snapshot.CameraWorldOwned,
            player.ProcessMode == Node.ProcessModeEnum.Disabled, player.Rac1TaxiWalking,
            player.VisualRoot.Visible, player.Camera.TopLevel,
            GodotMovementPlayer.Copy(player.Camera.GlobalPosition - player.GlobalPosition),
            snapshot.ExpectedPassengerPosition, snapshot.CabBasisOrthonormal, snapshot.BoardableAtPlayer,
            taxi.HudLine());
    }

    public Rac1TaxiCabDiagnostic ReadCab(ActorInstanceKey actor)
    {
        var stop = Resolve(actor);
        var cab = Host.ReadSnapshot().Cabs.SingleOrDefault(c =>
            c.InstanceIndex == stop.InstanceIndex && c.NativeClassId == stop.NativeClassId)
            ?? throw new DiagnosticUnavailableException("The requested taxi cab has retired from the scene.");
        return new(cab.Position, cab.Nose, cab.Phase, cab.CollisionLayer);
    }

    public double SourceDistanceSquared(ActorInstanceKey actor) =>
        Rac1MetropolisTaxiHost.ToTaxiPoint(Player.GlobalPosition).DistanceSquared(Resolve(actor).Position);

    public Rac1TaxiLandingDiagnostic ProbeLanding(NVector3 waypoint)
    {
        _scope.RequireCurrent();
        bool supported = Host.ProbeLanding(GodotMovementPlayer.Engine(waypoint), out var landing);
        return new(supported, GodotMovementPlayer.Copy(landing));
    }

    public string ReadRetailText(int id)
    {
        _scope.RequireCurrent(); const string unavailable = "\0missing-retail-taxi-text\0";
        string text = Rac1RetailUiAssets.Text(id, unavailable);
        return text == unavailable ? throw new DiagnosticUnavailableException("The original retail taxi HUD text is unavailable.") : text;
    }

    public void SetTimeScale(double value) { _scope.RequireCurrent(); Engine.TimeScale = value; }

    public void SuspendAtApproach(ActorInstanceKey actor)
    {
        var stop = Resolve(actor);
        Player.ProcessMode = Node.ProcessModeEnum.Disabled;
        Player.GlobalPosition = Rac1MetropolisTaxiHost.FromTaxiPoint(
            stop.ApproachTrigger?.Center ?? throw new DiagnosticUnavailableException("The taxi approach trigger is unavailable."));
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
        _scope.RequireCurrent();
        using var input = new InputEventKey { Keycode = global::Godot.Key.T, Pressed = true };
        return Host.TryHandleInput(input);
    }

    public void Abort() { _scope.RequireCurrent(); Host.AbortRide(); }

    public IDisposable PreservePresentation()
    {
        _scope.RequireCurrent();
        var player = Player; var process = player.ProcessMode; double scale = Engine.TimeScale;
        return new Restore(() =>
        {
            try { _scope.RequireCurrent(); Host.AbortRide(); }
            catch (DiagnosticUnavailableException) { /* The world owner already retired its taxi. */ }
            finally
            {
                if (GodotObject.IsInstanceValid(player)) player.ProcessMode = process;
                Engine.TimeScale = scale;
            }
        });
    }

    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
}
