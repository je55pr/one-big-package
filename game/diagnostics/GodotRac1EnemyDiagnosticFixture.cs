using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed record GodotRac1EnemyDiagnosticBindings(Func<ImmutableArray<Rac1HostedEnemy>> Enemies,
    Func<Rac1Class1440Session?> Robots, Func<Rac1NovalisPeckbotSession?> Peckbots,
    Func<Rac1NovalisBirdbotSession?> Birdbots, Func<Rac1Class749HostileSession> Toads,
    Func<Rac1EnemyPopulationSnapshot> Population, Func<PlayerHost?> Player,
    Func<RuntimeWorldScene.DynamicObjectNode, bool> Strike, Func<Rac1HostedEnemy, float, float, bool> Ground,
    Func<RuntimeWorldScene.DynamicObjectNode, Vector3> Forward, Func<Rac1HostedEnemy, Vector3> Socket,
    Func<Rac1HostedEnemy, Vector3, Vector3, bool> RayTouchesPlayer);

/// <summary>Maps live source actors to copied facts. Engine/native objects remain inside this fixture.</summary>
internal sealed class GodotRac1EnemyDiagnosticFixture(GodotRac1EnemyDiagnosticBindings bindings, IDiagnosticWorldObservations worlds)
    : IRac1EnemyDiagnosticFacts, IRac1EnemyDiagnosticActions
{
    private ActorInstanceKey Key(Rac1HostedEnemy enemy)
    {
        var world = worlds.ReadWorld(); var source = enemy.Node.Source;
        if (world.WorldInstanceId is null || source.SourceGame != ObpSourceGame.Rac1)
            throw new DiagnosticUnavailableException("The source-qualified R&C1 enemy world is unavailable.");
        return new(world.WorldInstanceId, world.Generation, new(source.SourceGame, source.NativeClassId, source.InstanceIndex));
    }
    private Rac1HostedEnemy Enemy(ActorInstanceKey key)
    {
        var world = worlds.ReadWorld();
        Rac1DiagnosticActorScope.RequireCurrent(key, world);
        var enemy = bindings.Enemies().SingleOrDefault(value => value.Node.Source.NativeClassId == key.Target.NativeClassId &&
            value.Node.Source.InstanceIndex == key.Target.InstanceIndex);
        if (enemy is null || !GodotObject.IsInstanceValid(enemy.Node.Root) || !GodotObject.IsInstanceValid(enemy.Body))
            throw new DiagnosticUnavailableException("The R&C1 diagnostic enemy is unavailable.");
        return enemy;
    }
    private static NVector3 Copy(Vector3 value) => GodotMovementPlayer.Copy(value);
    private static Vector3 Engine(NVector3 value) => GodotMovementPlayer.Engine(value);
    internal Rac1HostedEnemy Resolve(ActorInstanceKey key) => Enemy(key);
    public IDisposable PreserveActorPose(ActorInstanceKey key)
    {
        var enemy = Enemy(key);
        var root = enemy.Node.Root.GlobalPosition; var body = enemy.Body.GlobalPosition;
        bool visible = enemy.Node.Root.Visible, grounded = enemy.Grounded, pending = enemy.NeedsGroundPlacement, buried = enemy.Buried;
        uint layer = enemy.Body.CollisionLayer, mask = enemy.Body.CollisionMask;
        return new MovementFixtureLease(() =>
        {
            try { Rac1DiagnosticActorScope.RequireCurrent(key, worlds.ReadWorld()); }
            catch (DiagnosticUnavailableException) { return; }
            if (!GodotObject.IsInstanceValid(enemy.Node.Root) || !GodotObject.IsInstanceValid(enemy.Body)) return;
            enemy.Node.Root.GlobalPosition = root; enemy.Body.GlobalPosition = body;
            enemy.Node.Root.Visible = visible; enemy.Body.CollisionLayer = layer; enemy.Body.CollisionMask = mask;
            enemy.Grounded = grounded; enemy.NeedsGroundPlacement = pending; enemy.Buried = buried;
        });
    }
    private Rac1HostedEnemy Enemy(ActorInstanceKey key, int nativeClass)
    {
        if (key.Target.NativeClassId != nativeClass)
            throw new DiagnosticUnavailableException("The diagnostic actor belongs to a different native family.");
        return Enemy(key);
    }
    public IDisposable PreservePlayerPose()
    {
        var actor = bindings.Player() ?? throw new DiagnosticUnavailableException("The encounter fixture player is unavailable.");
        var world = worlds.ReadWorld();
        var position = actor.GlobalPosition; var rotation = actor.Rotation; double yaw = actor.Rac1CurrentYaw;
        return new MovementFixtureLease(() =>
        {
            var live = worlds.ReadWorld();
            if (!GodotObject.IsInstanceValid(actor) || live.WorldInstanceId != world.WorldInstanceId || live.Generation != world.Generation) return;
            actor.ApplyDevelopmentReset(position); actor.Rotation = rotation; actor.Rac1CurrentYaw = yaw;
        });
    }
    public Rac1EnemyPopulationSnapshot ReadPopulation() => bindings.Population();
    public ImmutableArray<ActorInstanceKey> ReadEnemies() => [.. bindings.Enemies().Select(Key)];
    public Rac1EnemyDiagnosticSnapshot ReadEnemy(ActorInstanceKey key)
    {
        var enemy = Enemy(key);
        return new(key, Copy(enemy.Node.Root.GlobalPosition), Copy(enemy.Body.GlobalPosition), Copy(enemy.AuthoredHomePosition),
            Copy(bindings.Forward(enemy.Node)), Copy(bindings.Socket(enemy)), enemy.Node.Root.Visible,
            enemy.Body.CollisionLayer, enemy.Body.CollisionMask, enemy.Grounded, enemy.Body.IsOnFloor(),
            enemy.NeedsGroundPlacement, enemy.Buried, enemy.DeathPresented, enemy.Flame is not null,
            enemy.Node.AnimationSample, enemy.Animation.Sequence, enemy.Animation.Position, enemy.Animation.Blending);
    }
    public Rac1Class1440DiagnosticSnapshot ReadRobot(ActorInstanceKey key)
    {
        var actor = Enemy(key, 1440);
        var state = (bindings.Robots() ?? throw new DiagnosticUnavailableException("The class1440 controller is unavailable.")).Get(actor.Node.Source.InstanceIndex);
        return new(state.Health, state.NativeState, state.Sequence, state.HitCooldown, state.HasAttackSlot, state.ReactionActive);
    }
    public Rac1PeckbotDiagnosticSnapshot ReadPeckbot(ActorInstanceKey key)
    {
        var actor = Enemy(key, Rac1NovalisPeckbotSession.ClassId);
        var state = (bindings.Peckbots() ?? throw new DiagnosticUnavailableException("The Peckbot controller is unavailable.")).Get(actor.Node.Source.InstanceIndex);
        return new(state.NativeState, state.Sequence, state.Health, state.InitialLoiter);
    }
    public Rac1BirdbotDiagnosticSnapshot ReadBirdbot(ActorInstanceKey key)
    {
        var actor = Enemy(key, 459);
        var state = (bindings.Birdbots() ?? throw new DiagnosticUnavailableException("The Birdbot controller is unavailable.")).Get(actor.Node.Source.InstanceIndex);
        return new(state.Mode, state.Sequence, state.Health);
    }
    public Rac1ToadDiagnosticSnapshot ReadToad(ActorInstanceKey key)
    {
        var actor = Enemy(key, Rac1Class749Hostile.NativeClassId); var state = bindings.Toads().Probe(actor.Node.Source);
        return new(state.NativeState, state.Health, state.NativeSequence, state.NativeSequenceUpdate, state.LinkedTargetInstanceIndex,
            state.NavigationYawOffset, state.EntityState.Presentation.Presence == RuntimeEntityPresence.Active,
            actor.Node.Root.Visible, state.ReactionActive, state.Attack);
    }
    public ImmutableArray<NVector3> ReadEntrancePath(ActorInstanceKey key)
    {
        var actor = Enemy(key);
        return [.. (bindings.Robots() ?? throw new DiagnosticUnavailableException("The class1440 controller is unavailable.")).EntrancePath(actor.Node.Source.InstanceIndex)];
    }
    public ImmutableArray<NVector3> ReadActivationRegion(ActorInstanceKey key)
    {
        var actor = Enemy(key);
        return [.. (bindings.Robots() ?? throw new DiagnosticUnavailableException("The class1440 controller is unavailable.")).ActivationRegion(actor.Node.Source.InstanceIndex)];
    }
    public bool SupportsSequence(ActorInstanceKey key, int sequence) => Enemy(key).Animation.SupportsSequence(sequence);
    public Rac1EnemyPerformanceSnapshot ReadPerformance() => new(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000,
        Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000);
    public void StageRoot(ActorInstanceKey key, NVector3 position) => Enemy(key).Node.Root.GlobalPosition = Engine(position);
    public void StageBody(ActorInstanceKey key, NVector3 position) => Enemy(key).Body.GlobalPosition = Engine(position);
    public void SetPlacementPending(ActorInstanceKey key, bool pending) => Enemy(key).NeedsGroundPlacement = pending;
    public void SetGrounded(ActorInstanceKey key, bool grounded) => Enemy(key).Grounded = grounded;
    public bool TryStrike(ActorInstanceKey key) => bindings.Strike(Enemy(key).Node);
    public bool TryGround(ActorInstanceKey key, float upwardReach, float downwardReach) => bindings.Ground(Enemy(key), upwardReach, downwardReach);
    public bool RayTouchesPlayer(ActorInstanceKey attacker, NVector3 from, NVector3 to) =>
        bindings.RayTouchesPlayer(Enemy(attacker), Engine(from), Engine(to));
    public float? MeasureGroundSeparation(ActorInstanceKey key)
    {
        var enemy = Enemy(key); var position = enemy.Body.GlobalPosition;
        var hit = Ray(Copy(position + Vector3.Up * 2), Copy(position + Vector3.Down * 8), ReadEnemies(), true);
        return hit is { Normal.Y: > .65f } ? position.Y - hit.Position.Y : null;
    }
    public Rac1DiagnosticRayHit? Ray(NVector3 from, NVector3 to, ImmutableArray<ActorInstanceKey> excluded, bool excludePlayer)
    {
        var player = bindings.Player() ?? throw new DiagnosticUnavailableException("The encounter contact-query player is unavailable.");
        using var query = PhysicsRayQueryParameters3D.Create(Engine(from), Engine(to));
        var ids = excluded.Select(key => Enemy(key).Body.GetRid());
        query.Exclude = new global::Godot.Collections.Array<Rid>(excludePlayer ? ids.Append(player.GetRid()) : ids);
        var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        var collider = hit.TryGetValue("collider", out var value) ? value.AsGodotObject() : null;
        var hitActor = bindings.Enemies().FirstOrDefault(enemy => ReferenceEquals(enemy.Body, collider));
        return hit.TryGetValue("position", out var point) && hit.TryGetValue("normal", out var normal)
            ? new(Copy(point.AsVector3()), Copy(normal.AsVector3()), hitActor is null ? null : Key(hitActor)) : null;
    }
}
