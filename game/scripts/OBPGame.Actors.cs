using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.IO;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OneBigPackage;

public partial class OBPGame
{
    private ActorRuntimeSet? _actors;
    private readonly Dictionary<WeaponTargetKey, HostedActor> _actorViews = [];
    private readonly Dictionary<string, ActorPlacement> _actorPlacements = [];
    private readonly Dictionary<string, ActorInstanceKey> _placedActors = [];
    private readonly Dictionary<string, Task<RuntimeWorld>> _actorPlacementTasks = [];
    private readonly Dictionary<string, (string Path, long Length, long Written)> _actorPlacementStamps = [];
    private readonly Dictionary<WeaponTargetKey, CharacterBody3D> _actorContactBodies = [];
    private readonly Dictionary<WeaponTargetKey, HashSet<int>> _actorRewardPickups = [];
    private readonly Dictionary<WeaponTargetKey, RuntimeAudioHost> _actorAudio = [];
    private OBP.RAC1.Gameplay.Rac1PickupRandom? _actorPlaybackRandom;
    private readonly Dictionary<(ObpSourceGame Game, string Build, int Level, string Path, long Length, long Written), Task<RuntimeWorld>> _actorSourceTasks = [];
    private readonly Dictionary<string, string> _actorProblems = [];
    private FusionLabDefinition? _actorLab;

    private sealed class HostedActor(ActorRegistration registration, RuntimeWorldScene.DynamicObjectNode node,
        Func<ActorObservation> observe, Func<WeaponDamageIntent, ActorHitResult> damage,
        Action<ActorTickFacts>? tick = null, Action? retire = null) : IRegisteredActor
    {
        public ActorRegistration Registration { get; } = registration;
        public RuntimeWorldScene.DynamicObjectNode Node { get; } = node;
        private bool _retired;
        public ActorObservation Observe() => _retired || !IsInstanceValid(Node.Root)
            ? new(NVector3.Zero, false, true, null, 0, null) : observe();
        public void Tick(ActorTickFacts facts) { if (!_retired) tick?.Invoke(facts); }
        public ActorHitResult Apply(WeaponDamageIntent intent) => _retired ? new(false, true, "Actor was retired.") : damage(intent);
        public void Dispose()
        {
            if (_retired) return;
            _retired = true; retire?.Invoke();
        }
    }

    private static WeaponTargetKey ActorTarget(RuntimeWorldScene.DynamicObjectNode node) =>
        new(node.Source.SourceGame, node.Source.NativeClassId, node.Source.InstanceIndex);

    private void ConfigureRegisteredActors(FusionLabDefinition? lab)
    {
        if (!OrdinaryPlayerSession || _playerDamageWorld is null) return;
        _actors = new(_playerDamageWorld, _playerDamageLease);
        foreach (var definition in PlayerEquipmentRegistrations.Catalog.Definitions.Where(definition =>
            definition.Category == EquipmentCategory.Weapon && definition.Implementation == EquipmentImplementation.Implemented))
        {
            var key = definition.Item!.Value; string build = definition.BuildId;
            _actors.RegisterWeapon(key, build, () => PlayerEquipmentSources.GetValueOrDefault(key.SourceGame) == build);
        }
        // Native enemy clocks are source registrations, not destination gates.
        // World missions, lifts, water, music and authored pickups remain with
        // their original environment adapters.
        _actors.RegisterUpdateGroup("rac1/native-enemies", _ => { if (_rac1Enemies.Count > 0) TickRac1Hostiles(1d / 60); });
        _actors.RegisterUpdateGroup("rac2/native-class2827", _ =>
        {
            foreach (var hostile in _rac2Class2827Hostiles.Values) hostile.TickCooldown();
            TickRac2OpeningMsr1NativeTick();
        });
        RegisterExistingNativeTargets();
        _actorLab = lab;
        if (lab is null) return;
        foreach (var placement in lab.Actors)
        {
            _actorPlacements.Add(placement.Id, placement);
            PrepareActorSource(placement);
        }
        TickActorPreparation();
    }

    private void ResetRegisteredActors()
    {
        _actors?.Dispose(); _actors = null;
        foreach (var audio in _actorAudio.Values) audio.StopAll();
        _actorAudio.Clear(); _actorRewardPickups.Clear(); _actorPlaybackRandom = null;
        foreach (var body in _actorContactBodies.Values)
            if (IsInstanceValid(body)) body.CollisionLayer = body.CollisionMask = 0;
        _actorContactBodies.Clear(); _actorPlacementTasks.Clear(); _actorPlacementStamps.Clear();
        _actorViews.Clear(); _actorPlacements.Clear(); _placedActors.Clear(); _actorProblems.Clear();
        _actorLab = null;
    }

