using Godot;
using OBP.RAC1.Animation;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using System.Collections.Generic;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>Level 6/10 class-827 host adapters only. Native selectors, combat
/// transitions and damage ownership belong to Rac1FrogSession.</summary>
public partial class OBPGame
{
    private Rac1FrogSession? _rac1Frogs;
    private Rac1FrogShuttleSwitch? _rac1FrogShuttle;
    private Rac1FrogWaveTriggers? _rac1FrogWaves;
    private Rac1FrogQueenGroup? _rac1FrogQueen;
    private Rac1AlienQueenRecovery? _rac1QueenRecovery;
    private Rac1AlienQueenEntrance? _rac1QueenEntrance;
    private Rac1AlienQueenFlight? _rac1QueenFlight;
    private float _rac1QueenAuthoredNativeZ;
    private NVector3 _rac1QueenCameraAnchor;
    private Rac1AlienQueenFlightCamera? _rac1QueenPendingCamera;
    private Camera3D? _rac1QueenScriptedCamera;
    private Rac1AlienQueenLinkedGate? _rac1QueenLinkedGate;
    private Rac1AlienQueenGateNative? _rac1QueenGateNative;
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _rac1QueenLinkedNodes = [];
    private readonly Dictionary<int, Quaternion> _rac1QueenLinkedBaseRotations = [];
    private Rac1MobySequenceClock? _rac1QueenAnimation;
    private RuntimeWorldScene.DynamicObjectNode? _rac1QueenNode;
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

    private void ConfigureRac1FrogQueen(RuntimeWorld world, RuntimeWorldScene.Result result,
        IReadOnlyList<int[]> groups, Rac1StaticClasses.MobyClass cls,
        ReadOnlySpan<byte> classBytes, Rac1AlienQueenGateNative tuning,
        NVector3 cameraAnchor)
    {
        if (world.LevelId != 6) return;
        _rac1QueenGateNative = tuning;
        _rac1QueenCameraAnchor = cameraAnchor;
        var queen = (world.DynamicObjects ?? [])
            .SingleOrDefault(source => source.NativeClassId == Rac1FrogQueenGroup.QueenClassId);
        if (queen is null)
            throw new InvalidDataException("Original class1051 Alien Queen is missing from LEVEL6.");
        _rac1FrogQueen = new(queen, groups,
            (world.DynamicObjects ?? []).Where(m => m.NativeClassId == Rac1FrogSession.ClassId)
                .Select(m => m.InstanceIndex));
        _rac1QueenRecovery = new(queen);
        _rac1QueenEntrance = new(queen, (world.DynamicObjects ?? [])
            .ToDictionary(m => m.InstanceIndex, m => m.NativeClassId));
        foreach (int id in new[] { _rac1QueenEntrance.FirstLinkedInstance,
            _rac1QueenEntrance.SecondLinkedInstance })
        {
            var parent = (world.DynamicObjects ?? []).Single(m => m.InstanceIndex == id);
            var node = FindPresentedDynamic(result, parent)
                ?? throw new InvalidDataException($"Original class1021 linked parent i{id} mesh missing.");
            _rac1QueenLinkedNodes.Add(id, node);
            _rac1QueenLinkedBaseRotations.Add(id, node.Root.Quaternion);
        }
        _rac1QueenLinkedGate = new(queen, world.DynamicObjects ?? [],
            _rac1QueenLinkedNodes.ToDictionary(pair => pair.Key,
                pair => ToEffectNative(pair.Value.Root.GlobalPosition)));
        ConfigureRac1QueenLinkedChildren(result.Root);
        _rac1QueenNode = FindPresentedDynamic(result, queen)
            ?? throw new InvalidDataException("Original Alien Queen model is unavailable.");
        var queenSpawn = ToEffectNative(_rac1QueenNode.Root.GlobalPosition);
        _rac1QueenAuthoredNativeZ = queenSpawn.Z;
        // FUN_L06_002f9a28 native state0 elevates the Queen by 17.5
        // while it saves the authored ground height in PVar +0xc8.
        _rac1QueenNode.Root.GlobalPosition = FromEffectNative(new NVector3(
            queenSpawn.X, queenSpawn.Y, queenSpawn.Z + Rac1AlienQueenFlight.SourceHeight));
        var animationAsset = Rac1MobyAnimationAssets.Bake(cls, _rac1QueenNode.Source.Meshes,
            classBytes, skipUnsupportedTiming: true);
        if (!animationAsset.Clips.ContainsKey(6) || !animationAsset.Clips.ContainsKey(1))
            throw new InvalidDataException("Alien Queen requires original animations 1 and 6.");
        _rac1QueenNode.ConfigureAnimations(animationAsset.Presentation);
        _rac1QueenAnimation = new(animationAsset,
            animationAsset.Clips.ContainsKey(0) ? 0 : animationAsset.Clips.Keys.Min());
        _rac1QueenNode.ApplyAnimationSample(_rac1QueenAnimation.Sample);
        GD.Print($"[frog-queen] class1051 i{queen.InstanceIndex}, source linked class1021 i{_rac1QueenEntrance.FirstLinkedInstance}/i{_rac1QueenEntrance.SecondLinkedInstance}, native camera{_rac1QueenEntrance.CameraSelector}/path{_rac1QueenEntrance.NativePathSelector}; group{_rac1FrogQueen.GroupIndex} ({_rac1FrogQueen.Members.Count} Frogs), waiting for entrance callback");
    }

