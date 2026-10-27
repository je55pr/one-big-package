using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.IO;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Effects;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>One world's effect preparation, admission and presentation; owns neither player state nor native gameplay.</summary>
internal sealed class WorldEffectHost : IDisposable, IProgress<string>
{
    private readonly Node3D _parent;
    private readonly Func<Camera3D?> _camera;
    private readonly Func<IReadOnlyList<ObpAttachedSource>> _sources;
    private readonly Func<ActorInstanceKey, bool> _actorAlive;
    private readonly Func<NVector3> _placementOffset;
    private readonly Func<bool> _suspended;
    private readonly Action<string>? _recordOperation;
    private readonly Func<bool> _commandAllowed;
    private readonly EffectNativeClockDriver _driver;
    private readonly CancellationTokenSource _cancellation = new();
    private Task<EffectSourceData>? _preparation;
    private readonly EffectExperimentDefinition _definition;
    private readonly string _worldId;
    private readonly long _generation;
    private EffectSourceData? _data;
    private EffectWorldSession? _session;
    private EffectSceneView? _view;
    private string _status = "Native effects idle. Start from PAUSE > DEVELOPER.";
    private bool _disposed;
    private bool _preparationFailed;
    private bool _startWhenReady;
    public bool Ready => _session is not null;
    public string? Failure => _preparationFailed ? Status : null;
    public bool Preparing => _preparation is { IsCompleted: false };
    public string ReplayIdentity => _definition.Id + ":" + _definition.Fingerprint;
    public string Status => Volatile.Read(ref _status);
    public int LiveInstances => _session?.Count ?? 0;
    public int RenderedParticles => _view?.RenderedCount ?? 0;
    public int TextureCount => _view?.TextureCount ?? 0;
    public int BatchCount => _view?.BatchCount ?? 0;
    public EffectExperimentDefinition Definition => _definition;
    public ImmutableArray<EffectObservation> Read() => _session?.Capture() ?? [];
    public ImmutableArray<EffectDraw> ReadDraw() => _session?.CaptureDraw(CameraView()) ?? [];
    public EffectHandle[] Handles => Read().Select(e => e.Handle).ToArray();
    public bool Stop(EffectHandle handle) => _session?.Stop(handle) == true;
    public NVector3 PresentationCenter => _definition.Effects.Select(e => e.Pose.Position).Aggregate(NVector3.Zero, (a, b) => a + b) / _definition.Effects.Count + _placementOffset();
    public bool IsDisposed => _disposed;

