using System.Collections.Immutable;

namespace OBP.Game.Diagnostics;

/// <summary>Per-run clock bounds, cancellation and temporary input/resource leases. No host state/service lookup.</summary>
public sealed class DiagnosticRunContext(IDiagnosticFrameClock clock, CancellationToken cancellationToken, int maximumFrameWaits) : IDisposable
{
    private readonly List<IDisposable> _leases = [];
    private int _waits;
    private bool _disposed;
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public DiagnosticFrameSnapshot Frames
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); CancellationToken.ThrowIfCancellationRequested(); return clock.ReadFrames(); }
    }

    public async Task WaitAsync(DiagnosticFramePhase phase, int count = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        for (int i = 0; i < count; i++)
        {
            CancellationToken.ThrowIfCancellationRequested();
            if (++_waits > maximumFrameWaits) throw new TimeoutException("Diagnostic frame-wait budget exhausted.");
            await clock.NextAsync(phase, CancellationToken);
        }
    }

    public T Own<T>(T lease) where T : IDisposable
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(lease);
        _leases.Add(lease); return lease;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        foreach (var lease in Enumerable.Reverse(_leases))
        {
            try { lease.Dispose(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        _leases.Clear();
        CleanupFailures = failures is null ? Array.Empty<Exception>() : failures.AsReadOnly();
    }

    public IReadOnlyList<Exception> CleanupFailures { get; private set; } = Array.Empty<Exception>();
}

/// <summary>Cooperatively bounded execution, awaited by the caller's HostOperationScope. No detached scenario task.</summary>
public sealed class DiagnosticScenarioRunner(IDiagnosticSources sources, IDiagnosticFrameClock clock,
    IDiagnosticWorldSetup? worlds = null, TimeProvider? timeProvider = null)
{
    public async Task<DiagnosticRunResult> RunAsync(IDiagnosticScenario scenario, bool explicitlyRequested, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var definition = scenario.Definition;
        definition.Validate();
        if (!explicitlyRequested) return new(definition.Id, DiagnosticRunStatus.Unavailable, definition.FailureExitCode, "Diagnostic launch was not explicitly requested.");
        using var deadline = new CancellationTokenSource(definition.Timeout, timeProvider ?? TimeProvider.System);
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        IDiagnosticWorldLease? world = null;
        DiagnosticRunContext? run = null;
        DiagnosticRunResult result;
        try
        {
            admission.Token.ThrowIfCancellationRequested();
            RequireSources(definition, sources.ReadSources());
            if (definition.Setup != DiagnosticSetup.AssetFreeFixture)
            {
                if (worlds is null) throw new DiagnosticUnavailableException("No diagnostic world setup port is bound.");
                world = await worlds.PrepareAsync(definition.DestinationId!, definition.Setup == DiagnosticSetup.FreshWorld,
                    definition.SourceAccess, admission.Token);
                RequireSources(definition, sources.ReadSources());
            }
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(admission.Token, world?.Lifetime ?? CancellationToken.None);
            run = new(clock, lifetime.Token, definition.MaximumFrameWaits);
            try
            {
                lifetime.Token.ThrowIfCancellationRequested();
                await scenario.RunAsync(run);
                lifetime.Token.ThrowIfCancellationRequested();
                result = new(definition.Id, DiagnosticRunStatus.Passed, 0, "Scenario passed.");
            }
            catch (OperationCanceledException error) when (error.CancellationToken == lifetime.Token && lifetime.IsCancellationRequested)
            { result = Canceled(definition, deadline.IsCancellationRequested); }
        }
        catch (OperationCanceledException error) when (error.CancellationToken == admission.Token && admission.IsCancellationRequested)
        { result = Canceled(definition, deadline.IsCancellationRequested); }
        catch (DiagnosticUnavailableException error)
        { result = new(definition.Id, DiagnosticRunStatus.Unavailable, definition.FailureExitCode, error.Message, error); }
        catch (TimeoutException error)
        { result = new(definition.Id, DiagnosticRunStatus.TimedOut, definition.FailureExitCode, error.Message, error); }
        catch (Exception error)
        { result = new(definition.Id, DiagnosticRunStatus.Failed, definition.FailureExitCode, error.Message, error); }
        finally
        {
            run?.Dispose();
        }
        // Cleanup errors cannot turn a failed scenario into a pass or prevent the remaining lease from closing.
        var cleanup = run?.CleanupFailures.ToList() ?? [];
        if (world is not null)
        {
            try { await world.DisposeAsync(); }
            catch (Exception error) { cleanup.Add(error); }
        }
        if (cleanup.Count > 0)
        {
            if (result.Error is { } error) cleanup.Insert(0, error);
            result = new(definition.Id, DiagnosticRunStatus.Failed, definition.FailureExitCode, "Diagnostic cleanup failed.", new AggregateException(cleanup));
        }
        return result;
    }

    private static DiagnosticRunResult Canceled(DiagnosticScenarioDefinition definition, bool deadline) => new(definition.Id,
        deadline ? DiagnosticRunStatus.TimedOut : DiagnosticRunStatus.Canceled, definition.FailureExitCode,
        deadline ? "Diagnostic deadline expired." : "Diagnostic lifetime ended.");

    private static void RequireSources(DiagnosticScenarioDefinition definition, ImmutableArray<DiagnosticSourceSnapshot> attached)
    {
        if (attached.IsDefault) throw new DiagnosticUnavailableException("Diagnostic source observations are unavailable.");
        foreach (var required in definition.RequiredSources)
            if (!attached.Any(source => source.Game == required.Game && source.Build == required.Build && source.Available))
                throw new DiagnosticUnavailableException($"Missing or unsupported diagnostic source: {required.Game} / {required.Build}.");
    }
}
