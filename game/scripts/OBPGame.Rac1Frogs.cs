using Godot;
using OBP.RAC1.Animation;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using System.Collections.Generic;
using NVector3 = System.Numerics.Vector3;

namespace OneBigPackage;

/// <summary>Level 6/10 class-827 host adapters only. Native selectors, combat
/// transitions and damage ownership belong to Rac1FrogSession.</summary>
public partial class OBPGame
{
    private Rac1FrogSession? _rac1Frogs;
    private Rac1FrogShuttleSwitch? _rac1FrogShuttle;
    private Rac1FrogWaveTriggers? _rac1FrogWaves;
    private Rac1FrogQueenGroup? _rac1FrogQueen;
    private sealed record HostedFrogWave(RuntimeWorldScene.DynamicObjectNode Node,
        Rac1MobySequenceClock Animation);
    private readonly Dictionary<int, HostedFrogWave> _rac1FrogWaveNodes = [];
    private const float FrogHostSleepRadius = 30f;
    private const float FrogHostAggroRadius = 12f; // provisional common-target query

    private void ConfigureRac1Frogs(RuntimeWorld world, RuntimeWorldScene.Result result,
        IReadOnlyList<NVector3[]> paths, IReadOnlyList<Rac1ClipVolume> volumes,
        List<RuntimeWorldScene.DynamicObjectNode> nodes)
    {
        _rac1Frogs = new Rac1FrogSession(_rac1MobyRuntime, paths);
        _rac1FrogShuttle = world.LevelId == 6
            ? Rac1FrogShuttleSwitch.FromRetail(world.DynamicObjects ?? [], volumes) : null;
        foreach (var authored in (world.DynamicObjects ?? []).Where(item => item.NativeClassId == Rac1FrogAuthored.ClassId))
        {
            var node = FindPresentedDynamic(result, authored)
                ?? throw new InvalidDataException("Original Frog mesh unavailable.");
            var forward = ToEffectNative(EnemyForward(node));
            var frame = _rac1Frogs.Register(authored, node.State,
                ToEffectNative(node.Root.GlobalPosition), MathF.Atan2(forward.Y, forward.X));
            node.ApplyState(_rac1MobyRuntime.Require(authored).EntityState);
            nodes.Add(node);
        }
    }

    private void ConfigureRac1FrogQueen(RuntimeWorld world, IReadOnlyList<int[]> groups)
    {
        if (world.LevelId != 6) return;
        var queen = (world.DynamicObjects ?? [])
            .SingleOrDefault(source => source.NativeClassId == Rac1FrogQueenGroup.QueenClassId);
        if (queen is null)
            throw new InvalidDataException("Original class1051 Alien Queen is missing from LEVEL6.");
        _rac1FrogQueen = new(queen, groups,
            (world.DynamicObjects ?? []).Where(m => m.NativeClassId == Rac1FrogSession.ClassId)
                .Select(m => m.InstanceIndex));
        GD.Print($"[frog-queen] original class1051 i{queen.InstanceIndex}, native group {_rac1FrogQueen.GroupIndex}, {_rac1FrogQueen.Members.Count} source Frogs; waiting for real boss event");
    }

    /// <summary>Only a verified Queen state3/sequence6 completion or a
    /// genuine Queen state4 update may feed this native subroutine.
    /// The Queen boss AI itself has not yet been hosted: no player-distance
    /// or elapsed-time fallback is permitted to emit this event.</summary>
    private int ObserveRac1QueenFrogEvent(int queenState, int queenSequence, bool completed)
    {
        if (_rac1FrogQueen is null || _rac1Frogs is null) return 0;
        int count = 0;
        foreach (var candidate in _rac1FrogQueen.ObserveNativeUpdate(
            queenState, queenSequence, completed,
            id => ToEffectNative(_rac1Enemies[id].Node.Root.GlobalPosition), Rac1EffectRandom))
            if (LaunchRac1FrogFromNativeProducer(candidate.FrogInstance,
                candidate.NativePosition, Rac1FrogLaunchProducer.QueenGroup))
                count++;
        return count;
    }

