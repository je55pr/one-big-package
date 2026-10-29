using NVector3 = System.Numerics.Vector3;
using OBP.Core;
using Godot;
using static Godot.GodotObject;
using static OBP.Game.Rac1EnemyCoordinates;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game;

internal sealed partial class Rac1EnemyHost
{
    internal bool TryGetRac1Hostile(
        int instanceIndex,
        out RuntimeWorldScene.DynamicObjectNode? node,
        out Rac1Class749HostProbe? probe)
    {
        if (Current && _world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: Rac1Class749VeldinPopulation.LevelId } &&
            Presentation.HostileNodes.TryGetValue(instanceIndex, out node))
        {
            probe = Context.Hostiles.Probe(node.Source);
            return true;
        }

        node = null;
        probe = null;
        return false;
    }

    internal Rac1Class749HostProbe[] SnapshotRac1HostileProbes() =>
        !Current ? [] : Presentation.HostileNodes.Values
            .OrderBy(node => node.Source.InstanceIndex)
            .Select(node => Context.Hostiles.Probe(node.Source))
            .ToArray();

    // Native IDs may be reused. Check both the scene subtree and the exact
    // roster node before admitting an old callback to the current runtime.
    private bool OwnsNode(RuntimeWorldScene.DynamicObjectNode node)
    {
        if (!Current || node.Source.SourceGame != ObpSourceGame.Rac1 ||
            !IsInstanceValid(_sceneResult.Root) || !IsInstanceValid(node.Root) ||
            node.Root.IsQueuedForDeletion() || !_sceneResult.Root.IsAncestorOf(node.Root)) return false;
        if (Roster.TryGetValue(node.Source.InstanceIndex, out var enemy))
            return !enemy.Retired && ReferenceEquals(enemy.Node, node);
        return !Roster.PreviouslyOwned(node.Source.InstanceIndex);
    }

    private bool TryOwnedRuntime(RuntimeWorldScene.DynamicObjectNode node, out Rac1MobyRuntimeInstance? instance)
    {
        instance = null;
        return OwnsNode(node) &&
            Context.MobyRuntime.TryGet(new(node.Source.NativeClassId, node.Source.InstanceIndex), out instance) &&
            instance is not null && instance.EntityState.Identity.Matches(node.Source);
    }

    internal bool IsRac1GameplayActive(RuntimeWorldScene.DynamicObjectNode node) =>
        TryOwnedRuntime(node, out var runtime) && runtime!.IsActive;

    internal static bool HasRac1HostContactGeometry(
        RuntimeWorldScene.DynamicObjectNode node) =>
        node.Root.GetChildren().Any(child => child is MeshInstance3D);

    internal void SetRac1HostedPresence(
        RuntimeWorldScene.DynamicObjectNode node,
        RuntimeEntityPresence presence)
    {
        if (!TryOwnedRuntime(node, out var runtime)) return;
        Context.MobyRuntime.SetTransform(
            runtime!,
            RuntimeWorldScene.ToRuntimeTransform(node.Root.Transform));
        node.ApplyState(Context.MobyRuntime.SetPresence(runtime!, presence));
        _ports.Combat.CratePresence(node.Source.InstanceIndex, presence);
        if (_rac1Enemies.TryGetValue(node.Source.InstanceIndex, out var enemy))
        {
            enemy.Body.CollisionLayer = presence == RuntimeEntityPresence.Active && !enemy.RewardEmitted ? 1u : 0;
            if (presence != RuntimeEntityPresence.Active) SetRac1EnemyFlame(enemy, false);
        }
    }

    internal static void SelectRac1EnemySequence(Rac1HostedEnemy enemy, int sequence, int blendTicks)
    {
        if (enemy.Retired) return;
        if (enemy.Animation.Sequence != sequence) enemy.Animation.Select(sequence, blendTicks);
    }

    internal void MoveRac1Enemy(Rac1HostedEnemy enemy, NVector3 look, NVector3? destination,
        float yawOffset = 0, bool sweeping = false, float? pathDistance = null, bool directPosition = false)
    {
        if (!Owns(enemy)) return;
        Vector3 start = enemy.Body.GlobalPosition;
        NVector3 motion = enemy.Motion!.Step(ToEffectNative(start), look, destination, enemy.Animation.Blending, enemy.Grounded,
            yawOffset, sweeping, pathDistance);
        enemy.Node.Root.Rotation = new(0, enemy.Motion!.Yaw, 0);
        if (directPosition)
        {
            // 0x2e0fa4..0x2e0fb0 adds the entrance displacement directly to
            // Moby+0x10. The authored path owns Z as well as XY; host support
            // projection can leave its endpoint permanently out of reach.
            enemy.Body.GlobalPosition += FromEffectNative(motion);
            enemy.Node.Root.GlobalPosition = enemy.Body.GlobalPosition;
        }
        else MoveRac1EnemyContact(enemy, Rac1EnemyContactMotion.PrepareOrdinary(
            enemy.Node.Source.NativeClassId, enemy.Grounded, enemy.Reaction is not null, FromEffectNative(motion)));
    }

    internal void StepRac1EnemyReaction(Rac1HostedEnemy enemy)
    {
        if (!Owns(enemy)) return;
        if (enemy.Reaction is not { } reaction) return;
        enemy.Node.Root.Rotation = new(0, reaction.FacingYaw, 0);
        MoveRac1EnemyContact(enemy, FromEffectNative(reaction.Step()));
    }

    internal void MoveRac1EnemyContact(Rac1HostedEnemy enemy, Vector3 remaining)
    {
        if (!Owns(enemy)) return;
        for (int slide = 0; slide < 3 && remaining.LengthSquared() > 1e-9f; slide++)
        {
            var collision = enemy.Body.MoveAndCollide(remaining);
            if (collision is null) break;
            enemy.Reaction?.AcceptHostContact(ToEffectNative(collision.GetNormal()));
            remaining = collision.GetRemainder().Slide(collision.GetNormal());
        }
        bool rising = enemy.Reaction?.Velocity.Z > 0;
        enemy.Grounded = !rising && TryGroundRac1Enemy(enemy, upwardReach: 0, downwardReach: .3f);
        enemy.Node.Root.GlobalPosition = enemy.Body.GlobalPosition;
    }

    internal bool TryGroundRac1Enemy(Rac1HostedEnemy enemy, float upwardReach, float downwardReach)
    {
        if (!Owns(enemy)) return false;
        // Host contact policy, independent of native steering. Sweep the actual
        // contact shape: snapping its origin to a point ray can put the uphill
        // side of the capsule inside sloped terrain again after depenetration.
        // Actors are not ground support; exclude Ratchet and other enemies.
        var from = enemy.Body.GlobalTransform;
        from.Origin += Vector3.Up * upwardReach;
        using var parameters = new PhysicsTestMotionParameters3D
        {
            From = from,
            Motion = Vector3.Down * (upwardReach + downwardReach),
            Margin = enemy.Body.SafeMargin,
            RecoveryAsCollision = true,
            MaxCollisions = 4,
            ExcludeBodies = new global::Godot.Collections.Array<Rid>(_rac1Enemies.Values.Select(other => other.Body.GetRid()).Append(_player!.GetRid())),
        };
        using var result = new PhysicsTestMotionResult3D();
        if (!PhysicsServer3D.BodyTestMotion(enemy.Body.GetRid(), parameters, result)) return false;
        for (int i = 0; i < result.GetCollisionCount(); i++)
        {
            Vector3 normal = result.GetCollisionNormal(i);
            if (normal.Y <= .65f) continue;
            enemy.Body.GlobalPosition = from.Origin + result.GetTravel();
            enemy.Reaction?.AcceptHostContact(ToEffectNative(normal));
            return true;
        }
        return false;
    }

    internal void SynchronizeRac1Enemy(Rac1HostedEnemy enemy)
    {
        if (Owns(enemy)) SynchronizeEnemyTransform(enemy);
    }

    private void SynchronizeEnemyTransform(Rac1HostedEnemy enemy)
    {
        var instance = Context.MobyRuntime.Require(enemy.Node.Source);
        Context.MobyRuntime.SetTransform(instance, RuntimeWorldScene.ToRuntimeTransform(enemy.Node.Root.Transform));
        enemy.Node.ApplyState(instance.EntityState);
    }

    internal Vector3 EnemySocket(Rac1HostedEnemy enemy) => enemy.Node.Root.ToGlobal(FromEffectNative(enemy.Animation.ContactSocket));

    internal bool EnemyMeleeTouchesPlayer(Rac1HostedEnemy enemy)
    {
        if (!Owns(enemy)) return false;
        // 0x25998c passes the joint position preserved in s6 to the sphere
        // query. The forward/up vector built separately belongs to its damage
        // descriptor; adding it here moves the hit volume past a nearby player.
        Vector3 center = EnemySocket(enemy);
        Vector3 feet = _player!.GlobalPosition;
        const float radius = Rac1CollisionProfile.OrdinaryHorizontalRadius;
        Vector3 nearest = new(feet.X, Mathf.Clamp(center.Y, feet.Y + radius,
            feet.Y + Rac1CollisionProfile.OrdinaryUpperContactHeight), feet.Z);
        float separation = center.DistanceTo(nearest);
        return separation <= Rac1EnemyMotion.Class749ContactRadius + radius &&
            (separation <= .00001f || EnemyRayTouchesPlayer(enemy, center, nearest));
    }

    internal Rac1GameplayEntityRef? EnemySweepContact(Rac1HostedEnemy enemy)
    {
        Vector3 socket = EnemySocket(enemy);
        return EnemyRayContact(enemy, socket, enemy.Node.Root.GlobalPosition + Vector3.Up * .5f) ??
            EnemyRayContact(enemy, socket, socket + EnemyForward(enemy.Node) * Rac1Class1440Session.ContactLength);
    }

    internal bool EnemyRayTouchesPlayer(Rac1HostedEnemy enemy, Vector3 start, Vector3 end)
        => EnemyRayContact(enemy, start, end) == Rac1GameplayEntityRef.Player;

    internal Rac1GameplayEntityRef? EnemyRayContact(Rac1HostedEnemy enemy, Vector3 start, Vector3 end)
    {
        if (!Owns(enemy)) return null;
        if (start.DistanceSquaredTo(end) < 1e-9f) return null;
        var query = PhysicsRayQueryParameters3D.Create(start, end);
        query.Exclude = new global::Godot.Collections.Array<Rid> { enemy.Body.GetRid() };
        query.HitFromInside = true;
        var hit = enemy.Body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!hit.TryGetValue("collider", out var collider)) return null;
        var body = collider.AsGodotObject();
        if (body == _player) return Rac1GameplayEntityRef.Player;
        var other = _rac1Enemies.Values.FirstOrDefault(candidate => candidate.Body == body);
        return other is null ? null : Rac1GameplayEntityRef.Moby(new(other.Node.Source.NativeClassId, other.Node.Source.InstanceIndex));
    }

    internal void ApplyRac1EnemyPlayerDamage(Rac1GameplayDamageEvent damage)
    {
        if (_disposed || !_ports.Neighbors.Current() || !_ports.Player.Alive()) return;
        var before = _rac1Nanotech.Probe();
        Context.DamageTransport.Publish(damage);
        if (_ports.Player.Persistent())
        {
            // Validate the recovered descriptor in its source module before
            // translating its admitted integer amount to the selected victim.
            _ = new Rac1RatchetNanotechSession().ApplyDamage(damage);
            var attacker = (_sceneResult?.DynamicObjectNodes ?? []).Concat(Rac1EnemyNodes).Concat(Presentation.CrateNodes).Concat(Presentation.HostileNodes.Values)
                .FirstOrDefault(node => node.Source.SourceGame == ObpSourceGame.Rac1 &&
                node.Source.NativeClassId == damage.Source.NativeClassId && node.Source.InstanceIndex == damage.Source.RuntimeId);
            if (attacker is null) { GD.PushWarning("Player damage source has no registered native actor."); return; }
            _ports.Player.SourceDamage(attacker, _clock.Frame, checked((int)damage.NativeDamage), damage.NativeDamageFlags ?? 0);
            return;
        }
        var after = _rac1Nanotech.ApplyDamage(damage);
        _player!.Rac1GameplayState = after;
        if (after.Nanotech == before.Nanotech) return;
        _ports.Player.Hurt(before.Nanotech, after.Nanotech);
        Context.CombatStatus = $"enemy hit: Nanotech {after.Nanotech}/{after.RespawnNanotech}";
        _ports.Player.Hud([Rac1HudProjection.DamageFeedback(before, after)]);
        GD.Print($"[rac1-enemies] class{damage.Source.NativeClassId} i{damage.Source.RuntimeId}: contact; Nanotech {after.Nanotech}");
    }

}
