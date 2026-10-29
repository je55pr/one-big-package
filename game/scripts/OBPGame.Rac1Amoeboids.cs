using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;
using NVector2 = System.Numerics.Vector2;

namespace OBP.Game;

public partial class OBPGame
{
    private Rac1AmoeboidFamilySession? _rac1Amoeboids;
    private IReadOnlyList<Rac1ClipVolume> _rac1AmoeboidVolumes = [];
    private readonly Dictionary<int, List<RuntimeWorldScene.DynamicObjectNode>> _rac1AmoeboidTargetGroups = [];
    private readonly Rac1Class623ActivationSession _rac1Class623Activation = new();
    private readonly Rac1Class830ControllerSession _rac1Class830Controllers = new();
    private readonly Rac1AmoeboidLungeTargetLatch _rac1AmoeboidLungeTargets = new();
    private const float Rac1AmoeboidHostActiveRadius = 45f;
    // Milestone policy: a live class-623 receiver is NOT wired into Godot.
    // Avoid sending otherwise playable lunges to an undamageable prop.
    // Switch this only when that receiver genuinely dispatches the hit.
    private const bool Rac1Class623HitReceiverAvailable = false;

    private void ResetRac1Amoeboids()
    {
        _rac1Amoeboids = null;
        _rac1AmoeboidVolumes = [];
        _rac1AmoeboidTargetGroups.Clear();
        _rac1Class623Activation.Clear();
        _rac1Class830Controllers.Clear();
        _rac1AmoeboidLungeTargets.Clear();
    }

    private void ConfigureRac1Amoeboids(RuntimeWorld world, RuntimeWorldScene.Result result,
        List<RuntimeWorldScene.DynamicObjectNode> presented, byte[] gameplay)
    {
        if (world.LevelId is not (1 or 5 or 11)) return;
        _rac1AmoeboidVolumes = Rac1ClipVolumes.Read(gameplay);
        var instances = Rac1Instances.Parse(gameplay).MobyInstances;
        _rac1AmoeboidTargetGroups.Clear();
        _rac1Class623Activation.Clear();
        _rac1Class830Controllers.Clear();
        // Source-owned controller roster, not a guessed live state machine.
        // Live state-0 lookup/owner facts have not yet been imported, so
        // every controller stays uncompleted until real native facts arrive.
        if (world.LevelId == 5)
            foreach (var placement in instances.Where(x => x.OClass == 830))
                _rac1Class830Controllers.Register(placement.Index, placement.OClass,
                    placement.PVar ?? throw new InvalidDataException("Class-830 controller has no PVar."));
        foreach (var candidate in (world.DynamicObjects ?? [])
            .Where(moby => moby.NativeClassId == Rac1AmoeboidTargetSelection.TargetClassId))
        {
            var node = Rac1WorldPresentation.FindPresentedDynamic(result, candidate);
            if (node is null) continue; // No source-backed presented target.
            var placement = instances[candidate.InstanceIndex];
            int group = Rac1AmoeboidAuthored.ReadGroup(placement.RawRecord);
            // State 0 clears the targeting flag in authored state-12/15
            // gates; only the independent volume activation is hosted.
            var pvar = placement.PVar ??
                throw new InvalidDataException("Class-623 target has no PVar.");
            _rac1Class623Activation.Register(candidate.InstanceIndex, placement.OClass, pvar);
            var links = _rac1Class623Activation.Links(candidate.InstanceIndex);
            if (links.LinkedMobyIndex >= 0 &&
                (links.LinkedMobyIndex >= instances.Count ||
                 instances[links.LinkedMobyIndex].OClass != 830))
                throw new InvalidDataException("Class-623 target references a non-controller Moby.");
            if (links.TriggerVolumeIndex >= _rac1AmoeboidVolumes.Count)
                throw new InvalidDataException("Class-623 target references a missing trigger volume.");
            if (!_rac1AmoeboidTargetGroups.TryGetValue(group, out var members))
                _rac1AmoeboidTargetGroups.Add(group, members = []);
            members.Add(node);
        }
        _rac1Amoeboids = new(Rac1World.Context.MobyRuntime,
            Rac1AmoeboidPathRouter.Read(gameplay),
            // Exact source 0x276fe8 waypoint/mask search, but Godot's
            // world ray query substitutes for native 0x261968 segment collision.
            (_, from, to) => !BombHasLineOfSight(
                FromEffectNative(from), FromEffectNative(to)));
        foreach (var source in (world.DynamicObjects ?? [])
            .Where(moby => Rac1AmoeboidAuthored.IsFamily(moby.NativeClassId)))
        {
            var placement = instances[source.InstanceIndex];
            var authored = Rac1AmoeboidAuthored.Read(placement.OClass,
                placement.PVar ?? throw new InvalidDataException("Amoeboid has no authored PVar."));
            var node = Rac1WorldPresentation.FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException($"Native Amoeboid i{source.InstanceIndex} has no original mesh.");
            var born = _rac1Amoeboids.Register(source, node.State, authored,
                Rac1AmoeboidAuthored.ReadGroup(placement.RawRecord),
                new(placement.Position.X, placement.Position.Y, placement.Position.Z),
                placement.Rotation.Z);
            node.ApplyState(Rac1World.Context.MobyRuntime.Require(source).EntityState);
            // The original state12 and state13 actors aren't drawn or solid.
            if (!born.Active) node.Root.Visible = false;
            presented.Add(node);
        }
        GD.Print($"[rac1-amoeboids] L{world.LevelId}: {_rac1Amoeboids.RegisteredCount} authored, " +
            $"{_rac1Amoeboids.ActiveCount} active, {_rac1Amoeboids.GatedCount} gated, " +
            $"{_rac1Amoeboids.DormantCount} reserve");
    }

