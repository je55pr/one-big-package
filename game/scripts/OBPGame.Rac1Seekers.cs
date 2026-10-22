using Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    // Native 0x25e450 dispatches the class-193 explosion to sphere targets.
    // Godot supplies obstacle LOS and world collision, not fabricated
    // combat damage values.

    private void TickRac1Seeker(Rac1HostedEnemy enemy)
    {
        if (_rac1Seekers is null || _player is null) return;
        var node = enemy.Node;
        int index = node.Source.InstanceIndex;
        var previous = _rac1Seekers.Get(index);

        bool recoiling = previous.NativeState is Rac1SeekerSession.HurtState or Rac1SeekerSession.DeathState;
        if (recoiling && enemy.Reaction is not null)
            StepRac1EnemyReaction(enemy);

        bool settled = previous.NativeState == Rac1SeekerSession.HurtState &&
            enemy.Reaction is not null &&
            enemy.Reaction.Velocity.Length() < Rac1SeekerSession.NativeKnockbackExitSpeed;
        bool fatalImpact = previous.NativeState == Rac1SeekerSession.DeathState && enemy.Grounded;
        var frame = _rac1Seekers.Step(index,
            ToEffectNative(node.Root.GlobalPosition),
            ToEffectNative(ArsenalEnemyGoal(enemy, _player.GlobalPosition)),
            enemy.Animation.Wrapped,
            knockbackSettled: settled, hitTerrain: fatalImpact,
            heroHeight: ToEffectNative(_player.GlobalPosition).Z);

        if (recoiling && frame.NativeState == 4)
            enemy.Reaction = null;
        SelectRac1EnemySequence(enemy, frame.Sequence, 10);

        if (!recoiling && frame.Displacement.LengthSquared() > 0)
        {
            // The original class 193 feeds this native displacement to the
            // 0x258830 world contact solver. Godot hosts sweep/slide.
            var remaining = FromEffectNative(frame.Displacement);
            for (int attempt = 0; attempt < 2 && remaining.LengthSquared() > 1e-8f; attempt++)
            {
                var contact = enemy.Body.MoveAndCollide(remaining);
                if (contact is null) break;
                remaining = contact.GetRemainder().Slide(contact.GetNormal());
            }
            node.Root.GlobalPosition = enemy.Body.GlobalPosition;
        }
        node.Root.Rotation = new(0,
            recoiling && enemy.Reaction is { } reaction ? reaction.FacingYaw : frame.Yaw, 0);

        if (frame.ExplosionTriggered)
        {
            Vector3 origin = node.Root.GlobalPosition;
            // Native Seeker state 7 calls FUN_L00_0025e450. The common Bomb
            // particle host is an explicit approximation of this source FX.
            SpawnRac1BombExplosion(origin);
            var blastCentre = origin + Vector3.Up * .3f;
            if (!_rac1Nanotech.Probe().IsDead)
            {
                Vector3 playerCentre = _player.GlobalPosition + Vector3.Up * .6f;
                if (blastCentre.DistanceTo(playerCentre) <= Rac1SeekerExplosion.Radius &&
                    BombHasLineOfSight(blastCentre, playerCentre))
                    ApplyRac1EnemyPlayerDamage(Rac1SeekerExplosion.DamageTo(index,
                        Rac1GameplayEntityRef.Player));
            }

            foreach (var target in _rac1CrateNodes.Concat(Rac1EnemyNodes).Where(candidate =>
                         IsInstanceValid(candidate.Root) && IsRac1GameplayActive(candidate) &&
                         candidate.Source.InstanceIndex != index))
            {
                var targetPoint = target.Root.GlobalPosition + Vector3.Up * .5f;
                if (blastCentre.DistanceTo(targetPoint) > Rac1SeekerExplosion.Radius ||
                    !BombHasLineOfSight(blastCentre, targetPoint)) continue;
                var damage = Rac1SeekerExplosion.DamageTo(index,
                    Rac1GameplayEntityRef.Moby(new(target.Source.NativeClassId, target.Source.InstanceIndex)));
                if (!_rac1MobyRuntime.CanDispatchDamage(damage)) continue;
                if (target.Source.NativeClassId == Rac1ExplosiveCrateSession.ClassId)
                {
                    TryTriggerRac1ExplosiveDamage(target, damage);
                }
                else if (Rac1BoltCrate.IsCrateClass(target.Source.NativeClassId))
                {
                    CompleteRac1CrateDamage(target,
                        _rac1MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(damage));
                }
                else if (target.Source.NativeClassId == Rac1SeekerSession.ClassId)
                {
                    ApplyRac1EnemyWeaponDamage(target, damage);
                }
                else continue; // Other class-specific response masks remain unverified.
                _rac1DamageTransport.Publish(damage);
            }
            GD.Print($"[rac1-seekers] class193 i{index}: native state7 timed detonation");
        }
        else if (frame.FatalBurst)
        {
            // Native mine_fall impact bit 0x121 calls cart_burst with scale
            // 0.5 and lifetime 13; preserve the separate fatal path.
            SpawnBombParticleAdapter(node.Root.GlobalPosition);
            GD.Print($"[rac1-seekers] class193 i{index}: fatal impact burst");
        }
        if (frame.Terminal)
        {
            enemy.Reaction = null;
            node.ApplyState(_rac1MobyRuntime.Require(node.Source).EntityState);
        }
    }
}
