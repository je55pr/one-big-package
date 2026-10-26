using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.IO;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

public partial class OBPGame
{
    private readonly ActorWorldHost _actorWorld = new();
    private ActorSourceCache? _actorSources;
    private ActorRuntimeSet? _actors => _actorWorld.Runtime;
    private ActorSourceCache ActorSources => _actorSources ??= new(_hostOperations, ActorFactoryRegistrations.SourceImports, SourceFileStamp);

    private static WeaponTargetKey ActorTarget(RuntimeWorldScene.DynamicObjectNode node) =>
        new(node.Source.SourceGame, node.Source.NativeClassId, node.Source.InstanceIndex);

    private void ConfigureRegisteredActors(FusionLabDefinition? lab)
    {
        if (!OrdinaryPlayerSession || _playerDamageWorld is null) return;
        _actorWorld.Begin(_playerDamageWorld, _playerDamageLease, lab);
        ConfigureActorFactories();
        foreach (var definition in PlayerEquipmentRegistrations.Catalog.Definitions.Where(definition =>
            definition.Category == EquipmentCategory.Weapon && definition.Implementation == EquipmentImplementation.Implemented))
        {
            var key = definition.Item!.Value; string build = definition.BuildId;
            _actors!.RegisterWeapon(key, build, () => PlayerEquipmentSources.GetValueOrDefault(key.SourceGame) == build);
        }
        // Native enemy clocks are source registrations, not destination gates.
        // World missions, lifts, water, music and authored pickups remain with
        // their original environment adapters.
        RegisterExistingNativeTargets();
        if (lab is null) return;
        foreach (var placement in lab.Actors)
        {
            _actorWorld.Placements.Add(placement.Id, placement);
            PrepareActorSource(placement);
        }
        TickActorPreparation();
    }

    private void ResetRegisteredActors() => _actorWorld.Reset();

    private void StepRegisteredActorsPhysics()
    {
        if (_actors is null || _player is null || _movementStateLoading) return;
        _actors.Step(ToNumerics(_player.GlobalPosition), PlayerAlive);
    }

    private void ClearActorSourceCaches()
    {
        _actorSources?.Clear();
        foreach (var (id, instance) in _actorWorld.Placed.ToArray())
        {
            RetirePlacedActor(instance);
            _actorWorld.Problems[id] = "Actor source attachments changed. Reset the encounter after source preparation.";
        }
        _actorWorld.Placed.Clear(); _actorWorld.Preparation.Clear(); _actorWorld.Sources.Clear();
    }

    private void PrepareActorSource(ActorPlacement placement)
    {
        try
        {
            // Reject unsupported recipes before starting IO or constructing a scene.
            _ = _actorWorld.Factories!.Resolve(placement);
            var source = _sources.Get(placement.Source.Game);
            if (source is null || source.Identity.BuildId != placement.Source.Build)
                throw new InvalidDataException("Missing or unsupported " + placement.Source.Game + " source.");
            var stamp = SourceFileStamp(source.Path) ?? throw new InvalidDataException("Actor source is unavailable.");
            var version = new ActorSourceVersion(source.Game, source.Identity.BuildId, source.Path, stamp.Length, stamp.Written);
            _actorWorld.Sources[placement.Id] = version;
            _actorWorld.Preparation[placement.Id] = ActorSources.Library(version, placement.LevelId);
        }
        catch (Exception error) { _actorWorld.Problems[placement.Id] = error.Message; }
    }

    private void TickActorPreparation()
    {
        if (_actors is null || _actorWorld.Lab is null) return;
        bool preparing = _actorWorld.Preparation.Values.Any(task => !task.IsCompleted);
        foreach (var placement in _actorWorld.Placements.Values.OrderBy(placement => placement.Source.Game)
            .ThenBy(placement => placement.NativeClassId).ThenBy(placement => placement.Id, StringComparer.Ordinal))
        {
            // Prepare in parallel, activate the admitted set together in stable
            // source order. Background completion order must not choose runtime
            // identities or give one enemy extra native ticks before the others.
            if (preparing) break;
            if (_actorWorld.Placed.ContainsKey(placement.Id) || _actorWorld.Problems.ContainsKey(placement.Id)) continue;
            var source = _sources.Attached.Values.SingleOrDefault(source => source.Game == placement.Source.Game);
            if (source is null) { _actorWorld.Problems[placement.Id] = "Source was detached before actor activation."; continue; }
            var task = _actorWorld.Preparation.GetValueOrDefault(placement.Id);
            if (task is null || !task.IsCompleted) continue;
            try
            {
                var library = task.GetAwaiter().GetResult();
                var stamp = _actorWorld.Sources[placement.Id];
                if (source.Path != stamp.Path || source.Identity.BuildId != placement.Source.Build ||
                    SourceFileStamp(source.Path) != (stamp.Length, stamp.Written))
                    throw new InvalidDataException("Actor source changed before activation. Reset after reattaching the source.");
                var actor = _actorWorld.Factories!.Resolve(placement).Create(placement, library, stamp);
                try { RegisterActorView(actor); _actorWorld.Placed.Add(placement.Id, actor.Registration.Instance); }
                catch { actor.Dispose(); throw; }
                GD.Print($"[actors] {placement.Id}: {SourceIdentityWire.FormatSource(actor.Registration.Source.Game)} class {placement.NativeClassId} active in current lease");
            }
            catch (Exception error)
            {
                _actorWorld.Problems[placement.Id] = error.Message;
                GD.PrintErr($"[actors] {placement.Id} unavailable: {error.Message}");
            }
        }
        if (_fusionLabStatus is not null)
            _fusionLabStatus.Text = "OBP FUSION LAB  |  PAUSE: LOADOUT / SETTINGS\n" +
                $"Mixed encounter: {_actorWorld.Placed.Count}/{_actorWorld.Placements.Count} source actors" +
                (_actorWorld.Problems.Count > 0 ? "\n" + string.Join("\n", _actorWorld.Problems.Select(pair => pair.Key + ": " + pair.Value)) :
                    _actorWorld.Placed.Count < _actorWorld.Placements.Count ? " · preparing original assets in the background" : " · live native controllers");
    }

