using System.Collections.Immutable;
using OBP.Runtime.Effects;

namespace OBP.Fusion;

public readonly record struct EffectHandle(string WorldInstanceId, long LeaseGeneration, long ResetGeneration, long Sequence);
public enum EffectAdmission { Ready, AlreadyConsumed, StaleLease, RetiredOwner, Unavailable, CapacityExceeded, Disposed }
public sealed record EffectRequest(string WorldInstanceId, long LeaseGeneration, long ResetGeneration, long Sequence,
    RuntimeEffectRecipeKey Recipe, uint Seed, RuntimeEffectPose Pose, int EmissionTicks, ActorInstanceKey? AttachedActor = null);
public sealed record EffectAdmissionResult(EffectAdmission Admission, EffectHandle? Handle, RuntimeEffectProblem? Problem = null);
public sealed record EffectObservation(EffectHandle Handle, RuntimeEffectRecipeKey Recipe, RuntimeEffectState State);
public sealed record EffectDraw(EffectHandle Handle, RuntimeEffectFrame Frame);

/// <summary>Scene-local admission, clocks and lifetime. Owns no source-native rules or player configuration.</summary>
public sealed class EffectWorldSession : IDisposable
{
    private sealed class Entry(EffectRequest request, IRuntimeEffectFactory factory, IRuntimeEffectInstance native)
    {
        public EffectRequest Request { get; } = request;
        public IRuntimeEffectFactory Factory { get; } = factory;
        public IRuntimeEffectInstance Native { get; } = native;
        public int EmissionRemaining = request.EmissionTicks;
        public double Accumulator;
        public bool OwnerRetired;
    }
    private readonly EffectRecipeRegistry _registry;
    private readonly Func<ActorInstanceKey, bool> _actorAlive;
    private readonly Dictionary<EffectHandle, Entry> _instances = [];
    private readonly int _maximumInstances;
    private long _lastSequence;
    private bool _disposed;
    public string WorldInstanceId { get; }
    public long LeaseGeneration { get; }
    public long ResetGeneration { get; private set; }
    public RuntimeEffectProblem? LastProblem { get; private set; }
    public int Count => _instances.Count;