    private void InitializeRac1AmoeboidPresentation(Rac1HostedEnemy enemy)
    {
        if (_rac1Amoeboids is null) return;
        var frame = _rac1Amoeboids.Get(enemy.Node.Source.InstanceIndex);
        if (!frame.Active)
        {
            enemy.Body.CollisionLayer = 0;
            enemy.Body.CollisionMask = 0;
            enemy.Node.Root.Visible = false;
        }
    }

    private void TickRac1AmoeboidGates()
    {
        if (_rac1Amoeboids is null || _player is null) return;
        var point = ToEffectNative(_player.GlobalPosition);
        // No fake native input is fed to class-830 based on scene
        // visibility/activity. The source controller session currently
        // returns an unknown completion, unless a genuine native input
        // provider is introduced; volume-driven admission is independent.
        _rac1Class623Activation.Advance(
            _rac1Class830Controllers.Snapshot,
            volume => _rac1AmoeboidVolumes[volume].Contains(point));
        foreach (var frame in _rac1Amoeboids.ActivateGates(point,
                     (volume, probe) => volume >= 0 && volume < _rac1AmoeboidVolumes.Count &&
                         _rac1AmoeboidVolumes[volume].Contains(probe)))
            ActivateRac1Amoeboid(frame);
    }

    private void ActivateRac1Amoeboid(Rac1AmoeboidFrame frame)
    {
        if (!Rac1World.Enemies.Roster.TryGetValue(frame.InstanceIndex, out var enemy))
            throw new InvalidOperationException("Split child lacks its original level Moby.");
        var node = enemy.Node;
        enemy.Body.GlobalPosition = FromEffectNative(frame.Position);
        node.Root.GlobalPosition = enemy.Body.GlobalPosition;
        var instance = Rac1World.Context.MobyRuntime.Require(node.Source);
        Rac1World.Context.MobyRuntime.SetTransform(instance, RuntimeWorldScene.ToRuntimeTransform(node.Root.Transform));
        node.Root.Visible = true;
        enemy.Body.CollisionLayer = 1;
        enemy.Body.CollisionMask = 1;
        node.ApplyState(instance.EntityState);
        Rac1EnemyHost.SelectRac1EnemySequence(enemy, frame.Sequence, 5);
        enemy.Grounded = false;
        enemy.Reaction = frame.NativeState == Rac1AmoeboidFamilySession.KnockbackState
            ? new Rac1MobyReaction(Rac1AmoeboidKnockbackProfile.Read(
                frame.ClassId, instance.PVar.Span), frame.Yaw)
            : null;
    }

