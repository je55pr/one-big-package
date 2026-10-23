using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;
using NVector2 = System.Numerics.Vector2;

namespace OneBigPackage;

public partial class OBPGame
{
    private Rac1AmoeboidFamilySession? _rac1Amoeboids;
    private IReadOnlyList<Rac1ClipVolume> _rac1AmoeboidVolumes = [];
    private const float Rac1AmoeboidHostActiveRadius = 45f;

    private void ResetRac1Amoeboids()
    {
        _rac1Amoeboids = null;
        _rac1AmoeboidVolumes = [];
    }

    private void ConfigureRac1Amoeboids(RuntimeWorld world, RuntimeWorldScene.Result result,
        List<RuntimeWorldScene.DynamicObjectNode> presented, byte[] gameplay)
    {
        if (world.LevelId is not (1 or 5 or 11)) return;
        _rac1AmoeboidVolumes = Rac1ClipVolumes.Read(gameplay);
        var instances = Rac1Instances.Parse(gameplay).MobyInstances;
        _rac1Amoeboids = new(_rac1MobyRuntime,
            Rac1AmoeboidPathRouter.Read(gameplay),
            // Exact source 0x276fe8 waypoint/mask search, but Godot's
            // world ray query substitutes for native 0x261968 segment collision.
            (_, from, to) => !BombHasLineOfSight(
                FromEffectNative(from), FromEffectNative(to)));
        foreach (var source in (world.DynamicObjects ?? [])
            .Where(moby => Rac1AmoeboidAuthored.IsFamily(moby.NativeClassId)))
        {
            var placement = instances[source.InstanceIndex];
            var authored = Rac1AmoeboidAuthored.Read(placement.OClass,
                placement.PVar ?? throw new InvalidDataException("Amoeboid has no authored PVar."));
            var node = FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException($"Native Amoeboid i{source.InstanceIndex} has no original mesh.");
            var born = _rac1Amoeboids.Register(source, node.State, authored,
                Rac1AmoeboidAuthored.ReadGroup(placement.RawRecord),
                new(placement.Position.X, placement.Position.Y, placement.Position.Z),
                placement.Rotation.Z);
            node.ApplyState(_rac1MobyRuntime.Require(source).EntityState);
            // The original state12 and state13 actors aren't drawn or solid.
            if (!born.Active) node.Root.Visible = false;
            presented.Add(node);
        }
        GD.Print($"[rac1-amoeboids] L{world.LevelId}: {_rac1Amoeboids.RegisteredCount} authored, " +
            $"{_rac1Amoeboids.ActiveCount} active, {_rac1Amoeboids.GatedCount} gated, " +
            $"{_rac1Amoeboids.DormantCount} reserve");
    }

    private void InitializeRac1AmoeboidPresentation(Rac1HostedEnemy enemy)
    {
        if (_rac1Amoeboids is null) return;
        var frame = _rac1Amoeboids.Get(enemy.Node.Source.InstanceIndex);
        if (!frame.Active)
        {
            enemy.Body.CollisionLayer = 0;
            enemy.Body.CollisionMask = 0;
            enemy.Node.Root.Visible = false;
        }
    }

    private void TickRac1AmoeboidGates()
    {
        if (_rac1Amoeboids is null || _player is null) return;
        var point = ToEffectNative(_player.GlobalPosition);
        foreach (var frame in _rac1Amoeboids.ActivateGates(point,
                     (volume, probe) => volume >= 0 && volume < _rac1AmoeboidVolumes.Count &&
                         _rac1AmoeboidVolumes[volume].Contains(probe)))
            ActivateRac1Amoeboid(frame);
    }

