using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.PS2;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

internal sealed record ActorWorldPorts(Func<ObpSourceLibrary> Sources, Func<string, (long Length, long Written)?> Stamp,
    Func<SourceWeaponKey, string?> WeaponBuild, Action<int, int, IReadOnlyDictionary<string, string>> LabStatus,
    Action<ActorInstanceKey> RetirePlayerAttack, Func<ActorInstanceKey, bool> HasPlayerAttack,
    Action InvalidateOcclusion, Action<int> DiscardPickup, Func<uint> PlaybackSeed);
internal sealed record RegisteredActorSnapshot(ActorRegistration Registration, ActorObservation State);
internal sealed record PlacedActorSnapshot(string Id, ActorInstanceKey Instance, ActorObservation State);
internal sealed record ActorPreparationProblem(string Id, string Message);
internal sealed record ActorWorldSnapshot(bool Active, bool Lab, bool Preparing, int Expected, int Placed, int Registered, int Bodies,
    int Libraries, int Assets, long Tick, ImmutableArray<ActorPreparationProblem> Problems, bool Rac2AnimationPresented);

/// <summary>Owns one active world's actors, activation barrier and scene transients; decoded source data survives world retirement.</summary>
internal sealed partial class ActorWorldHost
{
    private readonly ActorWorldPorts _ports;
    private readonly ActorSourceCache _sources;
    private readonly ActorNativeAdapters _native;
    private ActorRuntimeSet? _runtime;
    private FusionLabDefinition? _lab;
    private Node3D? _scene;
    private ActorAdapterRegistry<IActorFactory>? _factories;
    private IReadOnlyList<IActorFactory> _factoryList = [];
    private ActorPlacementPreparation? _preparation;
    private readonly Dictionary<WeaponTargetKey, HostedActor> _views = [];
    private readonly Dictionary<string, ActorInstanceKey> _placed = [];
    private readonly Dictionary<WeaponTargetKey, CharacterBody3D> _bodies = [];
    private readonly Dictionary<WeaponTargetKey, HashSet<int>> _rewards = [];
    private readonly Dictionary<WeaponTargetKey, RuntimeAudioHost> _audio = [];
    private Rac1PickupRandom? _playbackRandom;

    internal ActorWorldHost(HostOperationScope operations, ActorWorldPorts ports, ActorNativePorts native, ActorContactPorts contacts)
    {
        _ports = ports;
        _sources = new(operations, ActorFactoryRegistrations.SourceImports, ports.Stamp);
        _native = new(native, ClearTransients, DisableContactBody, (id, tick) => _runtime!.RegisterUpdateGroup(id, tick));
        Combat = new(this, contacts, () => _views.Values);
    }

    internal ActorCombatContactService Combat { get; }
    internal bool Active => _runtime is not null;
    internal long TickCount => _runtime?.TickCount ?? 0;
    internal static WeaponTargetKey Target(RuntimeWorldScene.DynamicObjectNode node) =>
        new(node.Source.SourceGame, node.Source.NativeClassId, node.Source.InstanceIndex);

    internal void Configure(bool ordinary, string? world, long generation, FusionLabDefinition? lab, Node3D scene)
    {
        if (!ordinary || world is null) return;
        if (_runtime is not null) throw new InvalidOperationException("Actors already belong to a live world lease.");
        _runtime = new(world, generation); _lab = lab; _scene = scene;
        _factoryList = _native.CreateFactories(new(_runtime, scene, _bodies), _sources);
        _factories = ActorFactoryRegistrations.Create(_runtime, _factoryList.ToArray());
        foreach (var definition in PlayerEquipmentRegistrations.Catalog.Definitions.Where(definition =>
            definition.Category == EquipmentCategory.Weapon && definition.Implementation == EquipmentImplementation.Implemented))
        {
            var key = definition.Item!.Value; string build = definition.BuildId;
            _runtime.RegisterWeapon(key, build, () => _ports.WeaponBuild(key) == build);
        }
        _native.RegisterOriginalTargets(RegisterOriginal);
        if (lab is null) return;
        _preparation = new(_sources, ReadSource, placement => { _ = _factories.Resolve(placement); }, _placed.ContainsKey,
            Activate, (id, error) => GD.PrintErr($"[actors] {id} unavailable: {error.Message}"));
        foreach (var placement in lab.Actors) { _preparation.Add(placement); _preparation.Prepare(placement); }
        PollPreparation();
    }

    private ActorSourceVersion? ReadSource(ObpSourceGame game)
    {
        var source = _ports.Sources().Get(game);
        if (source is null) return null;
        var stamp = _ports.Stamp(source.Path) ?? throw new InvalidDataException("Actor source is unavailable.");
        return new(source.Game, source.Identity.BuildId, source.Path, stamp.Length, stamp.Written);
    }

