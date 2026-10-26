namespace OBP.Game.Diagnostics;

/// <summary>Ownership of simulated in-application key/button events. It never reads or re-injects hardware state.</summary>
public sealed class DiagnosticPhysicalInput(Action<DiagnosticPhysicalKey, bool> key, Action<DiagnosticPhysicalButton, bool> button) : IDiagnosticPhysicalInput
{
    private readonly Action<DiagnosticPhysicalKey, bool> _key = key;
    private readonly Action<DiagnosticPhysicalButton, bool> _button = button;
    private readonly Dictionary<DiagnosticPhysicalKey, HashSet<Lease>> _keys = [];
    private readonly Dictionary<DiagnosticPhysicalButton, HashSet<Lease>> _buttons = [];
    public IDiagnosticPhysicalInputLease Acquire() => new Lease(this);
    private static void Set<T>(Dictionary<T, HashSet<Lease>> owners, Lease lease, T value, bool pressed, Action<T, bool> write) where T : notnull
    {
        owners.TryGetValue(value, out var leases);
        if (!pressed && (leases is null || !leases.Contains(lease))) { return; }
        bool effective = pressed || leases!.Any(item => item != lease);
        write(value, effective);
        if (pressed) { if (leases is null) { owners[value] = leases = []; } leases.Add(lease); }
        else { leases!.Remove(lease); if (leases.Count == 0) { owners.Remove(value); } }
    }
    private sealed class Lease(DiagnosticPhysicalInput owner) : IDiagnosticPhysicalInputLease
    {
        private bool _disposed;
        public void SetKey(DiagnosticPhysicalKey value, bool pressed)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Set(owner._keys, this, value, pressed, owner._key);
        }
        public void SetButton(DiagnosticPhysicalButton value, bool pressed)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Set(owner._buttons, this, value, pressed, owner._button);
        }
        public void Dispose()
        {
            _disposed = true;
            List<Exception>? failures = null;
            foreach (var value in owner._keys.Where(pair => pair.Value.Contains(this)).Select(pair => pair.Key).ToArray())
                try { Set(owner._keys, this, value, false, owner._key); } catch (Exception error) { (failures ??= []).Add(error); }
            foreach (var value in owner._buttons.Where(pair => pair.Value.Contains(this)).Select(pair => pair.Key).ToArray())
                try { Set(owner._buttons, this, value, false, owner._button); } catch (Exception error) { (failures ??= []).Add(error); }
            if (failures is not null) { throw new AggregateException("Physical diagnostic input restoration failed.", failures); }
        }
    }
}
