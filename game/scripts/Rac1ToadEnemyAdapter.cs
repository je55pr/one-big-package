using Godot;
using static Godot.GodotObject;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using static OBP.Game.Rac1EnemyCoordinates;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

internal sealed class Rac1ToadEnemyAdapter(Rac1EnemyHost host) : IRac1EnemyAdapter
{
    private readonly Rac1EnemyHost _host = host;
    public void Tick(Rac1HostedEnemy enemy)
    {
        if (!_host.Owns(enemy) || _host.Player is null) return;
        var node = enemy.Node;
        var before = _host.Context.Hostiles.Probe(node.Source);
        Vector3 position = node.Root.GlobalPosition, target = _host.Combat.Goal(enemy, _host.Player!.GlobalPosition);
        Rac1GameplayEntityRef identity = Rac1GameplayEntityRef.Player;
        bool linkedTerminal = false;
        if (before.NativeState == Rac1Class749Hostile.LinkedObjectNativeState && before.LinkedTargetInstanceIndex is int linked)
        {
            _host.Presentation.LinkedTargets.TryGetValue(linked, out var linkedNode);
            linkedTerminal = linkedNode is null || !IsInstanceValid(linkedNode.Root) || !_host.IsRac1GameplayActive(linkedNode);
            if (!linkedTerminal) { target = linkedNode!.Root.GlobalPosition; identity = Rac1GameplayEntityRef.Moby(new(linkedNode.Source.NativeClassId, linked)); }
        }
        Vector3 difference = target - position;
        Vector3 planar = new(difference.X, 0, difference.Z);
        float facing = planar.LengthSquared() == 0 ? 0 : Math.Abs(Rac1EnemyHost.EnemyForward(node).SignedAngleTo(planar.Normalized(), Vector3.Up));
        var next = _host.Context.Hostiles.Step(node.Source, new(difference.Length(), facing,
            new(-position.X, position.Y, position.Z), TargetDescriptor: new(identity, new(-target.X, target.Y, target.Z)),
            LinkedObjectTerminal: linkedTerminal, AnimationPosition: enemy.Animation.Position,
            AnimationFinished: enemy.Animation.Wrapped, AnimationBlending: enemy.Animation.Blending, Grounded: enemy.Grounded));
        // Native state5 selects locomotion4 for 0x261630's roaming motion.
        // This host does not implement that routine yet. Its stationary stand-in
        // uses the retail rest clip1 rather than bouncing without displacement.
        int sequence = next.NativeState == Rac1Class749Hostile.TargetSearchNativeState
            ? Rac1Class749Hostile.RestSequenceId : next.NativeSequence ?? enemy.Animation.Sequence;
        Rac1EnemyHost.SelectRac1EnemySequence(enemy, sequence, next.NativeState == 7 ? 7 : 10);
        if (next.ReactionActive) { _host.StepRac1EnemyReaction(enemy); return; }
        enemy.Reaction = null;
        var intent = next.HostIntents.OfType<Rac1Class749NavigationIntent>().FirstOrDefault();
        NVector3? destination = intent?.Destination is { } point ? new((float)point.X, (float)point.Z, (float)point.Y) : null;
        if (destination.HasValue)
            _host.MoveRac1Enemy(enemy, destination.Value, destination, next.NavigationYawOffset);
        else if (next.NativeState == Rac1Class749Hostile.LinkedObjectNativeState && !linkedTerminal)
            _host.MoveRac1Enemy(enemy, ToEffectNative(target), null);
        if (next.Attack is { } attack && !_host.Combat.HitsDecoy(enemy) && _host.EnemyMeleeTouchesPlayer(enemy))
            _host.ApplyRac1EnemyPlayerDamage(Rac1DamageRuntime.FromClass749Attack(new(749, node.Source.InstanceIndex), attack));
    }
    public int ClassId => 749;
    public Rac1EnemyDamageAdmission Damage(Rac1GameplayDamageEvent damage)
    {
        var result = _host.Context.MobyRuntime.DispatchDamage<Rac1Class749HostProbe>(damage);
        return new(result.Health <= 0, result.NativeSequence!.Value);
    }

}
