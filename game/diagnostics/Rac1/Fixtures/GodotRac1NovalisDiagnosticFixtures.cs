using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

internal sealed class GodotRac1NovalisRouteFixture(Func<IReadOnlyList<(Rac1NovalisMechanism Mechanism, RuntimeWorldScene.DynamicObjectNode Node)>> mechanisms,
    Func<PlayerHost?> player, IDiagnosticWorldObservations worlds) : IRac1NovalisRouteFixture
{
    private readonly GodotRac1DiagnosticWorldScope _scope = new(worlds);
    private ActorInstanceKey Key(Rac1NovalisMechanism mechanism)
    {
        var world = _scope.RequireCurrent();
        return new(world.WorldInstanceId!, world.Generation, new(ObpSourceGame.Rac1, mechanism.NativeClassId, mechanism.InstanceIndex));
    }
    public int MechanismCount { get { _scope.RequireCurrent(); return mechanisms().Count; } }
    public ImmutableArray<Rac1NovalisRockDiagnostic> ReadRocks()
    {
        _scope.RequireCurrent();
        return [.. mechanisms().Where(item => item.Mechanism is Rac1NovalisFallingRock).Select(item =>
            new Rac1NovalisRockDiagnostic(Key(item.Mechanism), item.Mechanism.State, item.Mechanism.Position, item.Mechanism.Home))];
    }
    private (Rac1NovalisMechanism Mechanism, RuntimeWorldScene.DynamicObjectNode Node) Resolve(int index)
    {
        _scope.RequireCurrent();
        return mechanisms().Single(item => item.Mechanism.InstanceIndex == index);
    }
    public Rac1NovalisPistonDiagnostic ReadPiston(int instanceIndex)
    {
        var (mechanism, node) = Resolve(instanceIndex); var piston = (Rac1NovalisPiston)mechanism;
        return new(Key(piston), piston.End, piston.Timer, GodotMovementPlayer.Copy(node.Root.GlobalPosition));
    }
    public Rac1NovalisDoorDiagnostic ReadDoor(int instanceIndex)
    {
        var (mechanism, node) = Resolve(instanceIndex); var door = (Rac1NovalisProximityDoor)mechanism;
        return new(Key(door), door.State, door.Progress, new(-door.Home.X, door.Home.Z, door.Home.Y), GodotMovementPlayer.Copy(node.Root.GlobalPosition));
    }
    private Rac1RouteRayDiagnostic Probe(ActorInstanceKey actor, Func<Rac1NovalisMechanism, Node3D, PhysicsRayQueryParameters3D> ray)
    {
        Rac1DiagnosticActorScope.RequireCurrent(actor, _scope.RequireCurrent());
        var (mechanism, node) = Resolve(actor.Target.InstanceIndex);
        if (mechanism.NativeClassId != actor.Target.NativeClassId) throw new DiagnosticUnavailableException("The route actor identity changed.");
        var hero = player() ?? throw new DiagnosticUnavailableException("The route player is unavailable.");
        using var query = ray(mechanism, node.Root); query.Exclude = new global::Godot.Collections.Array<Rid> { hero.GetRid() };
        var hit = hero.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return new(false, null, null, default);
        var target = RuntimeWorldScene.FindDynamicObjectOwner(hit["collider"].As<Node>());
        return new(true, target?.InstanceIndex, target?.NativeClassId, GodotMovementPlayer.Copy(hit["position"].AsVector3()));
    }
    public Rac1RouteRayDiagnostic ProbePiston(ActorInstanceKey actor) => Probe(actor, (_, node) =>
        PhysicsRayQueryParameters3D.Create(node.GlobalPosition + Vector3.Up * 8, node.GlobalPosition - Vector3.Up * 2));
    public Rac1RouteRayDiagnostic ProbeDoor(ActorInstanceKey actor) => Probe(actor, (mechanism, _) =>
    {
        var door = (Rac1NovalisProximityDoor)mechanism; Vector3 centre = new(-door.Home.X, door.Home.Z, door.Home.Y);
        Vector3 across = new(MathF.Sin(door.NativeYaw), 0, MathF.Cos(door.NativeYaw));
        return PhysicsRayQueryParameters3D.Create(centre + Vector3.Up * .8f - across * 3, centre + Vector3.Up * .8f + across * 3);
    });
}
internal sealed class GodotRac1NovalisMusicFixture(IDiagnosticWorldObservations worlds, Func<Rac1NovalisMusicDiagnostic> read,
    Action<int> crank, Action<double> tick) : IRac1NovalisMusicFixture
{
    private readonly GodotRac1DiagnosticWorldScope _scope = new(worlds);
    public Rac1NovalisMusicDiagnostic Read() { _scope.RequireCurrent(); return read(); }
    public void SelectAuthoredCrank(int instanceIndex) { _scope.RequireCurrent(); crank(instanceIndex); }
    public void Tick(double delta) { _scope.RequireCurrent(); tick(delta); }
}