    internal void PollPreparation()
    {
        if (_runtime is null || _lab is null || _preparation is null) return;
        _preparation.Poll();
        _ports.LabStatus(_placed.Count, _preparation.Count, _preparation.Problems);
    }

    private void Activate(ActorPlacement placement, RuntimeWorld library, ActorSourceVersion source)
    {
        var actor = _factories!.Resolve(placement).Create(placement, library, source);
        try { Register(actor); _placed.Add(placement.Id, actor.Registration.Instance); }
        catch { actor.Dispose(); throw; }
        GD.Print($"[actors] {placement.Id}: {SourceIdentityWire.FormatSource(actor.Registration.Source.Game)} class {placement.NativeClassId} active in current lease");
    }

    private void Register(HostedActor actor)
    {
        _runtime!.CreateAndRegister(() => actor);
        try { _views.Add(actor.Registration.Instance.Target, actor); }
        catch { _runtime.Retire(actor.Registration.Instance); throw; }
        _ports.InvalidateOcclusion();
    }

    private void RegisterOriginal(RuntimeWorldScene.DynamicObjectNode node, Func<ActorObservation> observe, Func<WeaponDamageIntent, ActorHitResult> damage)
    {
        string build = _ports.Sources().Attached.Values.Single(source => source.Game == node.Source.SourceGame).Identity.BuildId;
        var registration = new ActorRegistration(new(_runtime!.WorldInstanceId, _runtime.Generation, Target(node)),
            new(node.Source.SourceGame, build, node.Source.InteractionId), "source-world/" + SourceIdentityWire.FormatSource(node.Source.SourceGame),
            node.Source.InteractionId, [new("damage", CapabilityAdmission.Ready), new("native-update", CapabilityAdmission.Ready)]);
        Register(new(registration, node, observe, damage));
    }

    internal void Step(System.Numerics.Vector3 playerPosition, bool playerAlive, bool suspended)
    {
        if (!suspended) _runtime?.Step(playerPosition, playerAlive);
    }

    internal bool IsAlive(ActorInstanceKey instance) => _views.TryGetValue(instance.Target, out var actor) &&
        actor.Registration.Instance == instance && actor.Observe().Alive;

    internal ActorHitResult? ApplyContact(WeaponDamageIntent intent) => _runtime?.Find(intent.Target) is { } actor
        ? _runtime.Apply(actor.Registration.Instance, intent) : null;

    internal ImmutableArray<RegisteredActorSnapshot> ReadActors() => [.. _views.Values.Select(actor =>
        new RegisteredActorSnapshot(actor.Registration with { Capabilities = actor.Registration.Capabilities.ToImmutableArray() }, actor.Observe()))];

    internal ImmutableArray<string> ReadReplayAdapters()
    {
        var factories = _factoryList.Select(factory => new ActorReplayAdapter(factory.Descriptor, factory.SupportsDeterministicReplay)).ToArray();
        return [.. _views.Values.Select(actor => ActorReplayAdmission.Require(actor.Registration, factories))];
    }

    internal ActorWorldSnapshot ReadSnapshot() => new(Active, _lab is not null, _preparation?.Preparing == true,
        _preparation?.Count ?? 0, _placed.Count, _runtime?.Count ?? 0, _bodies.Count, _sources.LibraryCount, _sources.AssetCount, TickCount,
        _preparation is null ? [] : [.. _preparation.Problems.Select(pair => new ActorPreparationProblem(pair.Key, pair.Value))], _native.Rac2AnimationPresented);

    internal IReadOnlyDictionary<string, string> ReadStatus() => (_preparation?.Placements ?? []).ToDictionary(
        placement => placement.Id, placement => $"{SourceIdentityWire.FormatSource(placement.Source.Game)} / class {placement.NativeClassId}: " +
            (_preparation!.Problems.GetValueOrDefault(placement.Id) ??
                (_placed.TryGetValue(placement.Id, out var instance) && _runtime?.Find(instance.Target) is { } actor
                    ? (actor.Observe().Alive ? "Active" : "Defeated — reset to replay this encounter") +
                        string.Concat(actor.Registration.Capabilities.Where(capability => capability.Admission != CapabilityAdmission.Ready)
                            .Select(capability => "\n" + capability.Reason)) : "Preparing original assets")));

