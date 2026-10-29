using Godot;
using static Godot.GodotObject;
using static OBP.Game.Rac1EnemyCoordinates;
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

namespace OBP.Game;

internal sealed partial class Rac1EnemyHost : IDisposable
{
    private readonly RuntimeWorld _world;
    private readonly RuntimeWorldScene.Result _sceneResult;
    private readonly Rac1EnemyPorts _ports;
    private bool _disposed;
    private readonly Rac1EnemyTickClock _clock = new();
    private readonly Rac1ToadEnemyAdapter _toads;
    private readonly Rac1Class1440EnemyAdapter _robots;
    private readonly Rac1PeckbotEnemyAdapter _peckbots;
    private readonly Rac1BirdbotEnemyAdapter _birdbots;
    private readonly Rac1SeekerEnemyAdapter _seekers;
    private readonly Dictionary<int, IRac1EnemyAdapter> _families;

    internal Rac1EnemyHost(RuntimeWorld world, RuntimeWorldScene.Result scene, Rac1WorldContext context,
        Rac1WorldPresentation presentation, Rac1EnemyPorts ports)
    {
        _world = world; _sceneResult = scene; Context = context; Presentation = presentation; _ports = ports;
        Roster = new(enemy => (enemy.Node.Source.SourceGame, enemy.Node.Source.InstanceIndex), RetireContact);
        _toads = new(this); _robots = new(this); _peckbots = new(this); _birdbots = new(this); _seekers = new(this);
        _families = new IRac1EnemyAdapter[] { _toads, _robots, _peckbots, _birdbots, _seekers }.ToDictionary(family => family.ClassId);
    }

    internal Rac1WorldContext Context { get; }
    internal Rac1WorldPresentation Presentation { get; }
    internal Rac1EnemyRoster<Rac1HostedEnemy> Roster { get; }
    private Rac1EnemyRoster<Rac1HostedEnemy> _rac1Enemies => Roster;
    internal Rac1Class1440Session? Class1440 => _robots.Session;
    internal Rac1NovalisPeckbotSession? Peckbots => _peckbots.Session;
    internal Rac1NovalisBirdbotSession? Birdbots => _birdbots.Session;
    internal Rac1SeekerSession? Seekers => _seekers.Session;
    internal uint NativeFrame => _clock.Frame;
    internal long BirdbotShotCount => _birdbots.ShotCount;
    internal bool HasBirdbotShot(int owner) => _birdbots.HasShot(owner);
    internal PlayerHost? Player => _ports.Player.Player();
    internal Rac1RatchetNanotechSession Health => _ports.Player.Health();
    internal Rac1EnemyCombatPorts Combat => _ports.Combat;
    internal RuntimeWorldScene.Result Scene => _sceneResult;
    internal bool Current => !_disposed && _ports.Neighbors.Current();
    internal bool Owns(Rac1HostedEnemy enemy) => Current && !enemy.Retired && Roster.Contains(enemy);
    internal IEnumerable<RuntimeWorldScene.DynamicObjectNode> Targets => Rac1EnemyNodes;
    internal Rac1PickupRandom Random => _ports.Random();
    private Rac1PickupRandom Rac1EffectRandom => Random;
    internal PlayerHost? _player => Player;
    internal Rac1RatchetNanotechSession _rac1Nanotech => Health;
    internal Rac1Class1440Session? _rac1Class1440 => Class1440;
    internal Rac1NovalisPeckbotSession? _rac1Peckbots => Peckbots;
    internal Rac1NovalisBirdbotSession? _rac1Birdbots => Birdbots;
    internal Rac1SeekerSession? _rac1Seekers => Seekers;
    internal const float AridiaHostUpdateRadius = 24f;
    internal const float AridiaAnimatedPresentationRadius = 18f;
    internal IEnumerable<RuntimeWorldScene.DynamicObjectNode> Rac1EnemyNodes => _rac1Enemies.Values
        .Where(enemy => !enemy.Buried).Select(enemy => enemy.Node);
    // Novalis host sleep only; Aridia maintains its own buried and animation policies.
    internal const float Rac1NovalisEnemySleepDistance = 42f;

    internal void ResetRac1Enemies()
    {
        if (_disposed) return;
        ResetOwned();
    }

