using OBP.Core;

namespace OBP.Game;

public sealed record VerificationSourceStamp(ObpSourceGame Game, string Build, string Path, long Length, long Written);

/// <summary>One attachment's optional diagnostic hash job. An old job never writes another attachment's result.</summary>
public sealed class SourceVerificationOperation : IDisposable, IProgress<double>
{
    private readonly CancellationTokenSource _retired = new();
    private long _progress;
    private string? _outcome;
    private int _disposed;
    public VerificationSourceStamp Source { get; }
    public CancellationToken Retirement { get; }
    public double Progress => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _progress));
    public bool Matches(VerificationSourceStamp? current) => Volatile.Read(ref _disposed) == 0 && Source == current;
    public string? TakeOutcome() => Interlocked.Exchange(ref _outcome, null);

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