    internal EquipmentChangeResult ResetPlacement(string id)
    {
        if (_runtime is null || _preparation?.Find(id) is null) return new(false, "No registered actor placement.");
        if (_placed.Remove(id, out var previous))
        {
            try { _runtime.Retire(previous); }
            finally { Unbind(previous); }
        }
        _ports.InvalidateOcclusion();
        _preparation.Retry(id);
        // Retain the original poll/status phase when resetting an individual placement.
        _ports.LabStatus(_placed.Count, _preparation.Count, _preparation.Problems);
        return new(_placed.ContainsKey(id), _preparation.Problems.GetValueOrDefault(id) ?? "Actor assets are preparing.");
    }

    private void Unbind(ActorInstanceKey instance)
    {
        _views.Remove(instance.Target);
        _ports.RetirePlayerAttack(instance);
        _ports.InvalidateOcclusion();
    }

    internal void InvalidateSources()
    {
        _sources.Clear();
        var instances = _placed.Values.ToArray();
        var failures = _runtime?.RetireActors(instances) ?? Array.Empty<Exception>();
        foreach (var instance in instances) Unbind(instance);
        _preparation?.Invalidate(); _placed.Clear();
        foreach (var error in failures) GD.PrintErr("[actors] source invalidation retirement failed after cleanup: " + error);
    }

    internal void TrackPickups(RuntimeWorldScene.DynamicObjectNode node, IEnumerable<int> pickups)
    {
        var key = Target(node);
        if (_runtime?.Find(key) is null) return;
        if (!_rewards.TryGetValue(key, out var owned)) { owned = []; _rewards.Add(key, owned); }
        owned.UnionWith(pickups);
    }

    private void ClearTransients(WeaponTargetKey key)
    {
        if (_audio.Remove(key, out var audio)) audio.StopAll();
        if (_rewards.Remove(key, out var pickups)) foreach (int id in pickups) _ports.DiscardPickup(id);
    }

    internal bool PlayRac1Sound(RuntimeWorldScene.DynamicObjectNode node, int localSound)
    {
        var key = Target(node);
        if (!_placed.Values.Any(instance => instance.Target == key)) return false;
        if (_native.PickupBank?.Sounds?.TryGetValue((node.Source.NativeClassId, localSound), out var sound) != true)
        { GD.Print($"[actors] {key}: original sound {localSound} unavailable"); return true; }
        if (!_audio.TryGetValue(key, out var audio)) { audio = new(); _audio.Add(key, audio); }
        _playbackRandom ??= Rac1PickupRandom.FromSeed(_ports.PlaybackSeed());
        var clip = sound!.SelectPitch(_playbackRandom);
        if (clip.FrameCount > 0) audio.PlayEffect(node.Root, new(clip, OBP.Runtime.Audio.RuntimeAudioCategory.SoundEffect, gain: sound.Gain, position: new(0, 0, 0)));
        return true;
    }

    internal bool HasContactBody(WeaponTargetKey key) => _bodies.ContainsKey(key);
    internal void DisableContactBody(WeaponTargetKey key)
    {
        if (_bodies.TryGetValue(key, out var body)) body.CollisionLayer = body.CollisionMask = 0;
    }

    internal bool TryMoveContactBody(WeaponTargetKey key, Vector3 position, out Vector3 moved)
    {
        moved = position;
        if (!_bodies.TryGetValue(key, out var body)) return false;
        var contact = body.MoveAndCollide(position - body.GlobalPosition);
        if (contact is not null)
        {
            Vector3 remainder = contact.GetRemainder().Slide(contact.GetNormal());
            if (remainder.LengthSquared() > 1e-8f) body.MoveAndCollide(remainder);
        }
        moved = body.GlobalPosition;
        return true;
    }

    internal void AddRayExclusions(global::Godot.Collections.Array<Rid> exclusions)
    {
        foreach (var body in _bodies.Values) if (GodotObject.IsInstanceValid(body)) exclusions.Add(body.GetRid());
    }

    internal void Reset()
    {
        var runtime = _runtime;
        try { runtime?.Dispose(); }
        finally
        {
            _runtime = null;
            foreach (var audio in _audio.Values) audio.StopAll();
            foreach (var body in _bodies.Values.Where(GodotObject.IsInstanceValid)) body.CollisionLayer = body.CollisionMask = 0;
            _audio.Clear(); _rewards.Clear(); _playbackRandom = null; _bodies.Clear();
            _preparation?.Dispose(); _preparation = null;
            _views.Clear(); _placed.Clear(); _lab = null; _factories = null; _factoryList = []; _scene = null;
            foreach (var error in runtime?.RetirementFailures ?? Array.Empty<Exception>())
                GD.PrintErr("[actors] retirement failed after lease cleanup: " + error);
        }
    }
}