    private void ResetOwned()
    {
        var neighbor = _ports.Neighbors;
        var active = neighbor.Current();
        void Current(Action reset) { if (active) reset(); }
        OBP.Game.Diagnostics.DiagnosticCleanup.Run("R&C1 enemy reset",
            () => Current(neighbor.EndQueenCamera), () => Current(neighbor.ResetCarriers), () => Current(neighbor.ResetQueenChildren),
            Roster.Clear,
            () => { _robots.Session = null; _peckbots.Session = null; _birdbots.Session = null; _seekers.Session = null; },
            () => Current(neighbor.ClearFrogState), () => Current(neighbor.ResetAridia),
            _clock.Reset,
            () => Current(neighbor.ResetAridiaScripts), () => Current(neighbor.ResetConstructorbots),
            () => Current(neighbor.ResetAmoeboids), _birdbots.Reset);
    }

    private static void RetireContact(Rac1HostedEnemy enemy)
    {
        enemy.Retired = true;
        try { if (IsInstanceValid(enemy.Body) && !enemy.Body.IsQueuedForDeletion()) enemy.Body.QueueFree(); }
        finally { if (enemy.Flame is { } flame && IsInstanceValid(flame) && !flame.IsQueuedForDeletion()) flame.QueueFree(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { ResetOwned(); }
        finally { Roster.Dispose(); }
    }

    internal void ConfigureRac1Enemies(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        if (world.LevelId is not (0 or 1 or Rac1AridiaEnemies.LevelId or 5 or 6 or 9 or 10 or 11 or 15)) return;
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var reader = new FileRandomAccessReader(_ports.SourcePath());
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == world.LevelId);
        var core = Rac1LevelCore.Open(reader, level);
        var classes = Rac1StaticClasses.Read(core);
        var nodes = Presentation.HostileNodes.Values.ToList();
        if (world.LevelId == 0)
        {
            _robots.Register(world, result, Rac1LevelSettings.ReadGameplay(reader, level), nodes);
        }
        else if (world.LevelId == Rac1AridiaEnemies.LevelId)
        {
            // The native LEVEL2 nest timer lives in the gameplay PVar, not in
            // the general placement's 0x78-byte instance record.
            var levelGameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            var authoredPlacements = Rac1Instances.Parse(levelGameplay).MobyInstances;
            var volumes = Rac1ClipVolumes.Read(levelGameplay);
            _ports.Neighbors.AridiaVolumes(volumes);
            _ports.Neighbors.Aridia().SetAuthoredGroupRoster(Rac1AridiaEnemies.ReadAuthoredGroupRoster(levelGameplay));
            foreach (var authored in (world.DynamicObjects ?? []).Where(item => Rac1AridiaEnemies.IsEnemyClass(item.NativeClassId)))
            {
                var node = Rac1WorldPresentation.FindPresentedDynamic(result, authored) ?? Rac1WorldPresentation.CreateInvisibleRuntimeNode(result, authored);
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
                    _ports.Neighbors.Constructorbots.Add(authored.InstanceIndex, botStartup.Value);
                    int nativeWaypoint = Rac1AridiaConstructorbotNative.ReadWaypointRecordIndex(
                        pvar, volumes.Count);
                    _ports.Neighbors.ConstructorbotWaypoints.Add(authored.InstanceIndex,
                        FromEffectNative(volumes[nativeWaypoint].Centre));
                }
                // The Sandshark init routine's +0x258 branch enters state29.
                var runtime = Context.MobyRuntime.Register(authored, node.State,
                    nativeState: botStartup?.NativeState ?? (linkedActivationMobyIndex.HasValue ? 29 : 1),
                    pvar: pvar);
                node.ApplyState(runtime.EntityState);
                Vector3 forward = EnemyForward(node);
                NVector3 native = ToEffectNative(forward);
                int nestWait = authored.NativeClassId == Rac1AridiaEnemies.Nest
                    ? Rac1AridiaEnemies.ReadNestInitialWaitTicks(pvar)
                    : 60;
                _ports.Neighbors.Aridia().Register(authored.InstanceIndex, authored.NativeClassId,
                    MathF.Atan2(native.Y, native.X), nestWait,
                    linkedActivationMobyIndex: linkedActivationMobyIndex,
                    groupId: Rac1AridiaEnemies.ReadAuthoredGroup(authoredPlacements[authored.InstanceIndex].RawRecord),
                    constructorbotStartupSequence: botStartup?.Sequence ?? 1);
                nodes.Add(node);
            }
            GD.Print("[rac1-aridia] Constructorbot native startup " +
                string.Join(", ", _ports.Neighbors.Constructorbots.Values
                    .GroupBy(start => start.NativeState).OrderBy(group => group.Key)
                    .Select(group => $"state{group.Key}={group.Count()}")));
            _ports.Neighbors.AridiaScripts(world, result, authoredPlacements);
        }
        else
        {
            _peckbots.Session = new(Context.MobyRuntime, Rac1EffectRandom);
            _birdbots.Session = new(Context.MobyRuntime);
            if (world.LevelId is 9 or 15)
                _seekers.Session = new(Context.MobyRuntime);
        }
        if (world.LevelId is 6 or 10)
        {
            var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            _ports.Neighbors.Frogs(world, result, Rac1Paths.Read(gameplay),
                Rac1ClipVolumes.Read(gameplay), nodes);
        }
        _peckbots.Register(world, result, nodes);
        _birdbots.Register(world, result, nodes);
        _seekers.Register(world, result, nodes);
        _ports.Neighbors.Amoeboids(world, result, nodes, Rac1LevelSettings.ReadGameplay(reader, level));
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
                    (Rac1AmoeboidAuthored.IsFamily(group.Key) && !_ports.Neighbors.AmoeboidSession()!.Get(node.Source.InstanceIndex).Active) ||
                    (group.Key == Rac1FrogAuthored.ClassId && !_ports.Neighbors.FrogSession()!.Get(node.Source.InstanceIndex).Presented)
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
                            ? _ports.Neighbors.AmoeboidSession()!.Get(node.Source.InstanceIndex).Sequence
                        : group.Key == Rac1FrogAuthored.ClassId
                            ? _ports.Neighbors.FrogSession()!.Get(node.Source.InstanceIndex).Sequence
                        : group.Key == Rac1AridiaEnemies.Constructorbot &&
                            _ports.Neighbors.Constructorbots.TryGetValue(node.Source.InstanceIndex, out var startup) &&
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
                     _ports.Neighbors.AmoeboidSession()!.Get(node.Source.InstanceIndex).Active))
                    node.ApplyAnimationSample(enemy.Animation.Sample);
                else if (group.Key == Rac1AridiaEnemies.Sandshark)
                    _ports.Neighbors.HideAridia(enemy);
                SynchronizeEnemyTransform(enemy);
                _rac1Enemies.Add(node.Source.InstanceIndex, enemy);
                if (Rac1AmoeboidAuthored.IsFamily(group.Key))
                    _ports.Neighbors.InitializeAmoeboid(enemy);
            }
        }
        _ports.Neighbors.Carriers(result);
        if (world.LevelId == 6)
        {
            var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            var triggerClass = classes.Mobies[Rac1FrogWaveTriggers.ClassId];
            var groups = Rac1MobyIndexGroups.Read(gameplay, world.DynamicObjects?.Count ?? 0);
            var queenClass = classes.Mobies[Rac1FrogQueenGroup.QueenClassId];
            var overlay = Rac1ExecutableOverlay.Read(reader, level);
            var cameraAnchor = overlay.Read(0x167540, 12);
            _ports.Neighbors.Queen(world, result, groups, queenClass,
                core.Assets.AsSpan(queenClass.AssetOffset, queenClass.AssetLength),
                Rac1AlienQueenGateNative.Read(overlay), new NVector3(
                    System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(cameraAnchor),
                    System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(cameraAnchor.Slice(4)),
                    System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(cameraAnchor.Slice(8))));
            _ports.Neighbors.Waves(world, result,
                groups, Rac1ClipVolumes.Read(gameplay), triggerClass,
                core.Assets.AsSpan(triggerClass.AssetOffset, triggerClass.AssetLength));
        }
        GD.Print($"[rac1-enemies] {_rac1Enemies.Count} animated/colliding retail placements; " +
            $"{_rac1Class1440?.RegisteredCount ?? 0} class1440, {_rac1Peckbots?.RegisteredCount ?? 0} Novalis Peckbots, " +
            $"{_rac1Birdbots?.RegisteredCount ?? 0} Novalis Birdbots, " +
            $"{_rac1Seekers?.RegisteredCount ?? 0} Seekers, " +
            $"{_ports.Neighbors.FrogSession()?.RegisteredCount ?? 0} Frogs ({_ports.Neighbors.FrogSession()?.PresentedCount ?? 0} admitted)");
    }

    internal static Vector3 EnemyForward(RuntimeWorldScene.DynamicObjectNode node) =>
        (node.Root.ToGlobal(FromEffectNative(NVector3.UnitX)) - node.Root.GlobalPosition).Normalized();

    internal void TickRac1Hostiles(double delta)
    {
        if (_disposed || !_ports.Neighbors.Current() || _player is null) return;
        _clock.Advance(delta, TickNativeActors, _birdbots.TickProjectiles);
    }

    private bool TickNativeActors()
    {
        if (!Current || _player is null) return false;
        if (!_ports.Player.Persistent()) _rac1Nanotech.UpdateNative();
        _ports.Neighbors.TickAmoeboidGates();
        _ports.Neighbors.TickShuttle();
        _ports.Neighbors.TickWaves();
        _ports.Neighbors.TickQueenFlight();
        _ports.Neighbors.TickQueenRecovery();
        _ports.Neighbors.TickQueenChildren();
        foreach (var enemy in _rac1Enemies.Values.OrderBy(enemy => enemy.Node.Source.InstanceIndex))
        {
            var node = enemy.Node;
            if (!IsInstanceValid(node.Root)) continue;
            if (node.Source.NativeClassId == Rac1FrogSession.ClassId &&
                _ports.Neighbors.CarrierChild(node.Source.InstanceIndex))
                _ports.Neighbors.TickCarrier(enemy);
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
                if (enemy.Buried) _ports.Neighbors.HideAridia(enemy);
                continue;
            }
            // Distant Novalis actors retain their meshes, authored locations
            // and animation phase, but need no contact probes, sounds or AI
            // until Ratchet enters their gameplay neighbourhood.
            if (node.Source.NativeClassId == Rac1FrogAuthored.ClassId &&
                node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) >
                    30f * 30f) continue;
            if (node.Source.NativeClassId == Rac1SeekerSession.ClassId &&
                _rac1Seekers!.Get(node.Source.InstanceIndex).NativeState is not
                    (Rac1SeekerSession.HurtState or Rac1SeekerSession.DeathState or Rac1SeekerSession.ExplosionState) &&
                node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) >
                    Rac1NovalisEnemySleepDistance * Rac1NovalisEnemySleepDistance)
                continue;
            if (_world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 1 } &&
                node.Source.NativeClassId is Rac1NovalisPeckbotSession.ClassId or Rac1NovalisBirdbotSession.ClassId &&
                node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) >
                    Rac1NovalisEnemySleepDistance * Rac1NovalisEnemySleepDistance)
                continue;
            if (_ports.Combat.Chicken(node.Source.InstanceIndex)) continue;
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
                _ports.Neighbors.TickAridia(enemy);
                if (enemy.Buried) continue;
                aridiaStepped = true;
            }
            foreach (int sound in enemy.Animation.Tick())
                if (node.Root.GlobalPosition.DistanceTo(_player.GlobalPosition) < 40 && !_ports.Combat.ActorSound(node, sound))
                    _ports.Combat.Sound(node.Source.NativeClassId, sound, node.Root.GlobalPosition);
            if (_families.TryGetValue(node.Source.NativeClassId, out var family)) family.Tick(enemy);
            else if (Rac1AmoeboidAuthored.IsFamily(node.Source.NativeClassId)) _ports.Neighbors.TickAmoeboid(enemy);
            else if (node.Source.NativeClassId == Rac1FrogAuthored.ClassId) _ports.Neighbors.TickFrog(enemy);
            else if (!aridiaStepped) _ports.Neighbors.TickAridia(enemy);
            // All known Aridia attacks occur within the native-candidate
            // host encounter region. Preserve gameplay ticks at 24 units,
            // but avoid CPU skinning/GPU buffer uploads beyond 18 units.
            if (!aridia || node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) <=
                AridiaAnimatedPresentationRadius * AridiaAnimatedPresentationRadius)
                node.ApplyAnimationSample(enemy.Animation.Sample);
            SynchronizeRac1Enemy(enemy);
            if (Rac1AridiaEnemies.IsEnemyClass(node.Source.NativeClassId))
                _ports.Neighbors.ShowAridia(enemy);
            if (!IsRac1GameplayActive(node)) PresentRac1EnemyDeath(enemy);
            if (_rac1Nanotech.Probe().IsDead) return false;
        }
        return true;
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
        else MoveRac1EnemyContact(enemy, FromEffectNative(motion));
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

    internal bool ApplyRac1EnemyWeaponDamage(RuntimeWorldScene.DynamicObjectNode node, Rac1GameplayDamageEvent damage)
    {
        if (!Current || !Roster.TryGetValue(node.Source.InstanceIndex, out var owned) || !ReferenceEquals(owned.Node, node)) return false;
        if (Rac1AmoeboidAuthored.IsFamily(node.Source.NativeClassId))
            return _ports.Neighbors.AmoeboidDamage(node, damage);
        if (Rac1AridiaEnemies.IsEnemyClass(node.Source.NativeClassId))
            return _ports.Neighbors.AridiaDamage(node, damage);
        if (!Context.MobyRuntime.CanDispatchDamage(damage)) return false;
        var enemy = _rac1Enemies[node.Source.InstanceIndex];
        bool lethal;
        int sequence;
        if (_families.TryGetValue(node.Source.NativeClassId, out var family))
        {
            var admitted = family.Damage(damage);
            lethal = admitted.Lethal; sequence = admitted.Sequence;
        }
        else if (node.Source.NativeClassId == Rac1FrogAuthored.ClassId)
        {
            var result = Context.MobyRuntime.DispatchDamage<Rac1FrogDamageResult>(damage);
            lethal = result.Lethal; sequence = result.Frame.Sequence;
            if (!result.Frame.Presented)
            {
                node.ApplyState(Context.MobyRuntime.Require(node.Source).EntityState);
                enemy.Body.CollisionLayer = enemy.Body.CollisionMask = 0;
            }
        }
        else throw new InvalidDataException("No admitted R&C1 enemy damage adapter.");
        if (node.Source.NativeClassId != Rac1SeekerSession.ClassId || enemy.Animation.Sequence != sequence)
            enemy.Animation.Select(sequence, 1);
        Vector3 attackerPosition = _player!.GlobalPosition;
        if (damage.Source is { Kind: Rac1GameplayEntityKind.Projectile, NativeClassId: 121 } &&
            Presentation.Projectiles.TryGetValue(damage.Source.RuntimeId, out var projectile))
            attackerPosition = projectile.Node.GlobalPosition;
        else if (damage.Source is { Kind: Rac1GameplayEntityKind.Projectile, NativeClassId: 74 } &&
            _ports.Combat.MineOrigin((int)damage.Source.RuntimeId) is { } mine)
            attackerPosition = mine;
        else if (damage.Source is { Kind: Rac1GameplayEntityKind.Projectile, NativeClassId: 186 } &&
            _ports.Combat.DoomOrigin((int)damage.Source.RuntimeId) is { } bot)
            attackerPosition = bot;
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
                var pickups = Context.BoltPickups.Emit(_ports.Combat.Reward(Rac1EnemyMotion.RewardCentre(node.Source)));
                _ports.Combat.Pickups(node.Root.GlobalPosition, pickups);
                _ports.Combat.TrackPickups(node, pickups.Select(pickup => pickup.PickupId));
            }
        }
        Context.CombatStatus = $"enemy i{node.Source.InstanceIndex}: {(lethal ? "lethal reaction" : "hit reaction")}";
        GD.Print($"[rac1-enemies] class{node.Source.NativeClassId} i{node.Source.InstanceIndex}: " +
            $"{(lethal ? "lethal" : "nonlethal")} damage from {damage.Source.Kind} " +
            $"class{damage.Source.NativeClassId} i{damage.Source.RuntimeId}");
        return true;
    }

    internal void PresentRac1EnemyDeath(Rac1HostedEnemy enemy)
    {
        if (enemy.DeathPresented) return;
        enemy.DeathPresented = true;
        enemy.Body.CollisionLayer = enemy.Body.CollisionMask = 0;
        SetRac1EnemyFlame(enemy, false);
        if (Rac1AridiaEnemies.IsEnemyClass(enemy.Node.Source.NativeClassId) ||
            enemy.Node.Source.NativeClassId is Rac1SeekerSession.ClassId or Rac1FrogAuthored.ClassId) return;
        if (enemy.Node.Source.NativeClassId == 749) _ports.Combat.Sound(749, 7, enemy.Node.Root.GlobalPosition);
        else if (enemy.Node.Source.NativeClassId != Rac1NovalisBirdbotSession.ClassId)
            SpawnRac1EnemyFragments(enemy);
    }

    internal void SpawnRac1EnemyFragments(Rac1HostedEnemy enemy)
    {
        // The robot's dedicated fragment identities are native (0x2e15a8..0x2e1638).
        // Until 0x263fd8 is translated, scattering uses the existing host debris adapter.
        int[] pieces = enemy.Node.Source.NativeClassId == Rac1NovalisPeckbotSession.ClassId
            ? [1747, 1748, 1749] : [1933, 1934, 1935, 1963];
        foreach (int nativeClass in pieces)
        {
            if (!_ports.Combat.HasFragment(nativeClass)) continue;
            var motion = new Rac1CrateDebris(nativeClass, ToEffectNative(enemy.Node.Root.GlobalPosition) + new NVector3(0, 0, 1),
                new(Rac1EffectRandom.Range(-4, 4) / 60, Rac1EffectRandom.Range(-4, 4) / 60, Rac1EffectRandom.Range(4, 8) / 60),
                new(0, 0, Rac1EffectRandom.Angle()), 1, 1, Rac1EffectRandom);
            _ports.Combat.Fragment(nativeClass, motion);
        }
    }

    internal void SetRac1EnemyFlame(Rac1HostedEnemy enemy, bool emitting)
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
