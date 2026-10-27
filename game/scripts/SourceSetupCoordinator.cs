using OBP.Core;
using OBP.IO;
using OBP.PS2;

namespace OBP.Game;

public enum SourceAccessMode { OrdinaryPlay, ExplicitDiagnostic }
public sealed record SourceSetupOperation(bool Accepted, string Message, string? Warning = null);
public interface ISourceSetupStorage : ISourceValidationFiles
{
    bool Exists(string path);
    DocumentRead<ObpSavedSource[]> ReadConfiguration(string path);
    void SaveConfiguration(ObpSourceLibrary library, string path);
}
public interface ISourceSetupConsumers
{
    string? RefreshPresentation(ObpAttachedSource? source, IRandomAccessReader? reader);
    void Invalidate(ObpSourceGame game);
    void Prepare(bool force);
}

/// <summary>One persisted source library and explicit admission/repair lifetime. Never owns the player profile or native decoders.</summary>
public sealed class SourceSetupCoordinator
{
    private sealed record Observation(ObpAttachedSource Source, SourceFileIdentity? File);
    private readonly ObpSourceDefinition[] _authorities;
    private readonly ISourceSetupStorage _storage;
    private readonly ISourceSetupConsumers _consumers;
    private readonly Action<string> _log, _error;
    private readonly Dictionary<ObpSourceGame, string> _warnings = [];
    private readonly Dictionary<ObpSourceGame, Observation> _observed = [];
    private SourceReadinessEvaluator _readiness;
    private bool _initialized;
    private bool _presentationInvalidated;
    private double _elapsed;
    public ObpSourceLibrary Library { get; private set; }
    public SourceAccessMode Access { get; }
    public string ConfigurationPath { get; }
    public bool StartupResolved { get; private set; }
    public bool RepairNeeded { get; private set; }
    public bool ProfileSuspended { get; private set; }
    public bool ConsumersInvalidated { get; private set; }
    public string? ConfigurationWarning { get; private set; }
    public IReadOnlyDictionary<ObpSourceGame, string> RestoreWarnings => _warnings;

