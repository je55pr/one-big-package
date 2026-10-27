using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed class GodotRac1CombatDiagnosticBindings
{
    internal required Func<ImmutableArray<RuntimeWorldScene.DynamicObjectNode>> Crates { get; init; }
    internal required Func<ImmutableArray<RuntimeWorldScene.DynamicObjectNode>> Hostiles { get; init; }
    internal required Func<RuntimeWorld?> World { get; init; }
    internal required Func<PlayerHost?> Player { get; init; }
    internal required Func<Rac1CombatOpeningDiagnostic> Opening { get; init; }
    internal required Func<Rac1CombatPopulationDiagnostic> Population { get; init; }
    internal required Func<Rac1CombatPresentationDiagnostic> Presentation { get; init; }
    internal required Func<Rac1CombatLootDiagnostic> Loot { get; init; }
    internal required Func<Rac1BombDiagnostic> Bombs { get; init; }
    internal required Func<IRac1BombDiagnosticWitness?> BombWitness { get; init; }
    internal required Func<RuntimeWorldScene.DynamicObjectNode, bool> Active { get; init; }
    internal required Func<RuntimeWorldScene.DynamicObjectNode, Vector3> Forward { get; init; }
    internal required Action<RuntimeWorldScene.DynamicObjectNode, RuntimeEntityPresence> Presence { get; init; }
    internal required Action PrimaryAttack { get; init; }
    internal required Func<Rac1DamageTransportSession> Damage { get; init; }
}
internal sealed class GodotRac1BombDiagnosticWitness(Func<Rac1BombWitnessDiagnostic> read) : IRac1BombDiagnosticWitness
{
    public Rac1BombWitnessDiagnostic Read() => read();
}

