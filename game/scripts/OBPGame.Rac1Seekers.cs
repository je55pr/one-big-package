using Godot;
using OBP.RAC1.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private void TickRac1Seeker(Rac1HostedEnemy enemy)
    {
        if (_rac1Seekers is null || _player is null) return;
        var node = enemy.Node;
        var frame = _rac1Seekers.Step(node.Source.InstanceIndex,
            ToEffectNative(node.Root.GlobalPosition),
            ToEffectNative(ArsenalEnemyGoal(enemy, _player.GlobalPosition)),
            enemy.Animation.Wrapped);
        SelectRac1EnemySequence(enemy, frame.Sequence, 10);

        // Original 0x258830 sweeps native displacement against the world.
        // MoveAndCollide is the explicit Godot contact adapter.
        var motion = FromEffectNative(frame.Displacement);
        if (motion.LengthSquared() > 0)
        {
            for (int attempt = 0; attempt < 2 && motion.LengthSquared() > 1e-8f; attempt++)
            {
                var collision = enemy.Body.MoveAndCollide(motion);
                if (collision is null) break;
                motion = collision.GetRemainder().Slide(collision.GetNormal());
            }
            node.Root.GlobalPosition = enemy.Body.GlobalPosition;
        }
        node.Root.Rotation = new(0, frame.Yaw, 0);
        if (frame.Terminal)
            node.ApplyState(_rac1MobyRuntime.Require(node.Source).EntityState);
    }
}