    /// <summary>
    /// Source helper FUN_L06_002f53e8 adjusts original class1021 parent
    /// and child visibility. Source rotation constants are read from retail;
    /// native child collision/contact remains separately unhosted.
    /// </summary>
    private void SynchronizeRac1QueenLinkedParents()
    {
        if (_rac1QueenLinkedGate is null) return;
        foreach (var pair in _rac1QueenLinkedNodes)
        {
            if (!IsInstanceValid(pair.Value.Root)) continue;
            var source = _rac1QueenLinkedGate.Get(pair.Key);
            pair.Value.Root.Visible = source.Visible;
            pair.Value.Root.Quaternion = _rac1QueenLinkedBaseRotations[pair.Key] *
                new Quaternion(Vector3.Up, source.RotationOffset);
        }
        SynchronizeRac1QueenLinkedChildVisibility();
    }

    /// <summary>
    /// The retail Queen target is exposed to weapon collision producers only
    /// after the real encounter admits a combat state. Her native entrance,
    /// camera and boss navigation remain independent source work.
    /// </summary>
    private IEnumerable<RuntimeWorldScene.DynamicObjectNode> Rac1QueenDamageTargets
    {
        get
        {
            if (_rac1QueenNode is { } node && IsInstanceValid(node.Root) &&
                _rac1QueenRecovery?.CanReceiveHostWeaponContact == true)
                yield return node;
        }
    }

    private bool ApplyRac1QueenWeaponDamage(RuntimeWorldScene.DynamicObjectNode target,
        Rac1GameplayDamageEvent damage)
    {
        if (_rac1QueenNode != target || _rac1QueenRecovery is null) return false;
        var result = _rac1QueenRecovery.ApplyNativeDamageEvent(damage);
        if (!result.Accepted) return false;
        _rac1DamageTransport.Publish(damage);
        GD.Print($"[frog-queen] real weapon event {damage.Source.Kind} class{damage.Source.NativeClassId}, health={result.Health}, recovery={result.RecoveryStarted}");
        if (result.DeathTriggered)
            _rac1QueenAnimation?.Select(10, 10);
        return true;
    }

    private void ResolveRac1QueenWrenchContacts(Vector3 previousHand, Vector3 previousTip,
        Vector3 hand, Vector3 tip, float radius, Rac1WrenchDamageResult result)
    {
        foreach (var queen in Rac1QueenDamageTargets)
        {
            var key = new Rac1MobyRuntimeKey(queen.Source.NativeClassId, queen.Source.InstanceIndex);
            if (_rac1WrenchHitTargets.Contains(key)) continue;
            bool contact = queen.Root.GetChildren().OfType<MeshInstance3D>().Any(mesh =>
            {
                Aabb bounds = (mesh.GlobalTransform * mesh.GetAabb()).Grow(radius);
                return WrenchSegmentIntersects(bounds, previousHand, previousTip) ||
                    WrenchSegmentIntersects(bounds, hand, tip) ||
                    WrenchSegmentIntersects(bounds, previousHand, hand) ||
                    WrenchSegmentIntersects(bounds, previousTip, tip) ||
                    WrenchSegmentIntersects(bounds, previousHand, tip) ||
                    WrenchSegmentIntersects(bounds, previousTip, hand);
            });
            if (!contact) continue;
            if (!ApplyRac1QueenWeaponDamage(queen, Rac1DamageRuntime.FromWrench(key, result)))
                continue;
            _rac1WrenchHitTargets.Add(key);
        }
    }