    private void ActivateRac1Amoeboid(Rac1AmoeboidFrame frame)
    {
        if (!_rac1Enemies.TryGetValue(frame.InstanceIndex, out var enemy))
            throw new InvalidOperationException("Split child lacks its original level Moby.");
        var node = enemy.Node;
        enemy.Body.GlobalPosition = FromEffectNative(frame.Position);
        node.Root.GlobalPosition = enemy.Body.GlobalPosition;
        var instance = _rac1MobyRuntime.Require(node.Source);
        _rac1MobyRuntime.SetTransform(instance, RuntimeWorldScene.ToRuntimeTransform(node.Root.Transform));
        node.Root.Visible = true;
        enemy.Body.CollisionLayer = 1;
        enemy.Body.CollisionMask = 1;
        node.ApplyState(instance.EntityState);
        SelectRac1EnemySequence(enemy, frame.Sequence, 5);
        enemy.Grounded = false;
        enemy.Reaction = frame.NativeState == Rac1AmoeboidFamilySession.KnockbackState
            ? new Rac1MobyReaction(frame.ClassId, lethal: false, frame.Yaw)
            : null;
    }

    private void TickRac1Amoeboid(Rac1HostedEnemy enemy)
    {
        if (_rac1Amoeboids is null || _player is null) return;
        var node = enemy.Node;
        var before = _rac1Amoeboids.Get(node.Source.InstanceIndex);
        if (node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) >
            Rac1AmoeboidHostActiveRadius * Rac1AmoeboidHostActiveRadius &&
            before.NativeState is not Rac1AmoeboidFamilySession.KnockbackState)
            return;
        if (enemy.Reaction is not null)
            StepRac1EnemyReaction(enemy);
        var pos = ToEffectNative(node.Root.GlobalPosition);
        var target = ToEffectNative(ArsenalEnemyGoal(enemy, _player.GlobalPosition));
        bool contact = NVector2.Distance(new(pos.X, pos.Y), new(target.X, target.Y)) < Rac1AmoeboidFamilySession.NativeAttackRange &&
            BombHasLineOfSight(node.Root.GlobalPosition + Vector3.Up * .6f,
                _player.GlobalPosition + Vector3.Up * .6f);
        var next = _rac1Amoeboids.Step(node.Source.InstanceIndex, pos, target,
            enemy.Animation.Wrapped, enemy.Grounded, contact, enemy.Animation.Position);
        if (before.NativeState == Rac1AmoeboidFamilySession.KnockbackState &&
            next.NativeState == Rac1AmoeboidFamilySession.KnockbackRecoveryState)
            enemy.Reaction = null;
        SelectRac1EnemySequence(enemy, next.Sequence, 10);
        if (enemy.Reaction is null && next.Displacement.LengthSquared() > 0)
            MoveRac1EnemyContact(enemy, FromEffectNative(next.Displacement));
        node.Root.Rotation = new(0, next.Yaw, 0);
        if (next.Attack && !ArsenalEnemyHitsDecoy(enemy))
            ApplyRac1EnemyPlayerDamage(Rac1AmoeboidAttack.ToPlayer(next.ClassId, next.InstanceIndex));
    }

    private bool ApplyRac1AmoeboidDamage(RuntimeWorldScene.DynamicObjectNode node,
        Rac1GameplayDamageEvent damage)
    {
        if (_rac1Amoeboids is null || !_rac1MobyRuntime.CanDispatchDamage(damage)) return false;
        var result = _rac1MobyRuntime.DispatchDamage<Rac1AmoeboidDamageResult>(damage);
        var parent = _rac1Enemies[node.Source.InstanceIndex];
        parent.Body.CollisionLayer = 0;
        parent.Body.CollisionMask = 0;
        node.ApplyState(_rac1MobyRuntime.Require(node.Source).EntityState);
        node.Root.Visible = false;
        foreach (var child in result.Children)
            ActivateRac1Amoeboid(child);
        GD.Print($"[rac1-amoeboids] class{node.Source.NativeClassId} i{node.Source.InstanceIndex} " +
            $"split into [{string.Join(",", result.Children.Select(x => x.InstanceIndex))}] " +
            $"health={result.Parent.Health}");
        return true;
    }
}
