using Godot;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using static OBP.Game.Rac1EnemyCoordinates;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

internal sealed class Rac1Class1440EnemyAdapter(Rac1EnemyHost host) : IRac1EnemyAdapter
{
    private readonly Rac1EnemyHost _host = host;
    internal Rac1Class1440Session? Session { get; set; }
    public void Tick(Rac1HostedEnemy enemy)
    {
        if (!_host.Owns(enemy) || Session is null || _host.Player is null) return;
        int instance = enemy.Node.Source.InstanceIndex;
        var before = Session!.Get(instance);
        NVector3 position = ToEffectNative(enemy.Node.Root.GlobalPosition), target = ToEffectNative(_host.Combat.Goal(enemy, _host.Player!.GlobalPosition));
        Rac1GameplayEntityRef? contact = before.NativeState == 5 ? _host.EnemySweepContact(enemy) : null;
        if (before.NativeState == 5 && _host.Combat.ChargeDecoy(enemy)) contact = Rac1GameplayEntityRef.Player;
        Rac1Class1440Session.Target? linked = before.LinkedTargetInstanceIndex is int link &&
            _host.Roster.TryGetValue(link, out var linkedEnemy) && _host.IsRac1GameplayActive(linkedEnemy.Node)
            ? new(new(linkedEnemy.Node.Source.NativeClassId, link), ToEffectNative(linkedEnemy.Node.Root.GlobalPosition)) : null;
        var next = Session.Step(instance, new(position, target, enemy.Animation.Wrapped, enemy.Animation.Blending,
            enemy.Animation.Position, contact == Rac1GameplayEntityRef.Player, enemy.Grounded, !_host.Health.Probe().IsDead,
            enemy.Motion!.StepLength, linked, contact));
        Rac1EnemyHost.SelectRac1EnemySequence(enemy, next.Sequence, next.NativeState is 7 or 8 ? 1 : 10);
        if (next.ReactionActive) _host.StepRac1EnemyReaction(enemy);
        else
        {
            enemy.Reaction = null;
            if (next.TurnsToTarget) _host.MoveRac1Enemy(enemy, next.Destination ?? next.LookAt, next.Destination, next.TurnOffset, next.NativeState == 5,
                next.Destination.HasValue ? next.DistanceToPathEnd : null, directPosition: next.Destination.HasValue);
        }
        _host.SetRac1EnemyFlame(enemy, next.NativeState == 5);
        if (next.DamageTarget is { } victim)
        {
            var damage = Rac1DamageRuntime.FromClass1440Contact(new(Rac1ClassIds.VeldinFlameRobot, instance), victim);
            if (victim == Rac1GameplayEntityRef.Player && !_host.Combat.HitsDecoy(enemy)) _host.ApplyRac1EnemyPlayerDamage(damage);
            else if (_host.Roster.TryGetValue((int)victim.RuntimeId, out var targetEnemy) && _host.ApplyRac1EnemyWeaponDamage(targetEnemy.Node, damage))
                _host.Context.DamageTransport.Publish(damage);
        }
    }
    public int ClassId => Rac1ClassIds.VeldinFlameRobot;
    public Rac1EnemyDamageAdmission Damage(Rac1GameplayDamageEvent damage)
    {
        var result = _host.Context.MobyRuntime.DispatchDamage<Rac1Class1440Session.DamageResult>(damage);
        return new(result.Lethal, result.Probe.Sequence);
    }

    internal void Register(RuntimeWorld world, RuntimeWorldScene.Result result, byte[] gameplay, List<RuntimeWorldScene.DynamicObjectNode> nodes)
    {
        Session = new(_host.Context.MobyRuntime, Rac1Paths.Read(gameplay), _host.Random);
        foreach (var authored in (world.DynamicObjects ?? []).Where(item => item.NativeClassId == Rac1Class1440Session.ClassId))
        {
            var node = Rac1WorldPresentation.FindPresentedDynamic(result, authored) ?? throw new InvalidDataException("Veldin enemy has no rendered model.");
            var registered = Session.Register(authored, node.State);
            node.ApplyState(registered.Instance.EntityState);
            nodes.Add(node);
        }
    }

}
