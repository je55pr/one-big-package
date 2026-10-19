using Godot;
using OBP.RAC1.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private void TickRac1NovalisPeckbot(Rac1HostedEnemy enemy)
    {
        if (_rac1Peckbots is null || _player is null) return;
        var node = enemy.Node;
        Vector3 origin = node.Root.GlobalPosition;
        Vector3 toPlayer = _player.GlobalPosition - origin;
        Vector3 planar = new(toPlayer.X, 0, toPlayer.Z);
        Vector3 forward = EnemyForward(node);
        float facing = planar.LengthSquared() < 1e-6f
            ? 0f : forward.AngleTo(planar.Normalized());
        var before = _rac1Peckbots.Get(node.Source.InstanceIndex);
        var next = _rac1Peckbots.Step(node.Source.InstanceIndex, new(
            ToEffectNative(origin), ToEffectNative(_player.GlobalPosition),
            facing, enemy.Animation.Position,
            enemy.Animation.Wrapped, enemy.Animation.Blending, !_rac1Nanotech.Probe().IsDead));
        SelectRac1EnemySequence(enemy, next.Sequence, next.NativeState == 6 ? 3 : 10);
        if (next.Destination is { } goal)
            MoveRac1Enemy(enemy, goal, goal);
        else if (next.NativeState is 2 or 4)
        {
            // Native yaw_turn_damped; use the existing host steering adapter.
            enemy.Motion.Step(ToEffectNative(origin), ToEffectNative(_player.GlobalPosition),
                destination: null, enemy.Animation.Blending, enemy.Grounded);
            node.Root.Rotation = new(0, enemy.Motion.Yaw, 0);
        }

        if (next.PeckContact && !_rac1Nanotech.Probe().IsDead)
        {
            // The retail attack emits sub_00259bc8_v damage 1 at animation
            // time 13, under a facing and distance gate (L1:0x002efc60).
            // Common hit shape/occlusion remains Godot-host approximation.
            var damage = new Rac1GameplayDamageEvent(
                Rac1GameplayEntityRef.Moby(new(Rac1NovalisPeckbotSession.ClassId, node.Source.InstanceIndex)),
                Rac1GameplayEntityRef.Player, 1);
            ApplyRac1EnemyPlayerDamage(damage);
        }

        if (next.Terminal && !before.Terminal)
            GD.Print($"[rac1-peckbot] native death terminal class577 i{node.Source.InstanceIndex}");
    }
}