/// <summary>Only admitted R&C1 encounter/crate objects can be staged; object handles are checked against the live world generation.</summary>
internal sealed class GodotRac1CombatDiagnosticFixture(GodotRac1EnemyDiagnosticFixture enemies,
    IDiagnosticWorldObservations worlds, GodotRac1CombatDiagnosticBindings bindings) : IRac1CombatDiagnosticFixture
{
    private PlayerHost Player => bindings.Player() is { } actor && GodotObject.IsInstanceValid(actor) ? actor
        : throw new DiagnosticUnavailableException("The combat fixture player is unavailable.");
    private ActorInstanceKey Key(RuntimeWorldScene.DynamicObjectNode node)
    {
        var world = worlds.ReadWorld(); var source = node.Source;
        if (string.IsNullOrWhiteSpace(world.WorldInstanceId) || source.SourceGame != ObpSourceGame.Rac1)
            throw new DiagnosticUnavailableException("The source-qualified combat world is unavailable.");
        return new(world.WorldInstanceId, world.Generation, new(source.SourceGame, source.NativeClassId, source.InstanceIndex));
    }
    private RuntimeWorldScene.DynamicObjectNode Object(ActorInstanceKey key)
    {
        Rac1DiagnosticActorScope.RequireCurrent(key, worlds.ReadWorld());
        var node = bindings.Crates().Concat(bindings.Hostiles()).FirstOrDefault(value =>
            value.Source.NativeClassId == key.Target.NativeClassId && value.Source.InstanceIndex == key.Target.InstanceIndex);
        node ??= enemies.Resolve(key).Node;
        if (!GodotObject.IsInstanceValid(node.Root)) throw new DiagnosticUnavailableException("The combat fixture object was retired.");
        return node;
    }
    public Rac1CombatOpeningDiagnostic ReadOpening() => bindings.Opening();
    public Rac1CombatPopulationDiagnostic ReadPopulation() => bindings.Population();
    public Rac1CombatPresentationDiagnostic ReadPresentation() => bindings.Presentation();
    public Rac1CombatLootDiagnostic ReadLoot() => bindings.Loot();
    public Rac1BombDiagnostic ReadBombs() => bindings.Bombs();
    public IRac1BombDiagnosticWitness? CaptureBombWitness() => bindings.BombWitness();
    public ImmutableArray<ActorInstanceKey> ReadHostiles() => [.. bindings.Hostiles().Select(Key)];
    public ImmutableArray<ActorInstanceKey> ReadCrates() => [.. bindings.Crates().Select(Key)];
    public bool IsAvailable(ActorInstanceKey key)
    {
        try { Object(key); return true; }
        catch (DiagnosticUnavailableException) { return false; }
    }
    public Rac1DiagnosticObjectSnapshot ReadObject(ActorInstanceKey key)
    {
        var node = Object(key); var world = bindings.World();
        return new(key, GodotMovementPlayer.Copy(node.Root.GlobalPosition), GodotMovementPlayer.Copy(bindings.Forward(node)),
            node.Root.Visible, bindings.Active(node), world is not null && Rac1Class749Hostile.IsRecoveredVeldinPlacement(world.LevelId, node.Source));
    }
    public void StageObject(ActorInstanceKey key, NVector3 position) => Object(key).Root.GlobalPosition = GodotMovementPlayer.Engine(position);
    public void SetPresence(ActorInstanceKey key, RuntimeEntityPresence presence) => bindings.Presence(Object(key), presence);
    public void StagePlayerPosition(NVector3 position, bool clearVelocity)
    {
        var player = Player; player.GlobalPosition = GodotMovementPlayer.Engine(position);
        if (clearVelocity) player.Velocity = Vector3.Zero;
    }
    public void PlacePlayerFacing(NVector3 position, NVector3 direction, bool grounded)
    {
        direction.Y = 0;
        if (direction.LengthSquared() <= 1e-5f) throw new ArgumentException("RAC1 smoke facing direction must be planar and non-zero.");
        direction = NVector3.Normalize(direction);
        var player = Player; player.GlobalPosition = GodotMovementPlayer.Engine(position); player.Velocity = Vector3.Zero;
        player.Rac1CurrentYaw = Math.Atan2(direction.Z, -direction.X);
        if (!grounded) return;
        using var query = PhysicsRayQueryParameters3D.Create(player.GlobalPosition + Vector3.Up * 8, player.GlobalPosition + Vector3.Down * 128);
        query.Exclude = [player.GetRid()];
        var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) throw new InvalidOperationException($"No imported collision lies below wrench approach seed {position}.");
        Vector3 normal = (Vector3)hit["normal"];
        if (normal.Y < .5f) throw new InvalidOperationException($"Wrench approach seed {position} hit non-walkable normal {normal}.");
        player.GlobalPosition = (Vector3)hit["position"] + Vector3.Up * .08f; player.Velocity = Vector3.Zero;
    }
    public Rac1PlayerCameraDiagnostic ReadPlayerCamera()
    {
        var player = Player;
        return new(player.HasActiveRecoveredCamera, player.CameraControllerLabel, player.Rac1RuntimeCameraState is not null,
            GodotMovementPlayer.Copy(player.Camera.GlobalPosition), player.Rac1ControlYaw, player.Rac1CameraManualPitch);
    }
    public ImmutableArray<NVector3> ReadPlayerContactNormals()
    {
        var player = Player;
        return [.. Enumerable.Range(0, player.GetSlideCollisionCount()).Select(index => GodotMovementPlayer.Copy(player.GetSlideCollision(index).GetNormal()))];
    }
    public void RequestPrimaryAttack() => bindings.PrimaryAttack();
    public ImmutableArray<string> ReadPlayerContactDescriptions()
    {
        var player = Player;
        return [.. Enumerable.Range(0, player.GetSlideCollisionCount()).Select(index =>
        {
            var contact = player.GetSlideCollision(index);
            var collider = contact.GetCollider();
            return $"{(collider is Node node ? node.GetPath().ToString() : collider?.GetType().Name)} normal={contact.GetNormal()} position={contact.GetPosition()}";
        })];
    }
    public IDisposable SubscribeDamage(Action<Rac1GameplayDamageDispatch> observer)
    {
        var source = bindings.Damage(); source.Published += observer;
        return new MovementFixtureLease(() => source.Published -= observer);
    }
}