    private void TickRac1Amoeboid(Rac1HostedEnemy enemy)
    {
        if (_rac1Amoeboids is null || _player is null) return;
        var node = enemy.Node;
        var before = _rac1Amoeboids.Get(node.Source.InstanceIndex);
        if (node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) >
            Rac1AmoeboidHostActiveRadius * Rac1AmoeboidHostActiveRadius &&
            before.NativeState is not Rac1AmoeboidFamilySession.KnockbackState)
            return;
        if (enemy.Reaction is not null)
            Rac1World.Enemies.StepRac1EnemyReaction(enemy);
        var pos = ToEffectNative(node.Root.GlobalPosition);
        var target = ToEffectNative(ArsenalEnemyGoal(enemy, _player.GlobalPosition));
        Rac1GameplayEntityRef selectedTarget = Rac1GameplayEntityRef.Player;
        int candidateGroup = _rac1Amoeboids.TargetCandidateGroup(node.Source.InstanceIndex);
        if (candidateGroup >= 0 && _rac1AmoeboidTargetGroups.TryGetValue(candidateGroup, out var targets))
        {
            var nearest = Rac1AmoeboidTargetSelection.SelectForHostedCombat(
                Rac1Class623HitReceiverAvailable, candidateGroup, pos, target,
                _rac1Amoeboids.HostDetectionRange(node.Source.InstanceIndex),
                targets.Select(candidate => new Rac1AmoeboidTargetSelection.Candidate(
                    candidate.Source.InstanceIndex, candidate.Source.NativeClassId, candidateGroup,
                    ToEffectNative(candidate.Root.GlobalPosition),
                    candidate.Root.Visible &&
                        candidate.State.Presentation.Presence == RuntimeEntityPresence.Active &&
                        _rac1Class623Activation.Get(candidate.Source.InstanceIndex).HasTargetableFlag,
                    _rac1Class623Activation.Get(candidate.Source.InstanceIndex).NativeState)));
            if (nearest is not null)
            {
                target = nearest.Position;
                selectedTarget = Rac1GameplayEntityRef.Moby(
                    new(Rac1AmoeboidTargetSelection.TargetClassId, nearest.InstanceIndex));
            }
        }
        // A target change during an existing lunge is not permission to hit
        // the new victim. This is a conservative Godot attribution guard
        // until the native class-623 target pointer/hit receiver is recovered.
        int sourceIndex = node.Source.InstanceIndex;
        // LOS only tests obstruction; it cannot establish a melee overlap.
        // Before accepting contact with Ratchet, also require overlap of
        // the actual foot-aligned Godot player/enemy capsule height spans.
        // This remains an explicit host substitute for the retail contact solver.
        bool contact = _rac1AmoeboidLungeTargets.AcceptsContact(sourceIndex, selectedTarget) &&
            NVector2.Distance(new(pos.X, pos.Y), new(target.X, target.Y)) < Rac1AmoeboidFamilySession.NativeAttackRange &&
            (selectedTarget != Rac1GameplayEntityRef.Player ||
                (enemy.Body.GetChild<CollisionShape3D>(0).Shape is CapsuleShape3D attackerCapsule &&
                 _player.GetChildren().OfType<CollisionShape3D>().SingleOrDefault()?.Shape is CapsuleShape3D playerCapsule &&
                 Rac1AmoeboidHostMeleeContact.VerticalCapsulesOverlap(
                     pos, attackerCapsule.Height, target, playerCapsule.Height))) &&
            BombHasLineOfSight(node.Root.GlobalPosition + Vector3.Up * .6f,
                FromEffectNative(target) + Vector3.Up * .6f);
        var attackTarget = _rac1AmoeboidLungeTargets.Current(sourceIndex) ?? selectedTarget;
        var next = _rac1Amoeboids.Step(sourceIndex, pos, target,
            enemy.Animation.Wrapped, enemy.Grounded, contact, enemy.Animation.Position);
        _rac1AmoeboidLungeTargets.Observe(sourceIndex, next.NativeState, selectedTarget);
        if (before.NativeState == Rac1AmoeboidFamilySession.KnockbackState &&
            next.NativeState == Rac1AmoeboidFamilySession.KnockbackRecoveryState)
            enemy.Reaction = null;
        Rac1EnemyHost.SelectRac1EnemySequence(enemy, next.Sequence, 10);
        if (enemy.Reaction is null && next.Displacement.LengthSquared() > 0)
            Rac1World.Enemies.MoveRac1EnemyContact(enemy, FromEffectNative(next.Displacement));
        node.Root.Rotation = new(0, next.Yaw, 0);
        // Only a lunge originally admitted against Ratchet can touch his
        // Nanotech. The native class-623 receiver is still unimplemented.
        if (next.Attack && attackTarget == Rac1GameplayEntityRef.Player &&
            !ArsenalEnemyHitsDecoy(enemy))
            Rac1World.Enemies.ApplyRac1EnemyPlayerDamage(Rac1AmoeboidAttack.ToPlayer(next.ClassId, next.InstanceIndex));
    }

    private bool ApplyRac1AmoeboidDamage(RuntimeWorldScene.DynamicObjectNode node,
        Rac1GameplayDamageEvent damage)
    {
        if (_rac1Amoeboids is null || !Rac1World.Context.MobyRuntime.CanDispatchDamage(damage)) return false;
        var result = Rac1World.Context.MobyRuntime.DispatchDamage<Rac1AmoeboidDamageResult>(damage);
        var parent = Rac1World.Enemies.Roster[node.Source.InstanceIndex];
        parent.Body.CollisionLayer = 0;
        parent.Body.CollisionMask = 0;
        node.ApplyState(Rac1World.Context.MobyRuntime.Require(node.Source).EntityState);
        node.Root.Visible = false;
        foreach (var child in result.Children)
            ActivateRac1Amoeboid(child);
        GD.Print($"[rac1-amoeboids] class{node.Source.NativeClassId} i{node.Source.InstanceIndex} " +
            $"split into [{string.Join(",", result.Children.Select(x => x.InstanceIndex))}] " +
            $"health={result.Parent.Health}");
        return true;
    }
}