    private void ConfigureRac1FrogWaves(RuntimeWorld world, RuntimeWorldScene.Result result,
        IReadOnlyList<int[]> groups, IReadOnlyList<Rac1ClipVolume> volumes,
        Rac1StaticClasses.MobyClass cls, ReadOnlySpan<byte> classBytes)
    {
        if (_rac1Frogs is null) throw new InvalidOperationException("Frogs must be registered before their native producers.");
        var authored = (world.DynamicObjects ?? [])
            .Where(m => m.NativeClassId == Rac1FrogWaveTriggers.ClassId).ToArray();
        _rac1FrogWaves = new(groups,
            (world.DynamicObjects ?? []).Where(m => m.NativeClassId == Rac1FrogSession.ClassId)
                .Select(m => m.InstanceIndex));
        if (authored.Length == 0) return;
        var first = FindPresentedDynamic(result, authored[0])
            ?? throw new InvalidDataException("Class1066 original trigger mesh missing.");
        var asset = Rac1MobyAnimationAssets.Bake(cls, first.Source.Meshes, classBytes,
            skipUnsupportedTiming: true);
        if (!Enumerable.Range(0, 3).All(asset.Clips.ContainsKey))
            throw new InvalidDataException("Class1066 lacks original animation sequences 0, 1 or 2.");

        foreach (var controller in authored)
        {
            var node = FindPresentedDynamic(result, controller)
                ?? throw new InvalidDataException("Class1066 original trigger mesh missing.");
            _rac1FrogWaves.Register(controller, volumes,
                ToEffectNative(node.Root.GlobalPosition));
            node.ConfigureAnimations(asset.Presentation);
            var clock = new Rac1MobySequenceClock(asset, 0);
            node.ApplyAnimationSample(clock.Sample);
            _rac1FrogWaveNodes.Add(controller.InstanceIndex, new(node, clock));
        }
        GD.Print($"[frog-wave] {_rac1FrogWaveNodes.Count} original class1066 trigger actors with retail clips 0/1/2 and signed Moby groups");
    }

    private void TickRac1FrogWaves()
    {
        if (_rac1FrogWaves is null || _rac1Frogs is null || _player is null) return;
        NVector3 player = ToEffectNative(_player.GlobalPosition);
        foreach (var pair in _rac1FrogWaveNodes.OrderBy(pair => pair.Key))
        {
            var view = pair.Value;
            int prior = _rac1FrogWaves.State(pair.Key);
            if (prior == 3) continue;
            // OBP host safety: freeze a still-unreleased native wave while its
            // Frog group's shuttle switch is inactive. Otherwise the 20-tick
            // selector could exhaust its list without ever launching its
            // hidden members, losing an authored wave permanently.
            if (_rac1FrogWaves.Members(pair.Key).Any(index =>
                    !_rac1Frogs.GetAuthored(index).UpdateEligible(_rac1Frogs.ShuttleSwitchEnabled)))
                continue;
            foreach (int sound in view.Animation.Tick())
                if (view.Node.Root.GlobalPosition.DistanceTo(_player.GlobalPosition) < 40f)
                    PlayRac1PickupSound(Rac1FrogWaveTriggers.ClassId, sound,
                        view.Node.Root.GlobalPosition);
            var tick = _rac1FrogWaves.Step(pair.Key, player, view.Animation.Wrapped,
                index => _rac1Frogs.Get(index).NativeState == 15 &&
                    _rac1Frogs.GetAuthored(index).UpdateEligible(_rac1Frogs.ShuttleSwitchEnabled));
            if (tick.PreviousState != tick.State)
            {
                if (tick.State == 1) view.Animation.Select(1, 5);
                else if (tick.State == 2) view.Animation.Select(2, 5);
                GD.Print($"[frog-wave] class1066 i{pair.Key} group{_rac1FrogWaves.Group(pair.Key)} state{tick.PreviousState}->{tick.State}");
            }
            if (tick.LaunchInstance is int index)
                LaunchRac1FrogFromNativeProducer(index,
                    tick.NativeLaunchPosition, Rac1FrogLaunchProducer.IntervalTrigger);
            view.Node.ApplyAnimationSample(view.Animation.Sample);
        }
    }