    public WorldEffectHost(Node3D parent, string worldId, long generation, Func<Camera3D?> camera,
        Func<IReadOnlyList<ObpAttachedSource>> sources, Func<ActorInstanceKey, bool> actorAlive, Func<NVector3> placementOffset,
        Func<bool>? suspended = null, Action<string>? recordOperation = null, Func<bool>? commandAllowed = null)
    {
        _parent = parent; _worldId = worldId; _generation = generation; _camera = camera; _sources = sources;
        _actorAlive = actorAlive; _placementOffset = placementOffset;
        _suspended = suspended ?? (() => false); _recordOperation = recordOperation;
        _commandAllowed = commandAllowed ?? (() => true);
        _definition = EffectExperimentDefinition.Parse(global::Godot.FileAccess.GetFileAsString("res://data/effects_experiment.json"));
        _driver = new() { Name = "SourceEffectClock", ProcessPhysicsPriority = 5000, Advance = Physics };
        parent.AddChild(_driver);
    }
    public void Prepare()
    {
        if (_disposed || _preparation is not null || _preparationFailed) return;
        var snapshot = _sources().ToArray(); var token = _cancellation.Token;
        var cache = new FileVerificationCache(ProjectSettings.GlobalizePath("user://source-verification"));
        Report("Preparing separately qualified native effect banks…");
        _preparation = Task.Run(() => EffectSourceData.Load(snapshot, cache, this, token), token);
    }
    public void Report(string value) => Volatile.Write(ref _status, value);
    public void PrepareTick()
    {
        if (_disposed || _preparationFailed || _data is not null || _preparation is not { IsCompleted: true }) return;
        try
        {
            _data = _preparation.GetAwaiter().GetResult();
            _session = new(_worldId, _generation, _data.Registry, _actorAlive); _view = new(_parent);
            Report(_data.Problems.Count == 0 ? "Native effects ready. Start or reset from PAUSE > DEVELOPER." : string.Join(" ", _data.Problems.Values.Select(p => p.Reason)));
            if (_startWhenReady) { _startWhenReady = false; Control("start"); }
        }
        catch (Exception error)
        {
            Report("Native effect preparation failed: " + error.GetType().Name);
            _preparationFailed = true;
        }
    }
    public RuntimeEffectControlResult Control(string operation)
    {
        if (_disposed) return new(false, "The effect world was retired.");
        if (!_commandAllowed()) return new(false, "This recording excludes effects. Use --record-effects with --record-playtest to include them.");
        if (operation == "reset")
        {
            _startWhenReady = false; _session?.Reset(); _view?.Clear(); Report("Effects reset. Native clocks, seeds and handles retired; other experiment participants are unchanged.");
            _recordOperation?.Invoke(operation);
            return new(true, Status);
        }
        if (operation != "start") return new(false, "Unknown effect experiment operation.");
        Prepare();
        if (_preparationFailed) return new(false, Status);
        if (_session is null || _data is null)
        { _startWhenReady = true; return new(true, "Preparing native effects; the experiment will start when ready."); }
        ValidateSources();
        foreach (var entry in _definition.Effects)
        {
            var problem = _data.Registry.Resolve(entry.Key, out var factory);
            if (problem is not null) { Report(problem.Reason); return new(false, Status); }
            if (factory!.MaximumEmissionTicks is { } maximum && entry.EmissionTicks > maximum)
            { Report("The effect experiment exceeds a recovered caller's emission window."); return new(false, Status); }
        }
        _session.Reset(); _view!.Clear();
        foreach (var request in _definition.Requests(_session, _placementOffset()))
        {
            var result = _session.Start(request);
            if (result.Admission != EffectAdmission.Ready)
            { _session.Reset(); _view.Clear(); Report(result.Problem?.Reason ?? result.Admission.ToString()); return new(false, Status); }
        }
        Report("Six native instances active: two Novalis owners, two Museum presets, charge and charged-shot glow. Godot presentation retains bounded native draw coverage.");
        _recordOperation?.Invoke(operation);
        return new(true, Status);
    }
    public void Physics(double delta)
    {
        if (_disposed || _session is null || _session.Count == 0 || _suspended()) return;
        ValidateSources();
        _session.Advance(Math.Min(delta, 1), CameraView(), _parent.GetTree().Paused);
        if (_session.LastProblem is { } problem) Report(problem.Reason);
    }
    public void Render()
    {
        if (_disposed || _session is null || _view is null) return;
        if (_session.Count == 0)
        {
            if (_view.DrawCalls != 0 || _view.LightCount != 0 || _view.TextureCount != 0 || _view.BatchCount != 0) _view.Clear();
            return;
        }
        var view = CameraView(); _view.Render(_session.CaptureDraw(view).Select(d => d.Frame), _session.Textures, view.Aspect);
    }
    public RuntimeEffectView CameraView()
    {
        var camera = _camera() ?? throw new InvalidOperationException("Effect rendering requires the active scene camera.");
        var basis = camera.GlobalBasis.Orthonormalized(); float aspect = camera.GetViewport().GetVisibleRect().Size.Aspect();
        var projection = camera.GetCameraProjection();
        float tanY = 1 / MathF.Abs(projection.Y.Y), tanX = 1 / MathF.Abs(projection.X.X);
        return new(Runtime(camera.GlobalPosition), Runtime(-basis.Z), Runtime(basis.X), Runtime(basis.Y), tanX, tanY, 1 / tanY, aspect);
    }
    public static NVector3 Runtime(global::Godot.Vector3 value) => new(-value.X, value.Y, value.Z);
    public void AddCheckpoint(SortedDictionary<string, PlaytestValue> values)
    {
        values.Add("effects.definition", new(Text: ReplayIdentity));
        foreach (var effect in Read())
        {
            var s = effect.State;
            values.Add("effect." + effect.Handle.Sequence, new([effect.Handle.ResetGeneration, s.NativeTick, s.ParticleCount, s.Emitting ? 1 : 0, s.Attempted, s.Emitted, s.RandomSamples]));
        }
    }
    private void ValidateSources()
    {
        if (_data is null) return;
        var current = _sources();
        foreach (var (game, stamp) in _data.Stamps)
        {
            var source = current.SingleOrDefault(s => s.Game == game);
            bool same;
            try { same = source is not null && EffectSourceData.ReadStamp(source.Path) == stamp; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { same = false; }
            if (same) continue;
            var problem = new RuntimeEffectProblem(RuntimeEffectAvailability.MissingSource, $"{SourceIdentityWire.FormatSource(game)} effect source changed or detached. Re-enter after attaching it.");
            _data.Registry.Source(game, source?.Identity.BuildId ?? "detached", problem);
        }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _cancellation.Cancel();
        _driver.SetPhysicsProcess(false); _driver.QueueFree();
        try { _session?.Dispose(); }
        finally
        {
            try { _view?.Dispose(); }
            finally
            {
                _session = null; _view = null; _data = null;
                if (_preparation is null) _cancellation.Dispose();
                else _ = _preparation.ContinueWith(task => { _ = task.Exception; _cancellation.Dispose(); }, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }
}
