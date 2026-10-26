namespace OBP.Game.Diagnostics;

/// <summary>Scoped logical action state, independently tested without the engine or desktop input.</summary>
internal sealed class DiagnosticInputActions(Func<string, bool> exists, Action<string, float> write) : IDiagnosticInputActions
{
    private readonly Func<string, bool> _exists = exists;
    private readonly Dictionary<string, Dictionary<InputLease, float>> _presses = new(StringComparer.Ordinal);
    public IDiagnosticInputLease Acquire(CancellationToken lifetime)
    {
        lifetime.ThrowIfCancellationRequested();
        return new InputLease(this, lifetime);
    }

    private void Set(InputLease lease, string action, float strength)
    {
        if (!_presses.TryGetValue(action, out var owners))
        {
            if (strength == 0) return;
            _presses[action] = owners = [];
        }
        if (strength == 0)
        {
            if (!owners.Remove(lease)) return;
        }
        else owners[lease] = strength;
        float combined = owners.Values.DefaultIfEmpty(0).Max();
        if (owners.Count == 0) _presses.Remove(action);
        write(action, combined);
    }

    private sealed class InputLease(DiagnosticInputActions owner, CancellationToken lifetime) : IDiagnosticInputLease
    {
        private readonly HashSet<string> _touched = new(StringComparer.Ordinal);
        private bool _disposed;
        public void SetAction(string action, float strength)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lifetime.ThrowIfCancellationRequested();
            if (!owner._exists(action)) throw new ArgumentException("Unknown logical input action.", nameof(action));
            if (!float.IsFinite(strength) || strength < 0 || strength > 1) throw new ArgumentOutOfRangeException(nameof(strength));
            if (strength > 0) _touched.Add(action);
            owner.Set(this, action, strength);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            List<Exception>? failures = null;
            foreach (string action in _touched)
            {
                try { owner.Set(this, action, 0); }
                catch (Exception error) { (failures ??= []).Add(error); }
            }
            _touched.Clear();
            if (failures is not null) throw new AggregateException("Logical input restoration failed.", failures);
        }
    }
}
