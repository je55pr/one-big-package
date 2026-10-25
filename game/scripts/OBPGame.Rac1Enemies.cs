using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OneBigPackage;

public partial class OBPGame
{
    private readonly Dictionary<int, Rac1HostedEnemy> _rac1Enemies = [];
    private Rac1Class1440Session? _rac1Class1440;
    private Rac1NovalisPeckbotSession? _rac1Peckbots;
    private Rac1NovalisBirdbotSession? _rac1Birdbots;
    private Rac1SeekerSession? _rac1Seekers;
    private double _rac1EnemyTicks;
    // LEVEL2:0x15f5cc frame-low-bits emission gate. Host 60-Hz counter;
    // the exact retail global update phase is not yet proven.
    private uint _rac1AridiaNativeFrameCounter;
    private const float AridiaHostUpdateRadius = 24f;
    private const float AridiaAnimatedPresentationRadius = 18f;
    private IEnumerable<RuntimeWorldScene.DynamicObjectNode> Rac1EnemyNodes => _rac1Enemies.Values
        .Where(enemy => !enemy.Buried).Select(enemy => enemy.Node);
    // Novalis host sleep only; Aridia maintains its own buried and animation policies.
    private const float Rac1NovalisEnemySleepDistance = 42f;

    private sealed class Rac1HostedEnemy(RuntimeWorldScene.DynamicObjectNode node, Rac1MobyAnimationAssets.Asset asset,
        CharacterBody3D body, Rac1EnemyMotion? motion, int sequence)
    {
        public RuntimeWorldScene.DynamicObjectNode Node { get; } = node;
        // Capture the imported placement before any launches, contacts or
        // deaths can move Root. Native Aridia nest relaunch heading uses
        // the same shark's authored home across multiple recycle cycles.
        public Vector3 AuthoredHomePosition { get; } = node.Root.GlobalPosition;
        public CharacterBody3D Body { get; } = body;
        public Rac1EnemyMotion? Motion { get; } = motion;
        public Rac1MobySequenceClock Animation { get; } = new(asset, sequence);
        public bool Grounded { get; set; } = true;
        public bool NeedsGroundPlacement { get; set; } = node.Source.NativeClassId is 749 or Rac1AridiaEnemies.Sandshark;
        public bool Buried { get; set; } = node.Source.NativeClassId == Rac1AridiaEnemies.Sandshark;
        public int DeathTicks { get; set; }
        public int GroundPlacementAttempts { get; set; }
        public bool RewardEmitted { get; set; }
        public bool DeathPresented { get; set; }
        public GpuParticles3D? Flame { get; set; }
        public Rac1MobyReaction? Reaction { get; set; }
    }

    private void ResetRac1Enemies()
    {
        EndRac1QueenScriptedCamera();
        ResetRac1FrogCarriers();
        ResetRac1QueenLinkedChildren();
        foreach (var enemy in _rac1Enemies.Values)
        {
            if (IsInstanceValid(enemy.Body)) enemy.Body.QueueFree();
            if (enemy.Flame is { } flame && IsInstanceValid(flame)) flame.QueueFree();
        }
        _rac1Enemies.Clear(); _rac1Class1440 = null;
        _rac1Peckbots = null; _rac1Birdbots = null; _rac1Seekers = null; _rac1Frogs = null; _rac1FrogShuttle = null;
        _rac1FrogWaves = null; _rac1FrogQueen = null; _rac1FrogWaveNodes.Clear();
        _rac1QueenRecovery = null; _rac1QueenEntrance = null;
        _rac1QueenFlight = null; _rac1QueenPendingCamera = null;
        _rac1QueenAnimation = null; _rac1QueenNode = null;
        _rac1QueenLinkedGate = null; _rac1QueenGateNative = null;
        _rac1QueenLinkedNodes.Clear(); _rac1QueenLinkedBaseRotations.Clear();
        _rac1AridiaEnemies = new(); _rac1EnemyTicks = 0;
        _rac1AridiaNativeFrameCounter = 0;
        ResetRac1AridiaScriptActors();
        ResetRac1ConstructorbotStartups();
        ResetRac1Amoeboids();
        ResetRac1BirdbotProjectiles();
    }