    public SourceSetupCoordinator(IEnumerable<ObpSourceDefinition> authorities, string configurationPath, SourceAccessMode access,
        ISourceSetupStorage storage, ISourceSetupConsumers consumers, Action<string> log, Action<string> error)
    {
        if (!Enum.IsDefined(access)) throw new ArgumentOutOfRangeException(nameof(access));
        _authorities = authorities.ToArray(); Library = new(_authorities);
        if (access == SourceAccessMode.OrdinaryPlay &&
            !new[] { ObpSourceGame.Rac1, ObpSourceGame.Rac2, ObpSourceGame.Rac3 }.ToHashSet().SetEquals(_authorities.Select(source => source.Game)))
            throw new ArgumentException("Ordinary play requires the three supported source authorities.", nameof(authorities));
        ConfigurationPath = configurationPath; Access = access; _storage = storage; _consumers = consumers; _log = log; _error = error;
        _readiness = new(Library, _authorities, storage);
    }
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _consumers.RefreshPresentation(null, null);
        Restore();
        _consumers.Prepare(false);
    }
    private void Restore()
    {
        DocumentRead<ObpSavedSource[]> read;
        try { read = _storage.ReadConfiguration(ConfigurationPath); }
        catch (Exception error) { ConfigurationWarning = "Could not read remembered game sources: " + error.Message; return; }
        RepairNeeded = read.Kind is DocumentReadKind.Backup or DocumentReadKind.Rejected;
        if (read.Problem is { } problem) { ConfigurationWarning = "Remembered game sources: " + problem; _error("[sources] " + problem); }
        foreach (var entry in read.Value ?? []) Restore(entry);
    }
    private void Restore(ObpSavedSource entry)
    {
        if (!_storage.Exists(entry.Path)) { _warnings[entry.Game] = $"Remembered {entry.Game} source is missing: {entry.Path}"; return; }
        try
        {
            var reader = _storage.Open(entry.Path); using var owned = reader as IDisposable;
            var attached = Library.Restore(entry, reader);
            if (_consumers.RefreshPresentation(attached, reader) is { } warning) _warnings[entry.Game] = warning;
            RememberObservation(attached);
            _log($"[sources] restored {attached.Definition.DisplayName}: {attached.DiscSerial} · {attached.Identity.BuildId}");
        }
        catch (Exception error) { _warnings[entry.Game] = $"Could not restore {entry.Game} source '{entry.Path}': {error.Message}"; }
    }
    public bool ResolveStartup(IEnumerable<(ObpSourceGame Game, string? Path)> arguments, bool persist)
    {
        Initialize();
        if (StartupResolved) return true;
        foreach (var (game, path) in arguments)
            if (path is not null && !Attach(game, path, persist).Accepted) { SuspendProfile(); return false; }
        StartupResolved = true;
        return true;
    }
    public SourceSetupOperation Attach(ObpSourceGame game, string path, bool persist = true)
    {
        Initialize();
        var expected = Library.Definition(game);
        try
        {
            var reader = _storage.Open(path); using var owned = reader as IDisposable;
            var probe = Library.Probe(reader);
            if (probe.Definition is { } detected && detected.Game != game)
                return Reject($"That disc is {detected.DisplayName} ({probe.DiscSerial}), not {expected.DisplayName}.");
            if (!probe.Supported) return Reject(probe.Problem ?? $"The selected file is not the supported {expected.DisplayName} authority build.");
            var attached = Library.Attach(path, reader);
            if (StartupResolved && _observed.TryGetValue(game, out var previous) && previous != Observe(attached) && Access == SourceAccessMode.OrdinaryPlay)
            { SuspendProfile(); ConsumersInvalidated = true; }
            _consumers.Invalidate(game);
            string? warning = _consumers.RefreshPresentation(attached, reader);
            _consumers.Prepare(true);
            if (persist) SaveConfiguration();
            _warnings.Remove(game);
            if (warning is not null) _warnings[game] = warning;
            RememberObservation(attached);
            string message = $"✓ {attached.Definition.DisplayName} attached — {attached.DiscSerial} · {attached.Identity.BuildId}";
            _log($"[sources] {message} path={attached.Path}");
            return new(true, message, warning);
        }
        catch (Exception error) { return Reject($"Could not attach {expected.DisplayName}: {error.Message}"); }
    }
    private SourceSetupOperation Reject(string problem) { _error("[sources] " + problem); return new(false, problem); }
    public SourceSetupOperation Forget(ObpSourceGame game)
    {
        Initialize();
        if (!Library.Remove(game)) return new(false, string.Empty);
        if (Access == SourceAccessMode.OrdinaryPlay) SuspendProfile();
        _observed.Remove(game); _warnings.Remove(game);
        _consumers.Invalidate(game);
        if (game == ObpSourceGame.Rac1) _consumers.RefreshPresentation(null, null);
        _consumers.Prepare(true);
        try
        {
            SaveConfiguration();
            return new(true, $"Forgot {Library.Definition(game).DisplayName}. The retail file itself was not changed.");
        }
        catch (Exception error) { return new(false, $"Source was detached for this session, but the remembered-source config could not be saved: {error.Message}"); }
    }
    private void SaveConfiguration()
    {
        _storage.SaveConfiguration(Library, ConfigurationPath);
        RepairNeeded = false; ConfigurationWarning = null;
    }
    public SourceReadiness Readiness()
    {
        Initialize();
        var result = _readiness.Evaluate();
        foreach (var game in _authorities.Select(authority => authority.Game))
        {
            var next = Observe(Library.Get(game));
            if (_observed.TryGetValue(game, out var previous) && previous != next)
            {
                if (Access == SourceAccessMode.OrdinaryPlay) { SuspendProfile(); ConsumersInvalidated = true; }
                _consumers.Invalidate(game);
                if (game == ObpSourceGame.Rac1)
                {
                    _consumers.RefreshPresentation(null, null);
                    _presentationInvalidated = true;
                }
            }
            if (next is null) _observed.Remove(game); else _observed[game] = next;
        }
        return result;
    }
    private Observation? Observe(ObpAttachedSource? source)
    {
        if (source is null) return null;
        try { return new(source, _storage.Inspect(source.Path)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return new(source, null); }
    }
    private void RememberObservation(ObpAttachedSource source) => _observed[source.Game] = Observe(source)!;
    public bool RequireOrdinary(out SourceReadiness readiness)
    {
        Initialize();
        if (Access == SourceAccessMode.ExplicitDiagnostic) { readiness = new([]); return true; }
        readiness = Readiness();
        if (!readiness.Ready || RepairNeeded || ConsumersInvalidated) { SuspendProfile(); return false; }
        ProfileSuspended = false;
        return true;
    }
    public SourceSetupOperation Complete()
    {
        if (Access == SourceAccessMode.ExplicitDiagnostic) return new(true, string.Empty);
        if (!Readiness().Ready) return new(false, "Complete trilogy setup to continue.");
        try
        {
            if (RepairNeeded) SaveConfiguration();
            if (_presentationInvalidated && Library.Get(ObpSourceGame.Rac1) is { } source)
            {
                var reader = _storage.Open(source.Path); using var owned = reader as IDisposable;
                if (_consumers.RefreshPresentation(source, reader) is { } warning) _warnings[source.Game] = warning;
                _presentationInvalidated = false;
            }
            if (ConsumersInvalidated) _consumers.Prepare(true);
            ConsumersInvalidated = false;
            return new(true, string.Empty);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        { return new(false, "Could not repair the remembered configuration: " + error.Message); }
    }
    public bool Due(double delta, bool ordinarySurface)
    {
        if (Access == SourceAccessMode.ExplicitDiagnostic || !StartupResolved || !ordinarySurface) return false;
        _elapsed += delta;
        if (_elapsed < 1) return false;
        _elapsed = 0; return true;
    }
    public void SuspendProfile() => ProfileSuspended = true;
    internal void ReplaceDiagnosticLibrary(ObpSourceLibrary library)
    {
        if (Access != SourceAccessMode.ExplicitDiagnostic) throw new InvalidOperationException("Only an explicitly requested diagnostic can replace its isolated source library.");
        Library = library; _readiness = new(library, _authorities, _storage); _observed.Clear();
        foreach (var source in library.Attached.Values) RememberObservation(source);
    }
}