    /// <summary>
    /// Exclusive camera lease for the source class1051 opening shot. Do not
    /// steal from a Visibomb or another scripted camera. Only a real viewport
    /// camera installation acknowledges the original native landing helper.
    /// </summary>
    private bool ApplyRac1QueenScriptedCamera(Rac1AlienQueenFlightCamera? cue)
    {
        // A different viewport owner has superseded our camera lease. Retire
        // it immediately; otherwise the stale non-current camera can never
        // acknowledge completion or take the normal creation path again.
        // End only releases Queen-owned controls, preserving Visibomb's lock.
        if (_rac1QueenScriptedCamera is { } leased &&
            (!IsInstanceValid(leased) || !leased.Current ||
                GetViewport().GetCamera3D() != leased))
        {
            EndRac1QueenScriptedCamera();
            return false;
        }
        if (cue is null || _sceneResult is null || _player is null ||
            !IsInstanceValid(_player)) return false;
        if (_rac1QueenScriptedCamera is null)
        {
            if (!IsInstanceValid(_player.Camera) || !_player.Camera.Current ||
                _player.Rac1RemoteControlActive ||
                GetViewport().GetCamera3D() != _player.Camera) return false;
            _rac1QueenScriptedCamera = new Camera3D
            {
                Name = "AlienQueenSourceCamera59",
                Fov = _player.Camera.Fov,
                Near = _player.Camera.Near,
                Far = _player.Camera.Far
            };
            _sceneResult.Root.AddChild(_rac1QueenScriptedCamera);
            _rac1QueenScriptedCamera.MakeCurrent();
            _activeCamera = _rac1QueenScriptedCamera;
            _player.Rac1RemoteControlActive = true;
        }
        var shot = _rac1QueenScriptedCamera;
        if (!IsInstanceValid(shot) || !shot.Current ||
            GetViewport().GetCamera3D() != shot) return false;
        Vector3 position = FromEffectNative(cue.NativeCameraPosition);
        Vector3 target = FromEffectNative(cue.NativeLookAt);
        if (position.DistanceSquaredTo(target) < .0001f) return false;
        shot.GlobalPosition = position;
        shot.LookAt(target, Vector3.Up);
        return shot.Current && GetViewport().GetCamera3D() == shot;
    }

    private void EndRac1QueenScriptedCamera()
    {
        var shot = _rac1QueenScriptedCamera;
        if (shot is null) return;
        bool owned = IsInstanceValid(shot) && GetViewport().GetCamera3D() == shot;
        if (_player is { } player && IsInstanceValid(player))
        {
            // Do not reclaim the viewport if another scripted camera has
            // taken it. Queen input suppression must still be released on
            // cancellation, even when the Queen no longer owns the viewport.
            if (owned && IsInstanceValid(player.Camera))
                player.Camera.MakeCurrent();
            // Visibomb owns its own remote-control lock if it interrupted us.
            bool guidedOwnsInput = _rac1MissileCamera is { } guided &&
                IsInstanceValid(guided) && GetViewport().GetCamera3D() == guided;
            if (!guidedOwnsInput) player.Rac1RemoteControlActive = false;
            if (owned) player.CenterCameraBehindPlayer();
        }
        if (_activeCamera == shot)
        {
            // The other viewport owner may also be temporary (Visibomb).
            // Keep the world-host pointer on Ratchet's stable camera, rather
            // than retaining a camera another system will later free.
            if (_player is { } remainingPlayer && IsInstanceValid(remainingPlayer) &&
                IsInstanceValid(remainingPlayer.Camera))
                _activeCamera = remainingPlayer.Camera;
            else
                _activeCamera = _camera;
        }
        if (IsInstanceValid(shot)) shot.QueueFree();
        _rac1QueenScriptedCamera = null;
    }