    private void ConfigureRac1Enemies(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        if (world.LevelId is not (0 or 1 or Rac1AridiaEnemies.LevelId or 5 or 6 or 9 or 10 or 11 or 15)) return;
        var source = _sources.Get(ObpSourceGame.Rac1) ?? throw new InvalidOperationException("Enemy assets require the active R&C1 source.");
        using var reader = new FileRandomAccessReader(source.Path);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == world.LevelId);
        var core = Rac1LevelCore.Open(reader, level);
        var classes = Rac1StaticClasses.Read(core);
        var nodes = _rac1HostileNodes.Values.ToList();
        if (world.LevelId == 0)
        {
            _rac1Class1440 = new(_rac1MobyRuntime, Rac1Paths.Read(Rac1LevelSettings.ReadGameplay(reader, level)), Rac1EffectRandom);
            foreach (var authored in (world.DynamicObjects ?? []).Where(item => item.NativeClassId == Rac1Class1440Session.ClassId))
            {
                var node = FindPresentedDynamic(result, authored) ?? throw new InvalidDataException("Veldin enemy has no rendered model.");
                var registered = _rac1Class1440.Register(authored, node.State);
                node.ApplyState(registered.Instance.EntityState);
                nodes.Add(node);
            }
        }
        else if (world.LevelId == Rac1AridiaEnemies.LevelId)
        {
            // The native LEVEL2 nest timer lives in the gameplay PVar, not in
            // the general placement's 0x78-byte instance record.
            var levelGameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            var authoredPlacements = Rac1Instances.Parse(levelGameplay).MobyInstances;
            _rac1AridiaClipVolumes = Rac1ClipVolumes.Read(levelGameplay);
            _rac1AridiaEnemies.SetAuthoredGroupRoster(Rac1AridiaEnemies.ReadAuthoredGroupRoster(levelGameplay));
            foreach (var authored in (world.DynamicObjects ?? []).Where(item => Rac1AridiaEnemies.IsEnemyClass(item.NativeClassId)))
            {
                var node = FindPresentedDynamic(result, authored) ?? CreateRac1InvisibleRuntimeNode(result, authored);
                byte[] pvar = authoredPlacements[authored.InstanceIndex].PVar
                    ?? throw new InvalidDataException("Aridia enemy placement is missing its native PVar.");
                int? linkedActivationMobyIndex = authored.NativeClassId == Rac1AridiaEnemies.Sandshark
                    ? Rac1AridiaEnemies.ReadSharkLinkedActivationInstance(pvar) : null;
                // Native class-612 initialization branches by PVar +0x194;
                // the 15 retail bots span four distinct starting states.
                // Other source-gated native states retain existing semantics.
                Rac1AridiaConstructorbotNative.Frame? botStartup =
                    authored.NativeClassId == Rac1AridiaEnemies.Constructorbot
                        ? Rac1AridiaConstructorbotNative.Initialize(pvar) : null;
                if (botStartup.HasValue)
                {
                    _rac1ConstructorbotStartups.Add(authored.InstanceIndex, botStartup.Value);
                    int nativeWaypoint = Rac1AridiaConstructorbotNative.ReadWaypointRecordIndex(
                        pvar, _rac1AridiaClipVolumes.Count);
                    _rac1ConstructorbotNativeWaypoints.Add(authored.InstanceIndex,
                        FromEffectNative(_rac1AridiaClipVolumes[nativeWaypoint].Centre));
                }
                // The Sandshark init routine's +0x258 branch enters state29.
                var runtime = _rac1MobyRuntime.Register(authored, node.State,
                    nativeState: botStartup?.NativeState ?? (linkedActivationMobyIndex.HasValue ? 29 : 1),
                    pvar: pvar);
                node.ApplyState(runtime.EntityState);
                Vector3 forward = EnemyForward(node);
                NVector3 native = ToEffectNative(forward);
                int nestWait = authored.NativeClassId == Rac1AridiaEnemies.Nest
                    ? Rac1AridiaEnemies.ReadNestInitialWaitTicks(pvar)
                    : 60;
                _rac1AridiaEnemies.Register(authored.InstanceIndex, authored.NativeClassId,
                    MathF.Atan2(native.Y, native.X), nestWait,
                    linkedActivationMobyIndex: linkedActivationMobyIndex,
                    groupId: Rac1AridiaEnemies.ReadAuthoredGroup(authoredPlacements[authored.InstanceIndex].RawRecord),
                    constructorbotStartupSequence: botStartup?.Sequence ?? 1);
                nodes.Add(node);
            }
            GD.Print("[rac1-aridia] Constructorbot native startup " +
                string.Join(", ", _rac1ConstructorbotStartups.Values
                    .GroupBy(start => start.NativeState).OrderBy(group => group.Key)
                    .Select(group => $"state{group.Key}={group.Count()}")));
            ConfigureRac1AridiaScriptActors(world, result, authoredPlacements);
        }
        else
        {
            _rac1Peckbots = new(_rac1MobyRuntime, Rac1EffectRandom);
            _rac1Birdbots = new(_rac1MobyRuntime);
            if (world.LevelId is 9 or 15)
                _rac1Seekers = new(_rac1MobyRuntime);
        }
        if (world.LevelId is 6 or 10)
        {
            var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            ConfigureRac1Frogs(world, result, Rac1Paths.Read(gameplay),
                Rac1ClipVolumes.Read(gameplay), nodes);
        }
        if (_rac1Peckbots is { } peckbots)
            foreach (var authored in (world.DynamicObjects ?? []).Where(item => item.NativeClassId == Rac1NovalisPeckbotSession.ClassId))
            {
                var node = FindPresentedDynamic(result, authored)
                    ?? throw new InvalidDataException("Novalis Peckbot has no original visual.");
                var registered = peckbots.Register(authored, node.State);
                node.ApplyState(registered.Instance.EntityState);
                nodes.Add(node);
            }
        if (_rac1Birdbots is { } birdbots)
            foreach (var authored in (world.DynamicObjects ?? []).Where(item => item.NativeClassId == Rac1NovalisBirdbotSession.ClassId))
            {
                var node = FindPresentedDynamic(result, authored)
                    ?? throw new InvalidDataException("Novalis Birdbot has no original visual.");
                var registered = birdbots.Register(authored, node.State);
                node.ApplyState(registered.Instance.EntityState);
                nodes.Add(node);
            }
        if (_rac1Seekers is { } seekers)
            foreach (var authored in (world.DynamicObjects ?? [])
                .Where(item => item.NativeClassId == Rac1SeekerSession.ClassId))
            {
                var node = FindPresentedDynamic(result, authored)
                    ?? throw new InvalidDataException("Original Seeker model is unavailable.");
                NVector3 nativeForward = ToEffectNative(EnemyForward(node));
                seekers.Register(authored, node.State, MathF.Atan2(nativeForward.Y, nativeForward.X));
                nodes.Add(node);
            }
        ConfigureRac1Amoeboids(world, result, nodes, Rac1LevelSettings.ReadGameplay(reader, level));
        foreach (var group in nodes.GroupBy(node => node.Source.NativeClassId))
        {
            var cls = classes.Mobies[group.Key];
            var asset = Rac1MobyAnimationAssets.Bake(cls, group.First().Source.Meshes,
                core.Assets.AsSpan(cls.AssetOffset, cls.AssetLength),
                skipUnsupportedTiming: world.LevelId == Rac1AridiaEnemies.LevelId ||
                    group.Key == Rac1SeekerSession.ClassId || Rac1AmoeboidAuthored.IsFamily(group.Key) ||
                    group.Key == Rac1FrogAuthored.ClassId);
            foreach (var node in group)
            {
                if (group.Key == 749) node.Root.Scale *= Rac1EnemyMotion.Class749Scale;
                // Host contact adapter: upright capsules, independent of skeletal presentation.
                // The retail common query solver and per-part collision primitives are not reconstructed.
                float radius = group.Key == 749 ? .5f : group.Key == Rac1AridiaEnemies.Nest ? 1.1f : group.Key is Rac1AridiaEnemies.Sandshark or Rac1FrogAuthored.ClassId ? .35f : .45f;
                float height = group.Key == 749 ? 1 : group.Key is Rac1AridiaEnemies.Sandshark or Rac1FrogAuthored.ClassId ? .7f : group.Key == Rac1NovalisPeckbotSession.ClassId ? 1.3f : 1.7f;
                // A buried Sandshark is not a solid collider in the native
                // encounter. Register its shape but do not put it into broadphase
                // contacts until it has actually emerged or been released.
                uint initialContactMask = group.Key == Rac1AridiaEnemies.Sandshark ||
                    (Rac1AmoeboidAuthored.IsFamily(group.Key) && !_rac1Amoeboids!.Get(node.Source.InstanceIndex).Active) ||
                    (group.Key == Rac1FrogAuthored.ClassId && !_rac1Frogs!.Get(node.Source.InstanceIndex).Presented)
                    ? 0u : 1u;
                var body = new CharacterBody3D
                {
                    Name = $"EnemyContact_i{node.Source.InstanceIndex}",
                    CollisionLayer = initialContactMask,
                    CollisionMask = initialContactMask
                };
                body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = radius, Height = height }, Position = Vector3.Up * (height / 2) });
                result.Root.AddChild(body);
                body.GlobalPosition = node.Root.GlobalPosition;
                Vector3 forward = EnemyForward(node);
                NVector3 nativeForward = ToEffectNative(forward);
                int clip = group.Key == Rac1NovalisPeckbotSession.ClassId
                    ? _rac1Peckbots!.Get(node.Source.InstanceIndex).Sequence
                    : group.Key == Rac1NovalisBirdbotSession.ClassId
                        ? _rac1Birdbots!.Get(node.Source.InstanceIndex).Sequence
                        : group.Key == Rac1SeekerSession.ClassId
                            ? _rac1Seekers!.Get(node.Source.InstanceIndex).Sequence
                        : Rac1AmoeboidAuthored.IsFamily(group.Key)
                            ? _rac1Amoeboids!.Get(node.Source.InstanceIndex).Sequence
                        : group.Key == Rac1FrogAuthored.ClassId
                            ? _rac1Frogs!.Get(node.Source.InstanceIndex).Sequence
                        : group.Key == Rac1AridiaEnemies.Constructorbot &&
                            _rac1ConstructorbotStartups.TryGetValue(node.Source.InstanceIndex, out var startup) &&
                            asset.Clips.ContainsKey(startup.Sequence)
                            ? startup.Sequence
                        : asset.Clips.ContainsKey(1) ? 1 : asset.Clips.Keys.Min();
                var motion = group.Key is 749 or 1440 or Rac1NovalisPeckbotSession.ClassId or Rac1NovalisBirdbotSession.ClassId
                    ? new Rac1EnemyMotion(group.Key, MathF.Atan2(nativeForward.Y, nativeForward.X))
                    : null;
                var enemy = new Rac1HostedEnemy(node, asset, body, motion, clip);
                node.ConfigureAnimations(asset.Presentation);
                if (group.Key != Rac1AridiaEnemies.Sandshark &&
                    (!Rac1AmoeboidAuthored.IsFamily(group.Key) ||
                     _rac1Amoeboids!.Get(node.Source.InstanceIndex).Active))
                    node.ApplyAnimationSample(enemy.Animation.Sample);
                else if (group.Key == Rac1AridiaEnemies.Sandshark)
                    HideBuriedRac1AridiaEnemy(enemy);
                SynchronizeRac1Enemy(enemy);
                _rac1Enemies.Add(node.Source.InstanceIndex, enemy);
                if (Rac1AmoeboidAuthored.IsFamily(group.Key))
                    InitializeRac1AmoeboidPresentation(enemy);
            }
        }
        ConfigureRac1FrogCarriers(result);
        if (world.LevelId == 6)
        {
            var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            var triggerClass = classes.Mobies[Rac1FrogWaveTriggers.ClassId];
            var groups = Rac1MobyIndexGroups.Read(gameplay, world.DynamicObjects?.Count ?? 0);
            var queenClass = classes.Mobies[Rac1FrogQueenGroup.QueenClassId];
            var overlay = Rac1ExecutableOverlay.Read(reader, level);
            var cameraAnchor = overlay.Read(0x167540, 12);
            ConfigureRac1FrogQueen(world, result, groups, queenClass,
                core.Assets.AsSpan(queenClass.AssetOffset, queenClass.AssetLength),
                Rac1AlienQueenGateNative.Read(overlay), new NVector3(
                    System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(cameraAnchor),
                    System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(cameraAnchor.Slice(4)),
                    System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(cameraAnchor.Slice(8))));
            ConfigureRac1FrogWaves(world, result,
                groups, Rac1ClipVolumes.Read(gameplay), triggerClass,
                core.Assets.AsSpan(triggerClass.AssetOffset, triggerClass.AssetLength));
        }
        GD.Print($"[rac1-enemies] {_rac1Enemies.Count} animated/colliding retail placements; " +
            $"{_rac1Class1440?.RegisteredCount ?? 0} class1440, {_rac1Peckbots?.RegisteredCount ?? 0} Novalis Peckbots, " +
            $"{_rac1Birdbots?.RegisteredCount ?? 0} Novalis Birdbots, " +
            $"{_rac1Seekers?.RegisteredCount ?? 0} Seekers, " +
            $"{_rac1Frogs?.RegisteredCount ?? 0} Frogs ({_rac1Frogs?.PresentedCount ?? 0} admitted)");
    }

    private static Vector3 EnemyForward(RuntimeWorldScene.DynamicObjectNode node) =>
        (node.Root.ToGlobal(FromEffectNative(NVector3.UnitX)) - node.Root.GlobalPosition).Normalized();

    private void TickRac1Hostiles(double delta)
    {
        if (_player is null) return;
        _rac1EnemyTicks += Math.Max(0, delta) * Rac1NativeTicksPerSecond;
        while (_rac1EnemyTicks >= 1)
        {
            _rac1EnemyTicks--;
            _rac1AridiaNativeFrameCounter++;
            if (_playerNative is null) _rac1Nanotech.UpdateNative();
            TickRac1AmoeboidGates();
            TickRac1FrogShuttleSwitch();
            TickRac1FrogWaves();
            TickRac1QueenFlight();
            TickRac1QueenRecovery();
            TickRac1QueenLinkedChildren();
            foreach (var enemy in _rac1Enemies.Values.OrderBy(enemy => enemy.Node.Source.InstanceIndex))
            {
                var node = enemy.Node;
                if (!IsInstanceValid(node.Root)) continue;
                if (node.Source.NativeClassId == Rac1FrogSession.ClassId &&
                    _rac1FrogCarrierChildren.ContainsKey(node.Source.InstanceIndex))
                    TickRac1FrogCarrier(enemy);
                if (!IsRac1GameplayActive(node)) continue;
                bool aridia = Rac1AridiaEnemies.IsEnemyClass(node.Source.NativeClassId);
                // Aridia has 137 authored enemies; most are dormant, buried or
                // beyond Ratchet's encounter range. The legacy loop uploaded
                // animated mesh vertices for *all of them* 60 times/sec.
                // Freeze their animations and host physics when out of range.
                // Original meshes stay visible for stationary bots and nests.
                if (aridia && node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) >
                    AridiaHostUpdateRadius * AridiaHostUpdateRadius)
                {
                    if (enemy.Buried) HideBuriedRac1AridiaEnemy(enemy);
                    continue;
                }
                // Distant Novalis actors retain their meshes, authored locations
                // and animation phase, but need no contact probes, sounds or AI
                // until Ratchet enters their gameplay neighbourhood.
                if (node.Source.NativeClassId == Rac1FrogAuthored.ClassId &&
                    node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) >
                        FrogHostSleepRadius * FrogHostSleepRadius) continue;
                if (node.Source.NativeClassId == Rac1SeekerSession.ClassId &&
                    _rac1Seekers!.Get(node.Source.InstanceIndex).NativeState is not
                        (Rac1SeekerSession.HurtState or Rac1SeekerSession.DeathState or Rac1SeekerSession.ExplosionState) &&
                    node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) >
                        Rac1NovalisEnemySleepDistance * Rac1NovalisEnemySleepDistance)
                    continue;
                if (_world is { Game: "rac1", LevelId: 1 } &&
                    node.Source.NativeClassId is Rac1NovalisPeckbotSession.ClassId or Rac1NovalisBirdbotSession.ClassId &&
                    node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) >
                        Rac1NovalisEnemySleepDistance * Rac1NovalisEnemySleepDistance)
                    continue;
                if (_rac1Chickens.ContainsKey(node.Source.InstanceIndex)) continue;
                // Staged diagnostics may move an authored node; ordinary movement is synchronized below.
                if (enemy.Body.GlobalPosition.DistanceSquaredTo(node.Root.GlobalPosition) > 1e-5f)
                    enemy.Body.GlobalPosition = node.Root.GlobalPosition;
                if (enemy.NeedsGroundPlacement && !enemy.Buried)
                {
                    // Authored creature positions can lie inside the terrain.
                    // Wait for registered collision, then place the full capsule
                    // from above before idle or pursuit can start beneath it.
                    enemy.Grounded = TryGroundRac1Enemy(enemy, upwardReach: 1, downwardReach: 1);
                    if (!enemy.Grounded && ++enemy.GroundPlacementAttempts < 20) continue;
                    enemy.NeedsGroundPlacement = false;
                    node.Root.GlobalPosition = enemy.Body.GlobalPosition;
                }
                // Buried Sandsharks need proximity detection only, not per-tick
                // vertex interpolation or vertex-buffer uploads.
                bool aridiaStepped = false;
                if (aridia && enemy.Buried)
                {
                    TickRac1AridiaEnemy(enemy);
                    if (enemy.Buried) continue;
                    aridiaStepped = true;
                }
                foreach (int sound in enemy.Animation.Tick())
                    if (node.Root.GlobalPosition.DistanceTo(_player.GlobalPosition) < 40 && !PlayRegisteredRac1ActorSound(node, sound))
                        PlayRac1PickupSound(node.Source.NativeClassId, sound, node.Root.GlobalPosition);
                if (node.Source.NativeClassId == 749) TickRac1Class749(enemy);
                else if (node.Source.NativeClassId == 1440) TickRac1Class1440(enemy);
                else if (node.Source.NativeClassId == Rac1NovalisPeckbotSession.ClassId) TickRac1NovalisPeckbot(enemy);
                else if (node.Source.NativeClassId == Rac1NovalisBirdbotSession.ClassId) TickRac1NovalisBirdbot(enemy);
                else if (node.Source.NativeClassId == Rac1SeekerSession.ClassId) TickRac1Seeker(enemy);
                else if (Rac1AmoeboidAuthored.IsFamily(node.Source.NativeClassId)) TickRac1Amoeboid(enemy);
                else if (node.Source.NativeClassId == Rac1FrogAuthored.ClassId) TickRac1Frog(enemy);
                else if (!aridiaStepped) TickRac1AridiaEnemy(enemy);
                // All known Aridia attacks occur within the native-candidate
                // host encounter region. Preserve gameplay ticks at 24 units,
                // but avoid CPU skinning/GPU buffer uploads beyond 18 units.
                if (!aridia || node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) <=
                    AridiaAnimatedPresentationRadius * AridiaAnimatedPresentationRadius)
                    node.ApplyAnimationSample(enemy.Animation.Sample);
                SynchronizeRac1Enemy(enemy);
                if (Rac1AridiaEnemies.IsEnemyClass(node.Source.NativeClassId))
                    RestoreRac1AridiaEnemyVisibility(enemy);
                if (!IsRac1GameplayActive(node)) PresentRac1EnemyDeath(enemy);
                if (_rac1Nanotech.Probe().IsDead) return;
            }
        }
        TickRac1BirdbotProjectiles(delta);
    }

    private void TickRac1Class749(Rac1HostedEnemy enemy)
    {
        var node = enemy.Node;
        var before = _rac1Hostiles.Probe(node.Source);
        Vector3 position = node.Root.GlobalPosition, target = ArsenalEnemyGoal(enemy, _player!.GlobalPosition);
        Rac1GameplayEntityRef identity = Rac1GameplayEntityRef.Player;
        bool linkedTerminal = false;
        if (before.NativeState == Rac1Class749Hostile.LinkedObjectNativeState && before.LinkedTargetInstanceIndex is int linked)
        {
            _rac1LinkedTargetNodes.TryGetValue(linked, out var linkedNode);
            linkedTerminal = linkedNode is null || !IsInstanceValid(linkedNode.Root) || !IsRac1GameplayActive(linkedNode);
            if (!linkedTerminal) { target = linkedNode!.Root.GlobalPosition; identity = Rac1GameplayEntityRef.Moby(new(linkedNode.Source.NativeClassId, linked)); }
        }
        Vector3 difference = target - position;
        Vector3 planar = new(difference.X, 0, difference.Z);
        float facing = planar.LengthSquared() == 0 ? 0 : Math.Abs(EnemyForward(node).SignedAngleTo(planar.Normalized(), Vector3.Up));
        var next = _rac1Hostiles.Step(node.Source, new(difference.Length(), facing,
            new(-position.X, position.Y, position.Z), TargetDescriptor: new(identity, new(-target.X, target.Y, target.Z)),
            LinkedObjectTerminal: linkedTerminal, AnimationPosition: enemy.Animation.Position,
            AnimationFinished: enemy.Animation.Wrapped, AnimationBlending: enemy.Animation.Blending, Grounded: enemy.Grounded));
        // Native state5 selects locomotion4 for 0x261630's roaming motion.
        // This host does not implement that routine yet. Its stationary stand-in
        // uses the retail rest clip1 rather than bouncing without displacement.
        int sequence = next.NativeState == Rac1Class749Hostile.TargetSearchNativeState
            ? Rac1Class749Hostile.RestSequenceId : next.NativeSequence ?? enemy.Animation.Sequence;
        SelectRac1EnemySequence(enemy, sequence, next.NativeState == 7 ? 7 : 10);
        if (next.ReactionActive) { StepRac1EnemyReaction(enemy); return; }
        enemy.Reaction = null;
        var intent = next.HostIntents.OfType<Rac1Class749NavigationIntent>().FirstOrDefault();
        NVector3? destination = intent?.Destination is { } point ? new((float)point.X, (float)point.Z, (float)point.Y) : null;
        if (destination.HasValue)
            MoveRac1Enemy(enemy, destination.Value, destination, next.NavigationYawOffset);
        else if (next.NativeState == Rac1Class749Hostile.LinkedObjectNativeState && !linkedTerminal)
            MoveRac1Enemy(enemy, ToEffectNative(target), null);
        if (next.Attack is { } attack && !ArsenalEnemyHitsDecoy(enemy) && EnemyMeleeTouchesPlayer(enemy))
            ApplyRac1EnemyPlayerDamage(Rac1DamageRuntime.FromClass749Attack(new(749, node.Source.InstanceIndex), attack));
    }

    private void TickRac1Class1440(Rac1HostedEnemy enemy)
    {
        int instance = enemy.Node.Source.InstanceIndex;
        var before = _rac1Class1440!.Get(instance);
        NVector3 position = ToEffectNative(enemy.Node.Root.GlobalPosition), target = ToEffectNative(ArsenalEnemyGoal(enemy, _player!.GlobalPosition));
        Rac1GameplayEntityRef? contact = before.NativeState == 5 ? EnemySweepContact(enemy) : null;
        if (before.NativeState == 5 && _rac1Decoys.Any(decoy => decoy.Grounded &&
            decoy.View.GlobalPosition.DistanceTo(enemy.Body.GlobalPosition) < 1.8f)) contact = Rac1GameplayEntityRef.Player;
        Rac1Class1440Session.Target? linked = before.LinkedTargetInstanceIndex is int link &&
            _rac1Enemies.TryGetValue(link, out var linkedEnemy) && IsRac1GameplayActive(linkedEnemy.Node)
            ? new(new(linkedEnemy.Node.Source.NativeClassId, link), ToEffectNative(linkedEnemy.Node.Root.GlobalPosition)) : null;
        var next = _rac1Class1440.Step(instance, new(position, target, enemy.Animation.Wrapped, enemy.Animation.Blending,
            enemy.Animation.Position, contact == Rac1GameplayEntityRef.Player, enemy.Grounded, !_rac1Nanotech.Probe().IsDead,
            enemy.Motion!.StepLength, linked, contact));
        SelectRac1EnemySequence(enemy, next.Sequence, next.NativeState is 7 or 8 ? 1 : 10);
        if (next.ReactionActive) StepRac1EnemyReaction(enemy);
        else
        {
            enemy.Reaction = null;
            if (next.TurnsToTarget) MoveRac1Enemy(enemy, next.Destination ?? next.LookAt, next.Destination, next.TurnOffset, next.NativeState == 5,
                next.Destination.HasValue ? next.DistanceToPathEnd : null, directPosition: next.Destination.HasValue);
        }
        SetRac1EnemyFlame(enemy, next.NativeState == 5);
        if (next.DamageTarget is { } victim)
        {
            var damage = Rac1DamageRuntime.FromClass1440Contact(new(1440, instance), victim);
            if (victim == Rac1GameplayEntityRef.Player && !ArsenalEnemyHitsDecoy(enemy)) ApplyRac1EnemyPlayerDamage(damage);
            else if (_rac1Enemies.TryGetValue((int)victim.RuntimeId, out var targetEnemy) && ApplyRac1EnemyWeaponDamage(targetEnemy.Node, damage))
                _rac1DamageTransport.Publish(damage);
        }
    }

    private static void SelectRac1EnemySequence(Rac1HostedEnemy enemy, int sequence, int blendTicks)
    {
        if (enemy.Animation.Sequence != sequence) enemy.Animation.Select(sequence, blendTicks);
    }

    private void MoveRac1Enemy(Rac1HostedEnemy enemy, NVector3 look, NVector3? destination,
        float yawOffset = 0, bool sweeping = false, float? pathDistance = null, bool directPosition = false)
    {
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
        else MoveRac1EnemyContact(enemy, FromEffectNative(motion));
    }

    private void StepRac1EnemyReaction(Rac1HostedEnemy enemy)
    {
        if (enemy.Reaction is not { } reaction) return;
        enemy.Node.Root.Rotation = new(0, reaction.FacingYaw, 0);
        MoveRac1EnemyContact(enemy, FromEffectNative(reaction.Step()));
    }

    private void MoveRac1EnemyContact(Rac1HostedEnemy enemy, Vector3 remaining)
    {
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

    private bool TryGroundRac1Enemy(Rac1HostedEnemy enemy, float upwardReach, float downwardReach)
    {
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

    private void SynchronizeRac1Enemy(Rac1HostedEnemy enemy)
    {
        var instance = _rac1MobyRuntime.Require(enemy.Node.Source);
        _rac1MobyRuntime.SetTransform(instance, RuntimeWorldScene.ToRuntimeTransform(enemy.Node.Root.Transform));
        enemy.Node.ApplyState(instance.EntityState);
    }

    private Vector3 EnemySocket(Rac1HostedEnemy enemy) => enemy.Node.Root.ToGlobal(FromEffectNative(enemy.Animation.ContactSocket));

    private bool EnemyMeleeTouchesPlayer(Rac1HostedEnemy enemy)
    {
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

    private Rac1GameplayEntityRef? EnemySweepContact(Rac1HostedEnemy enemy)
    {
        Vector3 socket = EnemySocket(enemy);
        return EnemyRayContact(enemy, socket, enemy.Node.Root.GlobalPosition + Vector3.Up * .5f) ??
            EnemyRayContact(enemy, socket, socket + EnemyForward(enemy.Node) * Rac1Class1440Session.ContactLength);
    }

    private bool EnemyRayTouchesPlayer(Rac1HostedEnemy enemy, Vector3 start, Vector3 end)
        => EnemyRayContact(enemy, start, end) == Rac1GameplayEntityRef.Player;

    private Rac1GameplayEntityRef? EnemyRayContact(Rac1HostedEnemy enemy, Vector3 start, Vector3 end)
    {
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

    private void ApplyRac1EnemyPlayerDamage(Rac1GameplayDamageEvent damage)
    {
        if (!PlayerAlive) return;
        var before = _rac1Nanotech.Probe();
        _rac1DamageTransport.Publish(damage);
        if (_playerNative is not null)
        {
            // Validate the recovered descriptor in its source module before
            // translating its admitted integer amount to the selected victim.
            _ = new Rac1RatchetNanotechSession().ApplyDamage(damage);
            var attacker = (_sceneResult?.DynamicObjectNodes ?? []).Concat(Rac1EnemyNodes).Concat(_rac1CrateNodes).Concat(_rac1HostileNodes.Values)
                .FirstOrDefault(node => node.Source.SourceGame == "rac1" &&
                node.Source.NativeClassId == damage.Source.NativeClassId && node.Source.InstanceIndex == damage.Source.RuntimeId);
            if (attacker is null) { GD.PushWarning("Player damage source has no registered native actor."); return; }
            ApplyPlayerSourceDamage(attacker, OBP.RAC1.Rac1Authority.Primary.BuildId,
                PlayerSessionNativeState.Rac1DamageAdapter, PlayerSessionNativeState.Rac1DamageDescriptor,
                _rac1AridiaNativeFrameCounter, checked((int)damage.NativeDamage), damage.NativeDamageFlags ?? 0);
            return;
        }
        var after = _rac1Nanotech.ApplyDamage(damage);
        _player!.Rac1GameplayState = after;
        if (after.Nanotech == before.Nanotech) return;
        NotifyRac1PlayerDamageAudio(before, after);
        _rac1CombatStatus = $"enemy hit: Nanotech {after.Nanotech}/{after.RespawnNanotech}";
        RefreshRac1HudState(Rac1HudProjection.DamageFeedback(before, after));
        GD.Print($"[rac1-enemies] class{damage.Source.NativeClassId} i{damage.Source.RuntimeId}: contact; Nanotech {after.Nanotech}");
    }

    private bool ApplyRac1EnemyWeaponDamage(RuntimeWorldScene.DynamicObjectNode node, Rac1GameplayDamageEvent damage)
    {
        if (Rac1AmoeboidAuthored.IsFamily(node.Source.NativeClassId))
            return ApplyRac1AmoeboidDamage(node, damage);
        if (Rac1AridiaEnemies.IsEnemyClass(node.Source.NativeClassId))
            return ApplyRac1AridiaWeaponDamage(node, damage);
        if (!_rac1MobyRuntime.CanDispatchDamage(damage)) return false;
        var enemy = _rac1Enemies[node.Source.InstanceIndex];
        bool lethal;
        int sequence;
        if (node.Source.NativeClassId == 749)
        {
            var result = _rac1MobyRuntime.DispatchDamage<Rac1Class749HostProbe>(damage);
            lethal = result.Health <= 0; sequence = result.NativeSequence!.Value;
        }
        else if (node.Source.NativeClassId == Rac1NovalisBirdbotSession.ClassId)
        {
            var (probe, dead) = _rac1MobyRuntime.DispatchDamage<(Rac1NovalisBirdbotSession.Probe Probe, bool Lethal)>(damage);
            lethal = dead; sequence = probe.Sequence;
        }
        else if (node.Source.NativeClassId == Rac1SeekerSession.ClassId)
        {
            var result = _rac1MobyRuntime.DispatchDamage<Rac1SeekerDamageResult>(damage);
            lethal = result.Lethal; sequence = result.Frame.Sequence;
        }
        else if (node.Source.NativeClassId == Rac1FrogAuthored.ClassId)
        {
            var result = _rac1MobyRuntime.DispatchDamage<Rac1FrogDamageResult>(damage);
            lethal = result.Lethal; sequence = result.Frame.Sequence;
            if (!result.Frame.Presented)
            {
                node.ApplyState(_rac1MobyRuntime.Require(node.Source).EntityState);
                enemy.Body.CollisionLayer = enemy.Body.CollisionMask = 0;
            }
        }
        else if (node.Source.NativeClassId == Rac1NovalisPeckbotSession.ClassId)
        {
            var result = _rac1MobyRuntime.DispatchDamage<Rac1NovalisPeckbotSession.DamageResult>(damage);
            lethal = result.Lethal; sequence = result.Probe.Sequence;
        }
        else
        {
            var result = _rac1MobyRuntime.DispatchDamage<Rac1Class1440Session.DamageResult>(damage);
            lethal = result.Lethal; sequence = result.Probe.Sequence;
        }
        if (node.Source.NativeClassId != Rac1SeekerSession.ClassId || enemy.Animation.Sequence != sequence)
            enemy.Animation.Select(sequence, 1);
        Vector3 attackerPosition = _player!.GlobalPosition;
        if (damage.Source is { Kind: Rac1GameplayEntityKind.Projectile, NativeClassId: 121 } &&
            _rac1Projectiles.TryGetValue(damage.Source.RuntimeId, out var projectile))
            attackerPosition = projectile.Node.GlobalPosition;
        else if (damage.Source is { Kind: Rac1GameplayEntityKind.Projectile, NativeClassId: 74 } &&
            _rac1EquipmentShots.FirstOrDefault(shot => shot.Weapon == Rac1WeaponId.MineGlove && shot.Id == damage.Source.RuntimeId) is { } mine)
            attackerPosition = mine.Position;
        else if (damage.Source is { Kind: Rac1GameplayEntityKind.Projectile, NativeClassId: 186 } &&
            _rac1DoomAgents.FirstOrDefault(bot => bot.Id == damage.Source.RuntimeId) is { } bot)
            attackerPosition = bot.Body.GlobalPosition;
        else if (damage.Source.Kind == Rac1GameplayEntityKind.Moby && _rac1Enemies.TryGetValue((int)damage.Source.RuntimeId, out var attacker))
            attackerPosition = attacker.Node.Root.GlobalPosition;
        NVector3 direction = ToEffectNative(node.Root.GlobalPosition - attackerPosition);
        enemy.Reaction = node.Source.NativeClassId is Rac1NovalisPeckbotSession.ClassId or Rac1NovalisBirdbotSession.ClassId or Rac1FrogAuthored.ClassId
            ? null : new(node.Source.NativeClassId, lethal, MathF.Atan2(direction.Y, direction.X));
        // Native class-193 Seekers own their heading controller; only older
        // generic hostiles have the separate Rac1EnemyMotion adapter.
        if (enemy.Reaction is not null && enemy.Motion is not null)
            enemy.Motion.SetFacing(enemy.Reaction.FacingYaw);
        enemy.Grounded = false;
        SetRac1EnemyFlame(enemy, false);
        if (lethal && !enemy.RewardEmitted)
        {
            enemy.RewardEmitted = true;
            enemy.Body.CollisionLayer = 0;
            if (node.Source.NativeClassId != Rac1FrogAuthored.ClassId)
            {
                var pickups = _rac1BoltPickups.Emit(SelectRac1CrateReward(Rac1EnemyMotion.RewardCentre(node.Source)));
                SpawnRac1BoltPickups(node.Root.GlobalPosition, pickups);
                TrackRegisteredActorPickups(node, pickups.Select(pickup => pickup.PickupId));
            }
        }
        _rac1CombatStatus = $"enemy i{node.Source.InstanceIndex}: {(lethal ? "lethal reaction" : "hit reaction")}";
        GD.Print($"[rac1-enemies] class{node.Source.NativeClassId} i{node.Source.InstanceIndex}: " +
            $"{(lethal ? "lethal" : "nonlethal")} damage from {damage.Source.Kind} " +
            $"class{damage.Source.NativeClassId} i{damage.Source.RuntimeId}");
        return true;
    }

    private void PresentRac1EnemyDeath(Rac1HostedEnemy enemy)
    {
        if (enemy.DeathPresented) return;
        enemy.DeathPresented = true;
        enemy.Body.CollisionLayer = enemy.Body.CollisionMask = 0;
        SetRac1EnemyFlame(enemy, false);
        if (Rac1AridiaEnemies.IsEnemyClass(enemy.Node.Source.NativeClassId) ||
            enemy.Node.Source.NativeClassId is Rac1SeekerSession.ClassId or Rac1FrogAuthored.ClassId) return;
        if (enemy.Node.Source.NativeClassId == 749) PlayRac1PickupSound(749, 7, enemy.Node.Root.GlobalPosition);
        else if (enemy.Node.Source.NativeClassId != Rac1NovalisBirdbotSession.ClassId)
            SpawnRac1EnemyFragments(enemy);
    }

    private void SpawnRac1EnemyFragments(Rac1HostedEnemy enemy)
    {
        // The robot's dedicated fragment identities are native (0x2e15a8..0x2e1638).
        // Until 0x263fd8 is translated, scattering uses the existing host debris adapter.
        int[] pieces = enemy.Node.Source.NativeClassId == Rac1NovalisPeckbotSession.ClassId
            ? [1747, 1748, 1749] : [1933, 1934, 1935, 1963];
        foreach (int nativeClass in pieces)
        {
            if (_rac1PickupAssets?.Models.ContainsKey(nativeClass) != true) continue;
            var motion = new Rac1CrateDebris(nativeClass, ToEffectNative(enemy.Node.Root.GlobalPosition) + new NVector3(0, 0, 1),
                new(Rac1EffectRandom.Range(-4, 4) / 60, Rac1EffectRandom.Range(-4, 4) / 60, Rac1EffectRandom.Range(4, 8) / 60),
                new(0, 0, Rac1EffectRandom.Angle()), 1, 1, Rac1EffectRandom);
            var node = CreateRac1EffectNode(nativeClass, $"rac1_enemy_fragment_{nativeClass}");
            node.GlobalPosition = FromEffectNative(motion.Position);
            _rac1Debris.Add((node, motion));
        }
    }

    private void SetRac1EnemyFlame(Rac1HostedEnemy enemy, bool emitting)
    {
        if (enemy.Node.Source.NativeClassId is not (1440 or Rac1AridiaEnemies.Constructorbot) || _sceneResult is null) return;
        if (emitting && enemy.Flame is null)
        {
            // Host particle presentation, not a recovered retail particle/texture program.
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = Colors.White,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                VertexColorUseAsAlbedo = true,
                AlbedoTexture = new GradientTexture2D
                {
                    Width = 32,
                    Height = 32,
                    Fill = GradientTexture2D.FillEnum.Radial,
                    FillFrom = new(.5f, .5f),
                    FillTo = new(.5f, 1),
                    Gradient = new Gradient { Colors = [Colors.White, new(1, 1, 1, 0)] }
                },
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled
            };
            var flame = new GpuParticles3D
            {
                Name = $"EnemyFlame_i{enemy.Node.Source.InstanceIndex}",
                Amount = 90,
                Lifetime = .4,
                Emitting = false,
                DrawPass1 = new QuadMesh { Size = new(.28f, .28f), Material = material },
                VisibilityAabb = new(new(-6, -6, -6), new(12, 12, 12)),
                ProcessMaterial = new ParticleProcessMaterial
                {
                    Direction = Vector3.Forward,
                    Spread = 7,
                    InitialVelocityMin = 10,
                    InitialVelocityMax = 12,
                    Gravity = Vector3.Up * .3f,
                    ScaleMin = .5f,
                    ScaleMax = 1.3f,
                    ColorRamp = new GradientTexture1D
                    {
                        Gradient = new Gradient
                        {
                            Colors = [new(1, .9f, .3f, .8f), new(1, .25f, .03f, 0)]
                        }
                    }
                }
            };
            _sceneResult.Root.AddChild(flame); enemy.Flame = flame;
        }
        if (enemy.Flame is not { } particles) return;
        particles.GlobalPosition = EnemySocket(enemy);
        particles.LookAt(particles.GlobalPosition + EnemyForward(enemy.Node));
        particles.Emitting = emitting;
    }
}
