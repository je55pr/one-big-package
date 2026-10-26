namespace OBP.Game.Diagnostics;

/// <summary>Scoped logical action state, independently tested without the engine or desktop input.</summary>
internal sealed class DiagnosticInputActions(Func<string, bool> exists, Func<string, float> read, Action<string, float> write) : IDiagnosticInputActions
{
    public IDiagnosticInputLease Acquire(CancellationToken lifetime)
    {
        lifetime.ThrowIfCancellationRequested();
        return new InputLease(exists, read, write, lifetime);
    }

    private sealed class InputLease(Func<string, bool> exists, Func<string, float> read, Action<string, float> write,
        CancellationToken lifetime) : IDiagnosticInputLease
    {
        private readonly Dictionary<string, float> _previous = new(StringComparer.Ordinal);
        private bool _disposed;
        public void SetAction(string action, float strength)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lifetime.ThrowIfCancellationRequested();
            if (!exists(action)) throw new ArgumentException("Unknown logical input action.", nameof(action));
            if (!float.IsFinite(strength) || strength < 0 || strength > 1) throw new ArgumentOutOfRangeException(nameof(strength));
            _previous.TryAdd(action, read(action));
            write(action, strength);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            List<Exception>? failures = null;
            foreach (var (action, strength) in _previous)
            {
                try { write(action, strength); }
                catch (Exception error) { (failures ??= []).Add(error); }
            }
            _previous.Clear();
            if (failures is not null) throw new AggregateException("Logical input restoration failed.", failures);
        }
    }
}