    private void StepRegisteredActorsPhysics()
    {
        if (_actors is null || _player is null || _movementStateLoading) return;
        _actors.Step(ToNumerics(_player.GlobalPosition), PlayerAlive);
    }

    private void ClearActorSourceCaches()
    {
        // Reusable decoded data belongs to attached sources, not a world lease.
        _actorSourceTasks.Clear(); _actorRac1Animations.Clear(); _actorRac2Animations.Clear();
    }

    private void PruneActorSourceVersions(ObpSourceGame game, string build, string path, long length, long written)
    {
        foreach (var key in _actorSourceTasks.Keys.Where(key => key.Game == game &&
            (key.Build != build || key.Path != path || key.Length != length || key.Written != written)).ToArray())
            _actorSourceTasks.Remove(key);
        if (game == ObpSourceGame.Rac1)
            foreach (var key in _actorRac1Animations.Keys.Where(key => key.Path != path || key.Length != length || key.Written != written).ToArray())
                _actorRac1Animations.Remove(key);
        if (game == ObpSourceGame.Rac2)
            foreach (var key in _actorRac2Animations.Keys.Where(key => key.Path != path || key.Length != length || key.Written != written).ToArray())
                _actorRac2Animations.Remove(key);
    }

    private void PrepareActorSource(ActorPlacement placement)
    {
        placement.Validate();
        var game = placement.Source.Game;
        var source = _sources.Get(game);
        if (source is null || source.Identity.BuildId != placement.Source.Build)
        { _actorProblems[placement.Id] = "Missing or unsupported " + placement.Source.Game + " source."; return; }
        (long Length, long Written) stamp;
        try { stamp = SourceFileStamp(source.Path)!.Value; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _actorProblems[placement.Id] = "Actor source is unavailable: " + error.Message; return; }
        PruneActorSourceVersions(placement.Source.Game, placement.Source.Build, source.Path, stamp.Length, stamp.Written);
        var key = (placement.Source.Game, placement.Source.Build, placement.LevelId, source.Path, stamp.Length, stamp.Written);
        _actorPlacementStamps[placement.Id] = (source.Path, stamp.Length, stamp.Written);
        if (_actorSourceTasks.TryGetValue(key, out var cached) && !cached.IsFaulted)
        { _actorPlacementTasks[placement.Id] = cached; return; }
        _actorPlacementTasks[placement.Id] = _actorSourceTasks[key] = Task.Run(() =>
        {
            using var reader = new FileRandomAccessReader(source.Path);
            var imported = source.Game switch
            {
                ObpSourceGame.Rac1 => OBP.RAC1.Rac1WorldImport.Build(reader, placement.LevelId),
                ObpSourceGame.Rac2 => OBP.RAC2.Rac2WorldImport.Build(reader, placement.LevelId),
                ObpSourceGame.Rac3 => OBP.RAC3.Rac3WorldImport.Build(reader, placement.LevelId),
                _ => throw new NotSupportedException("Unregistered actor source provider."),
            };
            if (SourceFileStamp(source.Path) != stamp) throw new InvalidDataException("Actor source changed during preparation.");
            return SliceActorLibrary(imported);
        });
    }

    private static RuntimeWorld SliceActorLibrary(RuntimeWorld world)
    {
        // Native importers decide which classes are independently represented.
        // Adding another registered family needs no planet/class switch here.
        var objects = (world.DynamicObjects ?? []).ToArray();
        var textures = objects.SelectMany(source => source.Meshes).Select(mesh => (mesh.AssetKind, mesh.TextureId)).ToHashSet();
        return world with
        {
            Meshes = [],
            CollisionMeshes = [],
            AnimatedMeshes = [],
            AmbientAnimations = [],
            DynamicObjects = objects,
            Textures = world.Textures.Where(texture => textures.Contains((texture.AssetKind, texture.TextureId))).ToArray(),
            Environment = null,
            Lighting = null,
            LevelAudio = [],
            RepresentativeAudioOneShot = null
        };
    }

