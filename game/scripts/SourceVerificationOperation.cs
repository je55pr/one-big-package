using OBP.Core;

namespace OBP.Game;

public sealed record VerificationSourceStamp(ObpSourceGame Game, string Build, string Path, long Length, long Written);
public sealed record VerificationUpdate(double Progress, string? Outcome, bool SourceChanged);

/// <summary>One attachment's optional diagnostic hash job. An old job never writes another attachment's result.</summary>
public sealed class SourceVerificationOperation : IDisposable, IProgress<double>
{
    private readonly CancellationTokenSource _retired = new();
    private long _progress;
    private string? _outcome;
    private int _disposed;
    private ulong? _nextSourceCheck;
    public VerificationSourceStamp Source { get; }
    public CancellationToken Retirement { get; }
    public double Progress => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _progress));
    public bool Matches(VerificationSourceStamp? current) => Volatile.Read(ref _disposed) == 0 && Source == current;
    public string? TakeOutcome() => Interlocked.Exchange(ref _outcome, null);

    /// <summary>Host-thread polling: limit disk metadata reads, and always validate a completed result before publication.</summary>
    public VerificationUpdate Poll(ulong milliseconds, Func<VerificationSourceStamp?> currentSource)
    {
        if (Volatile.Read(ref _disposed) != 0) return new(Progress, null, true);
        string? outcome = TakeOutcome();
        if (outcome is not null || !_nextSourceCheck.HasValue || milliseconds >= _nextSourceCheck)
        {
            _nextSourceCheck = milliseconds + 250; // OBP diagnostic policy; unrelated to native update clocks.
            if (!Matches(currentSource())) return new(Progress, null, true);
        }
        return new(Progress, outcome, false);
    }

    public SourceVerificationOperation(VerificationSourceStamp source)
    {
        Source = source;
        Retirement = _retired.Token;
    }

    public async Task RunAsync(Func<IProgress<double>, CancellationToken, string> verify, CancellationToken cancellationToken)
    {
        string outcome = await Task.Run(() => verify(this, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _outcome, outcome);
    }

    public void Report(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(value));
        Interlocked.Exchange(ref _progress, BitConverter.DoubleToInt64Bits(value));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _retired.Cancel();
        _retired.Dispose();
    }
}