    private void TickRac1FrogShuttleSwitch()
    {
        if (_rac1FrogShuttle is null || _rac1Frogs is null || _player is null) return;
        // The LEVEL6 shuttle's class-1109 waiting-state volume test is native.
        // Its cutscene/travel state is not yet hosted; only wait-volume changes
        // are used here, never implied arrivals or generic nearby-Frog wakeups.
        if (_rac1FrogShuttle.ObserveWaitingShuttle(ToEffectNative(_player.GlobalPosition)))
            ApplyRac1FrogShuttleSelection();
    }

    private void ApplyRac1FrogShuttleSelection()
    {
        if (_rac1FrogShuttle is null || _rac1Frogs is null) return;
        foreach (int instance in _rac1Frogs.ApplyShuttleSwitch(_rac1FrogShuttle.SwitchEnabled))
        {
            if (!_rac1Enemies.TryGetValue(instance, out var enemy)) continue;
            bool active = _rac1Frogs.Get(instance).Presented;
            enemy.Body.CollisionLayer = active ? 1u : 0u;
            enemy.Body.CollisionMask = active ? 1u : 0u;
            enemy.Node.ApplyState(_rac1MobyRuntime.Require(enemy.Node.Source).EntityState);
        }
    }

    /// <summary>
    /// Only a witnessed LEVEL6 native producer/group callback may call this.
    /// Original Moby group IDs are decoded. Native Queen AI still does not
    /// emit its boss-owned event during ordinary play; proximity never does.
    /// </summary>
    private bool LaunchRac1FrogFromNativeProducer(int instance, NVector3 nativePosition,
        Rac1FrogLaunchProducer producer)
    {
        if (_rac1Frogs is null || !_rac1Enemies.TryGetValue(instance, out var enemy))
            return false;
        var launched = _rac1Frogs.LaunchFromNativeProducer(instance, nativePosition, producer);
        if (launched is null) return false;
        Vector3 position = FromEffectNative(launched.NativePosition);
        enemy.Node.Root.GlobalPosition = position;
        enemy.Node.Root.Rotation = new(0, launched.Frame.Yaw, 0);
        enemy.Body.GlobalPosition = position;
        enemy.Body.CollisionLayer = 1;
        enemy.Body.CollisionMask = 1;
        enemy.Grounded = false; // Native state6 awaits mine_fall landing.
        SynchronizeRac1Enemy(enemy);
        GD.Print($"[frog-launch] native {producer} activated class827 i{instance} state15->6");
        return true;
    }

    private void TickRac1Frog(Rac1HostedEnemy enemy)
    {
        if (_rac1Frogs is null || _player is null) return;
        var node = enemy.Node;
        int index = node.Source.InstanceIndex;
        var authored = _rac1Frogs.GetAuthored(index);
        Vector3 goal = ArsenalEnemyGoal(enemy, _player.GlobalPosition);
        bool eligible = goal.DistanceSquaredTo(node.Root.GlobalPosition) <
            FrogHostAggroRadius * FrogHostAggroRadius;
        bool hitsPlayer = true;
        if (authored.TargetInstance >= 0)
        {
            // Fixed target_id+0x204 addresses a native Moby, not Ratchet.
            // Never silently turn this special path into player pursuit.
            if (_rac1LinkedTargetNodes.TryGetValue(authored.TargetInstance, out var linked) &&
                IsInstanceValid(linked.Root) && IsRac1GameplayActive(linked))
            {
                goal = linked.Root.GlobalPosition;
                eligible = true;
            }
            else eligible = false;
            hitsPlayer = false;
        }
        var old = _rac1Frogs.Get(index);
        var native = _rac1Frogs.Step(index,
            ToEffectNative(node.Root.GlobalPosition), ToEffectNative(goal), eligible,
            enemy.Animation.Wrapped, enemy.Animation.Position,
            hitsPlayer && EnemyMeleeTouchesPlayer(enemy), enemy.Grounded);
        if (native.Sequence != enemy.Animation.Sequence)
            SelectRac1EnemySequence(enemy, native.Sequence, 10);
        if (native.Displacement.LengthSquared() > 0)
            MoveRac1EnemyContact(enemy, FromEffectNative(native.Displacement));
        node.Root.Rotation = new(0, native.Yaw, 0);
        if (native.Contact && !ArsenalEnemyHitsDecoy(enemy))
            ApplyRac1EnemyPlayerDamage(Rac1FrogContact.DamageToPlayer(index));
    }
}
