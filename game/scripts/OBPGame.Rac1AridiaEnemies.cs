using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private Rac1AridiaEnemySession _rac1AridiaEnemies = new();

    private void TickRac1AridiaEnemy(Rac1HostedEnemy enemy)
    {
        if (_player is null) return;
        var node = enemy.Node;
        int instance = node.Source.InstanceIndex;
        if (_rac1AridiaEnemies.Health(instance) <= 0)
        {
            SetRac1EnemyFlame(enemy, false);
            if (++enemy.DeathTicks >= 36)
                SetRac1HostedPresence(node, RuntimeEntityPresence.Inactive);
            return;
        }

        Vector3 position = node.Root.GlobalPosition;
        Vector3 player = _player.GlobalPosition;
        var facts = new Rac1AridiaEnemyFacts(
            ToEffectNative(position), ToEffectNative(player),
            !_rac1Nanotech.Probe().IsDead,
            position.DistanceSquaredTo(player) < 225f &&
                BombHasLineOfSight(position + Vector3.Up * .35f, player + Vector3.Up * .5f),
            node.Source.NativeClassId == Rac1AridiaEnemies.Nest
                ? _rac1Enemies.Values.Count(other =>
                    other.Node.Source.NativeClassId == Rac1AridiaEnemies.Sandshark &&
                    _rac1AridiaEnemies.CanReleaseFromNest(other.Node.Source.InstanceIndex) &&
                    other.Node.Root.GlobalPosition.DistanceSquaredTo(position) < 900f)
                : 0);
        var frame = _rac1AridiaEnemies.Step(instance, facts);
        enemy.Buried = frame.Buried;
        enemy.Body.CollisionLayer = enemy.Buried ? 0u : 1u;
        node.Root.Visible = !enemy.Buried;
        if (enemy.Animation.SupportsSequence(frame.Sequence))
            SelectRac1EnemySequence(enemy, frame.Sequence, 6);
        node.Root.Rotation = new(0, frame.Yaw, 0);
        if (frame.Motion.LengthSquared() > 0)
            MoveRac1EnemyContact(enemy, FromEffectNative(frame.Motion));
        else
            enemy.Node.Root.GlobalPosition = enemy.Body.GlobalPosition;
        SetRac1EnemyFlame(enemy, frame.FlameActive);
        if (frame.DamagePlayer &&
            node.Root.GlobalPosition.DistanceTo(player) <
                (node.Source.NativeClassId == Rac1AridiaEnemies.Sandshark ? 1.25f : 5f))
            ApplyRac1AridiaPlayerDamage(node);
        if (frame.SpawnSandshark) ActivateNearbyRac1AridiaShark(node.Root.GlobalPosition);
    }

    private void RestoreRac1AridiaEnemyVisibility(Rac1HostedEnemy enemy) =>
        enemy.Node.Root.Visible = IsRac1GameplayActive(enemy.Node) && !enemy.Buried;

    private void ApplyRac1AridiaPlayerDamage(RuntimeWorldScene.DynamicObjectNode source)
    {
        if (_rac1Nanotech.Probe().IsDead) return;
        // The original LEVEL2 outgoing damage descriptor is not recovered.
        // Keep this host adapter separate from any claimed native attack flags.
        var damage = new Rac1GameplayDamageEvent(
            new(Rac1GameplayEntityKind.Moby, source.Source.NativeClassId, source.Source.InstanceIndex),
            Rac1GameplayEntityRef.Player, 1);
        var before = _rac1Nanotech.Probe();
        _rac1DamageTransport.Publish(damage);
        var after = _rac1Nanotech.ApplyHostAridiaContactDamage();
        _player!.Rac1GameplayState = after;
        if (after.Nanotech != before.Nanotech)
            RefreshRac1HudState(OBP.RAC1.Presentation.Rac1HudProjection.DamageFeedback(before, after));
    }

    private void ActivateNearbyRac1AridiaShark(Vector3 nestPosition)
    {
        // L2:0x2dd2a8 locates an existing waiting class-580 Moby; 0x2d6118
        // relocates it to nest Z+1.5, sets heading toward its home position,
        // and selects native state 27/animation 11. Matching the original
        // group-mask selection still requires the native +0x21 roster.
        foreach (var shark in _rac1Enemies.Values
            .Where(other => other.Node.Source.NativeClassId == Rac1AridiaEnemies.Sandshark &&
                _rac1AridiaEnemies.CanReleaseFromNest(other.Node.Source.InstanceIndex))
            .OrderBy(other => other.Node.Root.GlobalPosition.DistanceSquaredTo(nestPosition)))
        {
            if (shark.Node.Root.GlobalPosition.DistanceSquaredTo(nestPosition) > 900f) break;
            int id = shark.Node.Source.InstanceIndex;
            if (!_rac1AridiaEnemies.ReleaseFromNest(id,
                    ToEffectNative(nestPosition), ToEffectNative(shark.Node.Root.GlobalPosition))) continue;
            var origin = nestPosition + Vector3.Up * Rac1AridiaEnemies.NestReleaseHeight;
            shark.Body.GlobalPosition = origin;
            shark.Node.Root.GlobalPosition = origin;
            shark.NeedsGroundPlacement = false;
            shark.Buried = false;
            shark.Body.CollisionLayer = 1;
            if (shark.Animation.SupportsSequence(Rac1AridiaEnemies.NestReleasedSharkSequence))
                SelectRac1EnemySequence(shark, Rac1AridiaEnemies.NestReleasedSharkSequence, 1);
            GD.Print($"[rac1-aridia] nest released authored class580 instance {id} with native state27/sequence11");
            return;
        }
    }

    private bool ApplyRac1AridiaWeaponDamage(RuntimeWorldScene.DynamicObjectNode node,
        Rac1GameplayDamageEvent damage)
    {
        int id = node.Source.InstanceIndex;
        if (!_rac1AridiaEnemies.Damage(id, damage.NativeDamage)) return false;
        var enemy = _rac1Enemies[id];
        bool lethal = _rac1AridiaEnemies.Health(id) == 0;
        if (lethal)
        {
            enemy.Body.CollisionLayer = enemy.Body.CollisionMask = 0;
            enemy.DeathTicks = 0;
            int preferredSequence = node.Source.NativeClassId == Rac1AridiaEnemies.Nest ? 3 : 6;
            if (enemy.Animation.SupportsSequence(preferredSequence))
                SelectRac1EnemySequence(enemy, preferredSequence, 1);
            if (node.Source.NativeClassId is Rac1AridiaEnemies.Sandshark or Rac1AridiaEnemies.Constructorbot)
            {
                // L2:0x2d54e0 (shark) / 0x2d8430 (bot) call moby_drop_bolts
                // with (moby, 0, -1). Both use the shared authored instance
                // reward word at +0x10, not a fabricated fixed enemy amount.
                int selected = SelectRac1CrateReward(Rac1EnemyMotion.RewardCentre(node.Source));
                if (selected > 0)
                    SpawnRac1BoltPickups(node.Root.GlobalPosition, _rac1BoltPickups.Emit(selected));
            }
            // The native nest death path makes an explosion and debris but
            // does not invoke moby_drop_bolts in the translated routine.
        }
        else if (enemy.Animation.SupportsSequence(3))
            SelectRac1EnemySequence(enemy, 3, 1);
        GD.Print($"[rac1-aridia] class{node.Source.NativeClassId} i{id}: " +
            (lethal ? "defeated" : $"hit ({_rac1AridiaEnemies.Health(id)} remaining)"));
        return true;
    }
}