    /// <summary>
    /// Drive the retail state2 descent, 8->9->6->1 sequences and native
    /// camera blend. A camera takeover and applied cue must be acknowledged
    /// before this source helper can enter state7.
    /// </summary>
    private Rac1AlienQueenFlightFrame? TickRac1QueenFlight()
    {
        if (_rac1QueenEntrance?.NativeState != 2 ||
            _rac1QueenNode is null || _rac1QueenAnimation is null ||
            _rac1QueenRecovery is null) return null;
        if (_rac1QueenFlight is null)
        {
            _rac1QueenFlight = new(_rac1QueenAuthoredNativeZ);
            if (!_rac1QueenAnimation.SupportsSequence(8) ||
                !_rac1QueenAnimation.SupportsSequence(9) ||
                !_rac1QueenAnimation.SupportsSequence(6) ||
                !_rac1QueenAnimation.SupportsSequence(1))
                throw new InvalidDataException("Retail Alien Queen requires animation sequences 8,9,6,1.");
            _rac1QueenAnimation.Select(8, 1);
            _rac1QueenRecovery.AdmitNativeState(2, 8);
        }
        _rac1QueenAnimation.Tick();
        var frame = _rac1QueenFlight.StepDescent(_rac1QueenAnimation.Wrapped);
        if (frame.SequenceChanged)
        {
            _rac1QueenAnimation.Select(frame.Sequence, frame.Sequence is 9 ? 5 : 30);
            _rac1QueenRecovery.AdmitNativeState(2, frame.Sequence);
        }
        var current = ToEffectNative(_rac1QueenNode.Root.GlobalPosition);
        var worldPosition = new NVector3(current.X, current.Y, frame.NativeZ);
        _rac1QueenNode.Root.GlobalPosition = FromEffectNative(worldPosition);
        _rac1QueenNode.ApplyAnimationSample(_rac1QueenAnimation.Sample);
        var hero = _player is not null && IsInstanceValid(_player)
            ? ToEffectNative(_player.GlobalPosition) : worldPosition;
        var heroForward = _player is not null && IsInstanceValid(_player)
            ? ToEffectNative(_player.GlobalTransform.Basis.Z) : NVector3.UnitY;
        _rac1QueenPendingCamera = _rac1QueenFlight.SampleCamera(
            worldPosition, ToEffectNative(EnemyForward(_rac1QueenNode)),
            hero, heroForward, _rac1QueenCameraAnchor);
        bool cameraApplied = ApplyRac1QueenScriptedCamera(_rac1QueenPendingCamera);
        if (_rac1QueenFlight.CommitCameraAndComplete(cameraApplied))
        {
            EndRac1QueenScriptedCamera();
            _rac1QueenEntrance.ObserveLanding(true, true);
            _rac1QueenRecovery.AdmitNativeState(7, frame.Sequence);
            GD.Print("[frog-queen] retail camera blend acknowledged; native state2->7");
        }
        return frame;
    }

    private void TickRac1QueenRecovery()
    {
        if (_rac1QueenRecovery is null || _rac1QueenAnimation is null ||
            _rac1QueenNode is null) return;
        var phase = _rac1QueenRecovery.Step();
        if (phase.PreviousState != phase.NativeState &&
            phase.NativeState is Rac1AlienQueenRecovery.WaveRecoveryState or
                Rac1AlienQueenRecovery.FinalRecoveryState)
        {
            _rac1QueenAnimation.Select(Rac1AlienQueenRecovery.RecoveryAnimation, 5);
            GD.Print($"[frog-queen] native damage recovery -> Queen state{phase.NativeState}, retail seq6");
        }
        if (phase.NativeState != Rac1AlienQueenRecovery.WaveRecoveryState) return;
        _rac1QueenAnimation.Tick();
        bool wrapped = _rac1QueenAnimation.Wrapped &&
            _rac1QueenAnimation.Sequence == Rac1AlienQueenRecovery.RecoveryAnimation;
        if (wrapped)
        {
            ObserveRac1QueenFrogEvent(phase.NativeState, phase.Sequence, true);
            // Retail changes to sequence1 in state3 before its external
            // camera interpolator transitions it to state4. Do not invent
            // that camera completion or automatically enter state4.
            _rac1QueenRecovery.AdmitNativeState(3, 1);
            _rac1QueenAnimation.Select(1, 20);
        }
        _rac1QueenNode.ApplyAnimationSample(_rac1QueenAnimation.Sample);
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
