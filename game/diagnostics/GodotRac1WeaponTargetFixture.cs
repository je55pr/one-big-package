using System.Collections.Immutable;
using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed record GodotRac1WeaponTargetBindings(Func<ImmutableArray<RuntimeWorldScene.DynamicObjectNode>> Crates,
    Func<ImmutableArray<Rac1HostedEnemy>> Enemies, Func<RuntimeWorldScene.DynamicObjectNode, bool> Active,
    Action<RuntimeWorldScene.DynamicObjectNode, Vector3> StageCrate, Func<int, float> RobotHealth);
internal sealed class GodotRac1WeaponTargetFixture(GodotRac1WeaponTargetBindings bindings, IDiagnosticWorldObservations worlds) : IRac1WeaponTargetFixture
{
    internal ActorInstanceKey Key(RuntimeWorldScene.DynamicObjectNode node)
    {
        var world = worlds.ReadWorld();
        if (string.IsNullOrWhiteSpace(world.WorldInstanceId) || node.Source.SourceGame != OBP.Core.ObpSourceGame.Rac1)
            throw new DiagnosticUnavailableException("The source-qualified R&C1 weapon fixture world is unavailable.");
        return new(world.WorldInstanceId, world.Generation, new(node.Source.SourceGame, node.Source.NativeClassId, node.Source.InstanceIndex));
    }
    private RuntimeWorldScene.DynamicObjectNode Resolve(ActorInstanceKey key)
    {
        Rac1DiagnosticActorScope.RequireCurrent(key, worlds.ReadWorld());
        var node = bindings.Crates().Concat(bindings.Enemies().Select(enemy => enemy.Node))
            .SingleOrDefault(node => node.Source.NativeClassId == key.Target.NativeClassId && node.Source.InstanceIndex == key.Target.InstanceIndex);
        if (node is null || !GodotObject.IsInstanceValid(node.Root)) throw new DiagnosticUnavailableException("The weapon fixture target was retired.");
        return node;
    }
    private Rac1WeaponTargetDiagnostic Snapshot(RuntimeWorldScene.DynamicObjectNode node, bool reward = false) => new(Key(node),
        GodotMovementPlayer.Copy(node.Root.GlobalPosition), bindings.Active(node), reward, Rac1BoltCrate.ReadAuthored(node.Source)?.RewardCentre);
    public ImmutableArray<Rac1WeaponTargetDiagnostic> ReadCrates() => [.. bindings.Crates().Select(node => Snapshot(node))];
    public ImmutableArray<Rac1WeaponTargetDiagnostic> ReadEnemies() => [.. bindings.Enemies().Select(enemy => Snapshot(enemy.Node, enemy.RewardEmitted))];
    public Rac1WeaponTargetDiagnostic Read(ActorInstanceKey key)
    {
        var node = Resolve(key);
        return Snapshot(node, bindings.Enemies().SingleOrDefault(enemy => ReferenceEquals(enemy.Node, node))?.RewardEmitted ?? false);
    }
    public void StageCrate(ActorInstanceKey key, NVector3 position) => bindings.StageCrate(Resolve(key), GodotMovementPlayer.Engine(position));
    public float ReadRobotHealth(ActorInstanceKey key)
    {
        Resolve(key);
        if (key.Target.NativeClassId != 1440) throw new DiagnosticUnavailableException("The robot health fixture requires class1440.");
        return bindings.RobotHealth(key.Target.InstanceIndex);
    }
    public void StageEnemy(ActorInstanceKey key, NVector3 position)
    {
        Resolve(key);
        var enemy = bindings.Enemies().SingleOrDefault(enemy => enemy.Node.Source.NativeClassId == key.Target.NativeClassId && enemy.Node.Source.InstanceIndex == key.Target.InstanceIndex)
            ?? throw new DiagnosticUnavailableException("The weapon fixture requires a hosted enemy.");
        StageEnemy(enemy, GodotMovementPlayer.Engine(position));
    }
    internal static void StageEnemy(Rac1HostedEnemy enemy, Vector3 position)
    {
        enemy.NeedsGroundPlacement = false; enemy.Body.GlobalPosition = position;
        enemy.Body.Velocity = Vector3.Zero; enemy.Node.Root.GlobalPosition = position;
    }
}