    private void TickActorPreparation()
    {
        if (_actors is null || _actorLab is null) return;
        bool preparing = _actorPlacementTasks.Values.Any(task => !task.IsCompleted);
        foreach (var placement in _actorPlacements.Values.OrderBy(placement => placement.Source.Game)
            .ThenBy(placement => placement.NativeClassId).ThenBy(placement => placement.Id, StringComparer.Ordinal))
        {
            // Prepare in parallel, activate the admitted set together in stable
            // source order. Background completion order must not choose runtime
            // identities or give one enemy extra native ticks before the others.
            if (preparing) break;
            if (_placedActors.ContainsKey(placement.Id) || _actorProblems.ContainsKey(placement.Id)) continue;
            var source = _sources.Attached.Values.SingleOrDefault(source => source.Game == placement.Source.Game);
            if (source is null) { _actorProblems[placement.Id] = "Source was detached before actor activation."; continue; }
            var task = _actorPlacementTasks.GetValueOrDefault(placement.Id);
            if (task is null || !task.IsCompleted) continue;
            try
            {
                var library = task.GetAwaiter().GetResult();
                var stamp = _actorPlacementStamps[placement.Id];
                if (source.Path != stamp.Path || source.Identity.BuildId != placement.Source.Build ||
                    SourceFileStamp(source.Path) != (stamp.Length, stamp.Written))
                    throw new InvalidDataException("Actor source changed before activation. Reset after reattaching the source.");
                var actor = CreatePlacedActor(placement, library, source.Path);
                try { RegisterActorView(actor); _placedActors.Add(placement.Id, actor.Registration.Instance); }
                catch { actor.Dispose(); throw; }
                GD.Print($"[actors] {placement.Id}: {SourceIdentityWire.FormatSource(actor.Registration.Source.Game)} class {placement.NativeClassId} active in current lease");
            }
            catch (Exception error)
            {
                _actorProblems[placement.Id] = error.Message;
                GD.PrintErr($"[actors] {placement.Id} unavailable: {error.Message}");
            }
        }
        if (_fusionLabStatus is not null)
            _fusionLabStatus.Text = "OBP FUSION LAB  |  PAUSE: LOADOUT / SETTINGS\n" +
                $"Mixed encounter: {_placedActors.Count}/{_actorPlacements.Count} source actors" +
                (_actorProblems.Count > 0 ? "\n" + string.Join("\n", _actorProblems.Select(pair => pair.Key + ": " + pair.Value)) :
                    _placedActors.Count < _actorPlacements.Count ? " · preparing original assets in the background" : " · live native controllers");
    }

    private HostedActor CreatePlacedActor(ActorPlacement placement, RuntimeWorld library, string sourcePath)
    {
        // Adapter registration is qualified before looking at any native class.
        var factory = ActorFactories.GetValueOrDefault(placement.AdapterId)
            ?? throw new NotSupportedException("Actor adapter is not implemented.");
        return factory(placement, library, sourcePath);
    }

    private IReadOnlyDictionary<string, Func<ActorPlacement, RuntimeWorld, string, HostedActor>> ActorFactories =>
        new Dictionary<string, Func<ActorPlacement, RuntimeWorld, string, HostedActor>>(StringComparer.Ordinal)
        {
            ["rac1/class749/v1"] = CreatePlacedRac1Toad,
            ["rac2/class2827/v1"] = CreatePlacedRac2Hostile,
            ["rac3/class500/v1"] = CreatePlacedUyaProp,
        };

    private (RuntimeWorldScene.Result Scene, RuntimeWorldScene.DynamicObjectNode Node, ActorRegistration Registration) CreateActorView(
        ActorPlacement placement, RuntimeWorld library, Func<RuntimeDynamicObject, bool> admit)
    {
        if (library.Game != placement.Source.Game || library.BuildId != placement.Source.Build || library.LevelId != placement.LevelId)
            throw new InvalidDataException("Actor library provenance does not match its placement.");
        var template = (library.DynamicObjects ?? []).OrderBy(source => source.InstanceIndex).FirstOrDefault(source =>
            source.SourceGame == placement.Source.Game && source.NativeClassId == placement.NativeClassId &&
            (!placement.SourceInstanceIndex.HasValue || source.InstanceIndex == placement.SourceInstanceIndex) && admit(source))
            ?? throw new InvalidDataException("No native instance satisfies this actor/context recipe.");
        if (template.Meshes.Count == 0) throw new InvalidDataException("The genuine actor model is missing.");
        var key = _actors!.Allocate(placement.Source.Game, placement.NativeClassId);
        // Placement is a rigid anchor; the model keeps its authored local
        // orientation/scale rather than silently being stretched to a new rig.
        var authored = RuntimeWorldScene.ToSceneTransform(template.Transform); authored.Origin = Vector3.Zero;
        var placed = RuntimeWorldScene.ToSceneTransform(placement.Transform) * authored;
        var cloned = template with
        {
            InstanceIndex = key.Target.InstanceIndex,
            Transform = RuntimeWorldScene.ToRuntimeTransform(placed),
            InteractionId = $"{placement.Id}/source:{template.InteractionId}/instance:{key.Target.InstanceIndex}"
        };
        var result = RuntimeWorldScene.Build(library with { DynamicObjects = [cloned] }, "Actor_" + placement.Id,
            new() { IncludeSky = false, IncludeCollision = false, IncludeMobyMarkers = false });
        _sceneResult!.Root.AddChild(result.Root);
        var node = result.DynamicObjectNodes!.Single();
        var registration = new ActorRegistration(key, placement.Source with { NativeId = template.InteractionId }, placement.AdapterId,
            placement.NativeContextId, [new("damage", CapabilityAdmission.Ready), new("native-update", CapabilityAdmission.Ready)]);
        return (result, node, registration);
    }

