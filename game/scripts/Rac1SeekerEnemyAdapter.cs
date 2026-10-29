using Godot;
using OBP.Godot;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;
using static Godot.GodotObject;
using static OBP.Game.Rac1EnemyCoordinates;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

internal sealed class Rac1SeekerEnemyAdapter(Rac1EnemyHost host) : IRac1EnemyAdapter
{
    private readonly Rac1EnemyHost _host = host;
    internal Rac1SeekerSession? Session { get; set; }
    // Native 0x25e450 dispatches the class-193 explosion to sphere targets.
    // Godot supplies obstacle LOS and world collision, not fabricated
    // combat damage values.

    public void Tick(Rac1HostedEnemy enemy)
    {
        if (!_host.Owns(enemy) || Session is null || _host.Player is null) return;
        var node = enemy.Node;
        int index = node.Source.InstanceIndex;
        var previous = Session.Get(index);

        bool recoiling = previous.NativeState is Rac1SeekerSession.HurtState or Rac1SeekerSession.DeathState;
        if (recoiling && enemy.Reaction is not null)
            _host.StepRac1EnemyReaction(enemy);

        bool settled = previous.NativeState == Rac1SeekerSession.HurtState &&
            enemy.Reaction is not null &&
            enemy.Reaction.Velocity.Length() < Rac1SeekerSession.NativeKnockbackExitSpeed;
        bool fatalImpact = previous.NativeState == Rac1SeekerSession.DeathState && enemy.Grounded;
        var frame = Session.Step(index,
            ToEffectNative(node.Root.GlobalPosition),
            ToEffectNative(_host.Combat.Goal(enemy, _host.Player.GlobalPosition)),
            enemy.Animation.Wrapped,
            knockbackSettled: settled, hitTerrain: fatalImpact,
            heroHeight: ToEffectNative(_host.Player.GlobalPosition).Z);

        if (recoiling && frame.NativeState == 4)
            enemy.Reaction = null;
        Rac1EnemyHost.SelectRac1EnemySequence(enemy, frame.Sequence, 10);

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
            _host.Combat.Explosion(origin);
            var blastCentre = origin + Vector3.Up * .3f;
            if (!_host.Health.Probe().IsDead)
            {
                Vector3 playerCentre = _host.Player.GlobalPosition + Vector3.Up * .6f;
                if (blastCentre.DistanceTo(playerCentre) <= Rac1SeekerExplosion.Radius &&
                    _host.Combat.LineOfSight(blastCentre, playerCentre))
                    _host.ApplyRac1EnemyPlayerDamage(Rac1SeekerExplosion.DamageTo(index,
                        Rac1GameplayEntityRef.Player));
            }

            foreach (var target in _host.Presentation.CrateNodes.Concat(_host.Targets).Where(candidate =>
                         IsInstanceValid(candidate.Root) && _host.IsRac1GameplayActive(candidate) &&
                         candidate.Source.InstanceIndex != index))
            {
                var targetPoint = target.Root.GlobalPosition + Vector3.Up * .5f;
                if (blastCentre.DistanceTo(targetPoint) > Rac1SeekerExplosion.Radius ||
                    !_host.Combat.LineOfSight(blastCentre, targetPoint)) continue;
                var damage = Rac1SeekerExplosion.DamageTo(index,
                    Rac1GameplayEntityRef.Moby(new(target.Source.NativeClassId, target.Source.InstanceIndex)));
                if (!_host.Context.MobyRuntime.CanDispatchDamage(damage)) continue;
                if (target.Source.NativeClassId == Rac1ExplosiveCrateSession.ClassId)
                {
                    _host.Combat.ExplosiveDamage(target, damage);
                }
                else if (Rac1BoltCrate.IsCrateClass(target.Source.NativeClassId))
                {
                    _host.Combat.CrateDamage(target,
                        _host.Context.MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(damage));
                }
                else if (target.Source.NativeClassId == Rac1SeekerSession.ClassId)
                {
                    _host.ApplyRac1EnemyWeaponDamage(target, damage);
                }
                else continue; // Other class-specific response masks remain unverified.
                _host.Context.DamageTransport.Publish(damage);
            }
            GD.Print($"[rac1-seekers] class193 i{index}: native state7 timed detonation");
        }
        else if (frame.FatalBurst)
        {
            // Native mine_fall impact bit 0x121 calls cart_burst with scale
            // 0.5 and lifetime 13; preserve the separate fatal path.
            // Retain a single short-lived impact flash through the new
            // retail particle presentation host (the original cart_burst
            // tuning is still distinct from a full bomb detonation).
            _host.Combat.FatalBurst(node.Root.GlobalPosition);
            GD.Print($"[rac1-seekers] class193 i{index}: fatal impact burst");
        }
        if (frame.Terminal)
        {
            enemy.Reaction = null;
            node.ApplyState(_host.Context.MobyRuntime.Require(node.Source).EntityState);
        }
    }
    public int ClassId => Rac1SeekerSession.ClassId;
    public Rac1EnemyDamageAdmission Damage(Rac1GameplayDamageEvent damage)
    {
        var result = _host.Context.MobyRuntime.DispatchDamage<Rac1SeekerDamageResult>(damage);
        return new(result.Lethal, result.Frame.Sequence);
    }

    internal void Register(RuntimeWorld world, RuntimeWorldScene.Result result, List<RuntimeWorldScene.DynamicObjectNode> nodes)
    {
        if (Session is { } seekers)
            foreach (var authored in (world.DynamicObjects ?? [])
                .Where(item => item.NativeClassId == Rac1SeekerSession.ClassId))
            {
                var node = Rac1WorldPresentation.FindPresentedDynamic(result, authored)
                    ?? throw new InvalidDataException("Original Seeker model is unavailable.");
                NVector3 nativeForward = ToEffectNative(Rac1EnemyHost.EnemyForward(node));
                seekers.Register(authored, node.State, MathF.Atan2(nativeForward.Y, nativeForward.X));
                nodes.Add(node);
            }
    }

}
