using OBP.Core;

namespace OBP.Game;

/// <summary>Optional attachment hashing has its own cancellation/drain scope; it never owns startup or capture work.</summary>
internal sealed class SourceVerificationCoordinator(Func<ObpSourceGame, VerificationSourceStamp?> current,
    Func<VerificationSourceStamp, IProgress<double>, CancellationToken, string> verify,
    Func<string> summary, Action<string, bool> publish) : IDisposable
{
    private readonly HostOperationScope _operations = new();
    private SourceVerificationOperation? _active;
    private string? _activeJob;
    private long _nextJob;
    internal int PendingCount => _operations.PendingCount;

    internal void Start(ObpSourceGame game)
    {
        Cancel(game);
        var stamp = current(game) ?? throw new InvalidOperationException("Verification requires its attached source.");
        _active?.Dispose();
        var operation = _active = new(stamp);
        _activeJob = "source-verification-" + ++_nextJob;
        _operations.Start(_activeJob, token => operation.RunAsync((progress, cancellation) =>
            verify(stamp, progress, cancellation), token), operation.Retirement);
    }

    internal void Cancel(ObpSourceGame game)
    {
        if (_active?.Source.Game != game) return;
        _active.Dispose(); _active = null; _activeJob = null;
    }

    internal void Tick(ulong milliseconds)
    {
        foreach (var failure in _operations.TakeFailures())
            if (!failure.Superseded && failure.Name == _activeJob)
            {
                if (_active is { } active) Cancel(active.Source.Game);
                publish("Optional source verification failed: " + failure.Error.Message, true);
            }
        if (_active is not { } operation) return;
        var update = operation.Poll(milliseconds, () => current(operation.Source.Game));
        if (update.SourceChanged)
        {
            Cancel(operation.Source.Game);
            publish("Verification canceled because the attached source changed.", false);
        }
        else if (update.Outcome is { } outcome)
        {
            Cancel(operation.Source.Game);
            publish($"{summary()}   —   {outcome}", outcome.StartsWith('⚠'));
        }
        else publish($"{summary()}   —   verifying disc SHA-256… ({update.Progress * 100:0}%)", false);
    }

    internal Task StopAsync()
    {
        _active?.Dispose(); _active = null; _activeJob = null;
        return _operations.StopAsync();
    }
    public void Dispose() => _ = StopAsync();
}