    private CharacterBody3D AddRegisteredActorBody(RuntimeWorldScene.DynamicObjectNode node, Node3D parent)
    {
        Aabb? bounds = null;
        foreach (var mesh in node.Root.GetChildren().OfType<MeshInstance3D>())
        { var next = mesh.GlobalTransform * mesh.GetAabb(); bounds = bounds?.Merge(next) ?? next; }
        var box = bounds ?? throw new InvalidDataException("Actor has no genuine model contact bounds.");
        var body = new CharacterBody3D { Name = "ActorContact_" + node.Source.InstanceIndex, CollisionLayer = 1, CollisionMask = 1 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = box.Size.Max(Vector3.One * .1f) }, Position = box.GetCenter() - node.Root.GlobalPosition });
        parent.AddChild(body); body.GlobalPosition = node.Root.GlobalPosition;
        _actorContactBodies.Add(ActorTarget(node), body);
        return body;
    }

    private void RegisterActorView(HostedActor actor)
    {
        _actors!.Register(actor);
        _actorViews.Add(actor.Registration.Instance.Target, actor);
        _rac1BombOcclusionFrame = ulong.MaxValue;
    }

    private void TrackRegisteredActorPickups(RuntimeWorldScene.DynamicObjectNode node, IEnumerable<int> pickups)
    {
        var key = ActorTarget(node);
        if (_actors?.Find(key) is null) return;
        if (!_actorRewardPickups.TryGetValue(key, out var owned)) _actorRewardPickups.Add(key, owned = []);
        owned.UnionWith(pickups);
    }

    private void ClearRegisteredActorTransients(WeaponTargetKey key)
    {
        if (_actorAudio.Remove(key, out var audio)) audio.StopAll();
        if (!_actorRewardPickups.Remove(key, out var pickups)) return;
        foreach (int id in pickups)
        {
            _rac1BoltPickups.Discard(id); _rac1PickupMotion.Remove(id);
            if (_rac1PickupNodes.Remove(id, out var node) && IsInstanceValid(node)) node.QueueFree();
        }
    }

    private bool PlayRegisteredRac1ActorSound(RuntimeWorldScene.DynamicObjectNode node, int localSound)
    {
        var key = ActorTarget(node);
        if (!_placedActors.Values.Any(instance => instance.Target == key)) return false;
        if (_rac1PickupAssets?.Sounds?.TryGetValue((node.Source.NativeClassId, localSound), out var sound) != true)
        { GD.Print($"[actors] {key}: original sound {localSound} unavailable"); return true; }
        if (!_actorAudio.TryGetValue(key, out var audio)) _actorAudio.Add(key, audio = new());
        _actorPlaybackRandom ??= OBP.RAC1.Gameplay.Rac1PickupRandom.FromSeed(unchecked((uint)(_args.FixedSeed ?? System.Environment.TickCount)));
        var clip = sound!.SelectPitch(_actorPlaybackRandom);
        if (clip.FrameCount > 0) audio.PlayEffect(node.Root, new(clip, OBP.Runtime.Audio.RuntimeAudioCategory.SoundEffect, gain: sound.Gain, position: new(0, 0, 0)));
        return true;
    }

    public EquipmentChangeResult ResetPlacedActor(string id)
    {
        if (_actors is null || !_actorPlacements.TryGetValue(id, out var placement)) return new(false, "No registered actor placement.");
        if (_placedActors.Remove(id, out var previous))
        {
            _actors.Retire(previous); _actorViews.Remove(previous.Target);
            _playerDamageActors.Remove(previous); _playerNative?.Life.RetireActor(previous);
        }
        _rac1BombOcclusionFrame = ulong.MaxValue;
        _actorProblems.Remove(id); PrepareActorSource(placement); TickActorPreparation();
        return new(_placedActors.ContainsKey(id), _actorProblems.GetValueOrDefault(id) ?? "Actor assets are preparing.");
    }

    private IReadOnlyDictionary<string, string> ReadPlacedActorStatus() => _actorPlacements.Values.ToDictionary(
        placement => placement.Id, placement => $"{SourceIdentityWire.FormatSource(placement.Source.Game)} / class {placement.NativeClassId}: " +
            (_actorProblems.GetValueOrDefault(placement.Id) ??
                (_placedActors.TryGetValue(placement.Id, out var instance) && _actors?.Find(instance.Target) is { } actor
                    ? (actor.Observe().Alive ? "Active" : "Defeated — reset to replay this encounter") +
                        string.Concat(actor.Registration.Capabilities.Where(capability => capability.Admission != CapabilityAdmission.Ready)
                            .Select(capability => "\n" + capability.Reason)) : "Preparing original assets")));
}
