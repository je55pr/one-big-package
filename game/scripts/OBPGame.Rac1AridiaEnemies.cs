using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private Rac1AridiaEnemySession _rac1AridiaEnemies = new();
    private readonly Dictionary<int, Rac1AridiaConstructorbotNative.Frame> _rac1ConstructorbotStartups = [];

    private void ResetRac1ConstructorbotStartups() => _rac1ConstructorbotStartups.Clear();

    private void TickRac1AridiaEnemy(Rac1HostedEnemy enemy)
    {
        if (_player is null) return;
        var node = enemy.Node;
        int instance = node.Source.InstanceIndex;
        if (_rac1AridiaEnemies.Health(instance) <= 0 && !_rac1AridiaEnemies.CanReleaseFromNest(instance))
        {
            SetRac1EnemyFlame(enemy, false);
            if (++enemy.DeathTicks >= 36)
            {
                // Native shark state 26 is a reusable nest pool, not a terminal
                // despawn. Other defeated classes still leave gameplay.
                if (_rac1AridiaEnemies.RetireToNestPool(instance))
                {
                    var native = _rac1MobyRuntime.Require(node.Source);
                    _rac1MobyRuntime.SetNativeState(native, 26);
                    enemy.Buried = true;
                    enemy.NeedsGroundPlacement = false;
                    enemy.Body.CollisionLayer = enemy.Body.CollisionMask = 0;
                    HideBuriedRac1AridiaEnemy(enemy);
                }
                else SetRac1HostedPresence(node, RuntimeEntityPresence.Inactive);
            }
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
                ? _rac1AridiaEnemies.WaitingSharkCountForNest(instance)
                : 0,
            LinkedMobyNativeState: ResolveAridiaSharkLinkedMobyState(instance),
            NestAnimationTime: node.Source.NativeClassId == Rac1AridiaEnemies.Nest
                ? enemy.Animation.Position : null);
        var frame = _rac1AridiaEnemies.Step(instance, facts);
        if (node.Source.NativeClassId == Rac1AridiaEnemies.Sandshark &&
            _rac1AridiaEnemies.LinkedActivationSatisfied(instance))
        {
            var native = _rac1MobyRuntime.Require(node.Source);
            if (native.State.NativeState == 29)
                _rac1MobyRuntime.SetNativeState(native, 1);
        }
        enemy.Buried = frame.Buried;
        enemy.Body.CollisionLayer = enemy.Body.CollisionMask = enemy.Buried ? 0u : 1u;
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
        if (frame.SpawnSandshark) ActivateNearbyRac1AridiaShark(instance, node.Root.GlobalPosition);
    }

    private int? ResolveAridiaSharkLinkedMobyState(int instanceIndex)
    {
        int? link = _rac1AridiaEnemies.LinkedActivationMobyIndex(instanceIndex);
        if (!link.HasValue) return null;
        // Native class 1818 is the Aridia Sandmouse. Unless its native
        // controller is actually running, the required state >= 5 cannot be
        // inferred from elapsed time or the player's nearby position.
        return _rac1MobyRuntime.TryGet(new Rac1MobyRuntimeKey(Rac1AridiaEnemies.Sandmouse, link.Value),
            out var moby) && moby is { IsActive: true }
            ? moby.State.NativeState : null;
    }

    private static void HideBuriedRac1AridiaEnemy(Rac1HostedEnemy enemy) =>
        enemy.Node.Root.Visible = false;

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
        NotifyRac1PlayerDamageAudio(before, after);
        if (after.Nanotech != before.Nanotech)
            RefreshRac1HudState(OBP.RAC1.Presentation.Rac1HudProjection.DamageFeedback(before, after));
    }

    private void ActivateNearbyRac1AridiaShark(int nestInstance, Vector3 nestPosition)
    {
        // L2:0x2dd2a8 walks the nest's authored group (+0x21) and selects
        // class 580 in state 26 only. No distance-based candidate fallback.
        int? candidate = _rac1AridiaEnemies.FindWaitingSharkForNest(nestInstance);
        if (candidate is not int id || !_rac1Enemies.TryGetValue(id, out var shark)) return;
        if (!_rac1AridiaEnemies.ReleaseFromNest(nestInstance, id,
                ToEffectNative(nestPosition), ToEffectNative(shark.Node.Root.GlobalPosition))) return;
        var native = _rac1MobyRuntime.Require(shark.Node.Source);
        _rac1MobyRuntime.SetNativeState(native, Rac1AridiaEnemies.NestReleasedSharkState);
        var origin = nestPosition + Vector3.Up * Rac1AridiaEnemies.NestReleaseHeight;
        shark.Body.GlobalPosition = origin;
        shark.Node.Root.GlobalPosition = origin;
        shark.NeedsGroundPlacement = false;
        shark.Buried = false;
        shark.DeathTicks = 0;
        shark.Body.CollisionLayer = shark.Body.CollisionMask = 1;
        if (shark.Animation.SupportsSequence(Rac1AridiaEnemies.NestReleasedSharkSequence))
            SelectRac1EnemySequence(shark, Rac1AridiaEnemies.NestReleasedSharkSequence, 1);
        GD.Print($"[rac1-aridia] nest released authored class580 instance {id} with native state27/sequence11");
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