    public EffectWorldSession(string worldInstanceId, long leaseGeneration, EffectRecipeRegistry registry,
        Func<ActorInstanceKey, bool>? actorAlive = null, int maximumInstances = 64)
    {
        if (string.IsNullOrWhiteSpace(worldInstanceId) || leaseGeneration < 0) throw new ArgumentException("Invalid world effect lease.");
        WorldInstanceId = worldInstanceId; LeaseGeneration = leaseGeneration; _registry = registry;
        _actorAlive = actorAlive ?? (_ => false);
        _maximumInstances = maximumInstances > 0 ? maximumInstances : throw new ArgumentOutOfRangeException(nameof(maximumInstances));
    }
    public EffectAdmissionResult Start(EffectRequest request)
    {
        if (_disposed) return new(EffectAdmission.Disposed, null);
        if (request.WorldInstanceId != WorldInstanceId || request.LeaseGeneration != LeaseGeneration || request.ResetGeneration != ResetGeneration)
            return new(EffectAdmission.StaleLease, null);
        request.Pose.Validate(); request.Recipe.Validate();
        if (request.Sequence < 1 || request.EmissionTicks is < 1 or > 36000) throw new ArgumentException("Invalid effect request bounds.");
        var handle = new EffectHandle(WorldInstanceId, LeaseGeneration, ResetGeneration, request.Sequence);
        if (request.Sequence <= _lastSequence) return new(EffectAdmission.AlreadyConsumed, _instances.ContainsKey(handle) ? handle : null);
        if (_instances.Count >= _maximumInstances) return new(EffectAdmission.CapacityExceeded, null);
        if (request.AttachedActor is { } actor && !OwnerAlive(actor)) return new(EffectAdmission.RetiredOwner, null);
        var problem = _registry.Resolve(request.Recipe, out var factory);
        if (problem is not null) { LastProblem = problem; return new(EffectAdmission.Unavailable, null, problem); }
        if (factory!.MaximumEmissionTicks is { } maximum && request.EmissionTicks > maximum)
            return new(EffectAdmission.Unavailable, null, new(RuntimeEffectAvailability.UnsupportedContext, $"This recovered recipe admits at most {maximum} emission ticks."));
        IRuntimeEffectInstance native;
        try { native = factory.Create(request.Seed, request.Pose); }
        catch (NotSupportedException error) { return new(EffectAdmission.Unavailable, null, new(RuntimeEffectAvailability.UnsupportedContext, error.Message)); }
        catch (InvalidDataException error) { return new(EffectAdmission.Unavailable, null, new(RuntimeEffectAvailability.MissingAssets, error.Message)); }
        _instances.Add(handle, new(request, factory, native));
        _lastSequence = request.Sequence; LastProblem = null; return new(EffectAdmission.Ready, handle);
    }
    public bool SetPose(EffectHandle handle, RuntimeEffectPose pose)
    {
        if (!_instances.TryGetValue(handle, out var entry) || entry.OwnerRetired) return false;
        pose.Validate(); entry.Native.SetPose(pose); return true;
    }
    public bool Stop(EffectHandle handle)
    {
        if (!_instances.TryGetValue(handle, out var entry)) return false;
        entry.EmissionRemaining = 0; entry.Native.StopEmission(); return true;
    }
    public void Advance(double seconds, RuntimeEffectView view, bool paused = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > 1) throw new ArgumentOutOfRangeException(nameof(seconds));
        view.Validate(); if (paused) return;
        foreach (var (handle, entry) in _instances.ToArray())
        {
            var problem = _registry.Resolve(entry.Request.Recipe, out var factory);
            if (problem is not null || !ReferenceEquals(factory, entry.Factory))
            { LastProblem = problem ?? new(RuntimeEffectAvailability.MissingAssets, "The effect registration changed."); Remove(handle, entry); continue; }
            if (!entry.OwnerRetired && entry.Request.AttachedActor is { } actor && !OwnerAlive(actor))
            { entry.OwnerRetired = true; entry.EmissionRemaining = 0; entry.Native.RetireOwner(); }
            entry.Accumulator += seconds * entry.Factory.NativeTicksPerSecond;
            while (entry.Accumulator + 1e-9 >= 1)
            {
                if (entry.EmissionRemaining == 0) entry.Native.StopEmission();
                entry.Native.Step(view); entry.Accumulator = Math.Max(0, entry.Accumulator - 1);
                if (entry.EmissionRemaining > 0) entry.EmissionRemaining--;
            }
            if ((entry.EmissionRemaining == 0 || !entry.Native.State.Emitting) && entry.Native.State.ParticleCount == 0) Remove(handle, entry);
        }
    }
    public ImmutableArray<EffectObservation> Capture() => [.. _instances.Select(p => new EffectObservation(p.Key, p.Value.Request.Recipe, p.Value.Native.State))];
    public ImmutableArray<EffectDraw> CaptureDraw(RuntimeEffectView view)
    {
        view.Validate(); return [.. _instances.Select(p => new EffectDraw(p.Key, p.Value.Native.Capture(view)))];
    }
    public IReadOnlyList<RuntimeEffectTexture> Textures => _instances.Values.SelectMany(e => e.Factory.Textures).DistinctBy(t => t.Source).ToArray();
    private bool OwnerAlive(ActorInstanceKey actor) => actor.WorldInstanceId == WorldInstanceId && actor.Generation == LeaseGeneration && _actorAlive(actor);
    private void Remove(EffectHandle handle, Entry entry) { _instances.Remove(handle); entry.Native.Dispose(); }
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var entry in _instances.Values) entry.Native.Dispose();
        _instances.Clear(); _lastSequence = 0; ResetGeneration = checked(ResetGeneration + 1); LastProblem = null;
    }
    public void Dispose()
    {
        if (_disposed) return;
        Reset(); _disposed = true;
    }
}
