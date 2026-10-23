using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using System.Collections.Generic;
using NVector3 = System.Numerics.Vector3;

namespace OneBigPackage;

/// <summary>Level 6/10 class-827 host adapters only. Native selectors, combat
/// transitions and damage ownership belong to Rac1FrogSession.</summary>
public partial class OBPGame
{
    private Rac1FrogSession? _rac1Frogs;
    private const float FrogHostSleepRadius = 30f;
    private const float FrogHostAggroRadius = 12f; // provisional common-target query

    private void ConfigureRac1Frogs(RuntimeWorld world, RuntimeWorldScene.Result result,
        IReadOnlyList<NVector3[]> paths, List<RuntimeWorldScene.DynamicObjectNode> nodes)
    {
        _rac1Frogs = new Rac1FrogSession(_rac1MobyRuntime, paths);
        foreach (var authored in (world.DynamicObjects ?? []).Where(item => item.NativeClassId == Rac1FrogAuthored.ClassId))
        {
            var node = FindPresentedDynamic(result, authored)
                ?? throw new InvalidDataException("Original Frog mesh unavailable.");
            var forward = ToEffectNative(EnemyForward(node));
            var frame = _rac1Frogs.Register(authored, node.State,
                ToEffectNative(node.Root.GlobalPosition), MathF.Atan2(forward.Y, forward.X));
            node.ApplyState(_rac1MobyRuntime.Require(authored).EntityState);
            nodes.Add(node);
        }
    }

    private void TickRac1Frog(Rac1HostedEnemy enemy)
    {
        if (_rac1Frogs is null || _player is null) return;
        var node = enemy.Node;
        int index = node.Source.InstanceIndex;
        var authored = _rac1Frogs.GetAuthored(index);
        Vector3 goal = ArsenalEnemyGoal(enemy, _player.GlobalPosition);
        bool eligible = goal.DistanceSquaredTo(node.Root.GlobalPosition) <
            FrogHostAggroRadius * FrogHostAggroRadius;
        bool hitsPlayer = true;
        if (authored.TargetInstance >= 0)
        {
            // Fixed target_id+0x204 addresses a native Moby, not Ratchet.
            // Never silently turn this special path into player pursuit.
            if (_rac1LinkedTargetNodes.TryGetValue(authored.TargetInstance, out var linked) &&
                IsInstanceValid(linked.Root) && IsRac1GameplayActive(linked))
            {
                goal = linked.Root.GlobalPosition;
                eligible = true;
            }
            else eligible = false;
            hitsPlayer = false;
        }
        var old = _rac1Frogs.Get(index);
        var native = _rac1Frogs.Step(index,
            ToEffectNative(node.Root.GlobalPosition), ToEffectNative(goal), eligible,
            enemy.Animation.Wrapped, enemy.Animation.Position,
            hitsPlayer && EnemyMeleeTouchesPlayer(enemy), enemy.Grounded);
        if (native.Sequence != enemy.Animation.Sequence)
            SelectRac1EnemySequence(enemy, native.Sequence, 10);
        if (native.Displacement.LengthSquared() > 0)
            MoveRac1EnemyContact(enemy, FromEffectNative(native.Displacement));
        node.Root.Rotation = new(0, native.Yaw, 0);
        if (native.Contact && !ArsenalEnemyHitsDecoy(enemy))
            ApplyRac1EnemyPlayerDamage(Rac1FrogContact.DamageToPlayer(index));
    }
}
