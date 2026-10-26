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
        _presses.TryGetValue(action, out var owners);
        if (strength == 0 && (owners is null || !owners.ContainsKey(lease))) return;
        float combined = Math.Max(strength, owners?.Where(pair => pair.Key != lease).Select(pair => pair.Value).DefaultIfEmpty(0).Max() ?? 0);
        // Retain the last accepted ownership state if the engine rejects this change.
        write(action, combined);
        if (strength == 0)
        {
            owners!.Remove(lease);
            if (owners.Count == 0) _presses.Remove(action);
        }
        else
        {
            if (owners is null) _presses[action] = owners = [];
            owners[lease] = strength;
        }
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
            _disposed = true;
            List<Exception>? failures = null;
            foreach (string action in _touched.ToArray())
            {
                try { owner.Set(this, action, 0); _touched.Remove(action); }
                catch (Exception error) { (failures ??= []).Add(error); }
            }
            if (failures is not null) throw new AggregateException("Logical input restoration failed.", failures);
        }
    }
}
