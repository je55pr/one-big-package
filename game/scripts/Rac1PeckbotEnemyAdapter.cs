using Godot;
using OBP.Godot;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;
using static Godot.GodotObject;
using static OBP.Game.Rac1EnemyCoordinates;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

internal sealed class Rac1PeckbotEnemyAdapter(Rac1EnemyHost host) : IRac1EnemyAdapter
{
    private readonly Rac1EnemyHost _host = host;
    internal Rac1NovalisPeckbotSession? Session { get; set; }
    public void Tick(Rac1HostedEnemy enemy)
    {
        if (!_host.Owns(enemy) || Session is null || _host.Player is null) return;
        var node = enemy.Node;
        Vector3 origin = node.Root.GlobalPosition;
        Vector3 toPlayer = _host.Player.GlobalPosition - origin;
        Vector3 planar = new(toPlayer.X, 0, toPlayer.Z);
        Vector3 forward = Rac1EnemyHost.EnemyForward(node);
        float facing = planar.LengthSquared() < 1e-6f
            ? 0f : forward.AngleTo(planar.Normalized());
        var before = Session.Get(node.Source.InstanceIndex);
        var next = Session.Step(node.Source.InstanceIndex, new(
            ToEffectNative(origin), ToEffectNative(_host.Player.GlobalPosition),
            facing, enemy.Animation.Position,
            enemy.Animation.Wrapped, enemy.Animation.Blending, !_host.Health.Probe().IsDead));
        Rac1EnemyHost.SelectRac1EnemySequence(enemy, next.Sequence, next.NativeState == 6 ? 3 : 10);
        if (next.Destination is { } goal)
            _host.MoveRac1Enemy(enemy, goal, goal);
        else if (next.NativeState is 2 or 4)
        {
            // Native yaw_turn_damped; use the existing host steering adapter.
            enemy.Motion!.Step(ToEffectNative(origin), ToEffectNative(_host.Player.GlobalPosition),
                destination: null, enemy.Animation.Blending, enemy.Grounded);
            node.Root.Rotation = new(0, enemy.Motion!.Yaw, 0);
        }

        if (next.PeckContact && !_host.Health.Probe().IsDead)
        {
            // The retail attack emits sub_00259bc8_v damage 1 at animation
            // time 13, under a facing and distance gate (L1:0x002efc60).
            // Common hit shape/occlusion remains Godot-host approximation.
            var damage = new Rac1GameplayDamageEvent(
                Rac1GameplayEntityRef.Moby(new(Rac1NovalisPeckbotSession.ClassId, node.Source.InstanceIndex)),
                Rac1GameplayEntityRef.Player, 1);
            _host.ApplyRac1EnemyPlayerDamage(damage);
        }

        if (next.Terminal && !before.Terminal)
            GD.Print($"[rac1-peckbot] native death terminal class577 i{node.Source.InstanceIndex}");
    }
    public int ClassId => Rac1NovalisPeckbotSession.ClassId;
    public Rac1EnemyDamageAdmission Damage(Rac1GameplayDamageEvent damage)
    {
        var result = _host.Context.MobyRuntime.DispatchDamage<Rac1NovalisPeckbotSession.DamageResult>(damage);
        return new(result.Lethal, result.Probe.Sequence);
    }

    internal void Register(RuntimeWorld world, RuntimeWorldScene.Result result, List<RuntimeWorldScene.DynamicObjectNode> nodes)
    {
        if (Session is { } peckbots)
            foreach (var authored in (world.DynamicObjects ?? []).Where(item => item.NativeClassId == Rac1NovalisPeckbotSession.ClassId))
            {
                var node = Rac1WorldPresentation.FindPresentedDynamic(result, authored)
                    ?? throw new InvalidDataException("Novalis Peckbot has no original visual.");
                var registered = peckbots.Register(authored, node.State);
                node.ApplyState(registered.Instance.EntityState);
                nodes.Add(node);
            }
    }

}