    private void RegisterActorView(HostedActor actor)
    {
        _actors!.CreateAndRegister(() => actor);
        try { _actorWorld.Views.Add(actor.Registration.Instance.Target, actor); }
        catch { _actors.Retire(actor.Registration.Instance); throw; }
        _rac1BombOcclusionFrame = ulong.MaxValue;
    }

    private void TrackRegisteredActorPickups(RuntimeWorldScene.DynamicObjectNode node, IEnumerable<int> pickups)
    {
        var key = ActorTarget(node);
        if (_actors?.Find(key) is null) return;
        if (!_actorWorld.Rewards.TryGetValue(key, out var owned)) _actorWorld.Rewards.Add(key, owned = []);
        owned.UnionWith(pickups);
    }

    private void ClearRegisteredActorTransients(WeaponTargetKey key)
    {
        if (_actorWorld.Audio.Remove(key, out var audio)) audio.StopAll();
        if (!_actorWorld.Rewards.Remove(key, out var pickups)) return;
        foreach (int id in pickups)
        {
            _rac1BoltPickups.Discard(id); _rac1PickupMotion.Remove(id);
            if (_rac1PickupNodes.Remove(id, out var node) && IsInstanceValid(node)) node.QueueFree();
        }
    }

    private bool PlayRegisteredRac1ActorSound(RuntimeWorldScene.DynamicObjectNode node, int localSound)
    {
        var key = ActorTarget(node);
        if (!_actorWorld.Placed.Values.Any(instance => instance.Target == key)) return false;
        if (_rac1PickupAssets?.Sounds?.TryGetValue((node.Source.NativeClassId, localSound), out var sound) != true)
        { GD.Print($"[actors] {key}: original sound {localSound} unavailable"); return true; }
        if (!_actorWorld.Audio.TryGetValue(key, out var audio)) _actorWorld.Audio.Add(key, audio = new());
        _actorWorld.PlaybackRandom ??= OBP.RAC1.Gameplay.Rac1PickupRandom.FromSeed(unchecked((uint)(_args.FixedSeed ?? System.Environment.TickCount)));
        var clip = sound!.SelectPitch(_actorWorld.PlaybackRandom);
        if (clip.FrameCount > 0) audio.PlayEffect(node.Root, new(clip, OBP.Runtime.Audio.RuntimeAudioCategory.SoundEffect, gain: sound.Gain, position: new(0, 0, 0)));
        return true;
    }

    public EquipmentChangeResult ResetPlacedActor(string id)
    {
        if (_actors is null || !_actorWorld.Placements.TryGetValue(id, out var placement)) return new(false, "No registered actor placement.");
        if (_actorWorld.Placed.Remove(id, out var previous))
            RetirePlacedActor(previous);
        _rac1BombOcclusionFrame = ulong.MaxValue;
        _actorWorld.Problems.Remove(id); PrepareActorSource(placement); TickActorPreparation();
        return new(_actorWorld.Placed.ContainsKey(id), _actorWorld.Problems.GetValueOrDefault(id) ?? "Actor assets are preparing.");
    }

    private void RetirePlacedActor(ActorInstanceKey instance)
    {
        try { _actors?.Retire(instance); }
        finally
        {
            _actorWorld.Views.Remove(instance.Target);
            _playerDamageActors.Remove(instance); _playerNative?.Life.RetireActor(instance);
            _rac1BombOcclusionFrame = ulong.MaxValue;
        }
    }

    private IReadOnlyDictionary<string, string> ReadPlacedActorStatus() => _actorWorld.Placements.Values.ToDictionary(
        placement => placement.Id, placement => $"{SourceIdentityWire.FormatSource(placement.Source.Game)} / class {placement.NativeClassId}: " +
            (_actorWorld.Problems.GetValueOrDefault(placement.Id) ??
                (_actorWorld.Placed.TryGetValue(placement.Id, out var instance) && _actors?.Find(instance.Target) is { } actor
                    ? (actor.Observe().Alive ? "Active" : "Defeated — reset to replay this encounter") +
                        string.Concat(actor.Registration.Capabilities.Where(capability => capability.Admission != CapabilityAdmission.Ready)
                            .Select(capability => "\n" + capability.Reason)) : "Preparing original assets")));
}
