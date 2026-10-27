namespace OBP.Game.Diagnostics;

/// <summary>Ownership of simulated in-application key/button events. It never reads or re-injects hardware state.</summary>
public sealed class DiagnosticPhysicalInput(Action<DiagnosticPhysicalKey, bool> key, Action<DiagnosticPhysicalButton, bool> button,
    Action<DiagnosticPhysicalAxis, float>? axis = null) : IDiagnosticPhysicalInput
{
    private readonly Action<DiagnosticPhysicalKey, bool> _key = key;
    private readonly Action<DiagnosticPhysicalButton, bool> _button = button;
    private readonly Dictionary<DiagnosticPhysicalKey, HashSet<Lease>> _keys = [];
    private readonly Dictionary<DiagnosticPhysicalButton, HashSet<Lease>> _buttons = [];
    private readonly HashSet<DiagnosticPhysicalKey> _dirtyKeys = [];
    private readonly HashSet<DiagnosticPhysicalButton> _dirtyButtons = [];
    private readonly Dictionary<DiagnosticPhysicalAxis, List<(Lease Owner, float Value)>> _axes = [];
    private readonly HashSet<DiagnosticPhysicalAxis> _dirtyAxes = [];
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "S1244", Justification = "Axis ownership forwards the exact latest requested strength; tolerances would discard intentional input changes.")]
    private void SetAxis(Lease lease, DiagnosticPhysicalAxis value, float strength)
    {
        if (!_axes.TryGetValue(value, out var owners)) _axes[value] = owners = [];
        if (strength == 0 && !owners.Any(entry => entry.Owner == lease) && !_dirtyAxes.Contains(value)) return;
        var write = axis ?? throw new DiagnosticUnavailableException("Simulated stick axes are not bound.");
        float previous = owners.Count == 0 ? 0 : owners[^1].Value;
        owners.RemoveAll(entry => entry.Owner == lease);
        if (strength != 0) owners.Add((lease, strength));
        float effective = owners.Count == 0 ? 0 : owners[^1].Value;
        if (effective.Equals(previous) && !_dirtyAxes.Contains(value)) return;
        _dirtyAxes.Add(value);
        write(value, effective);
        _dirtyAxes.Remove(value);
    }
    public IDiagnosticPhysicalInputLease Acquire() => new Lease(this);
    private static void Set<T>(Dictionary<T, HashSet<Lease>> owners, HashSet<T> dirty, Lease lease, T value, bool pressed, Action<T, bool> write) where T : notnull
    {
        owners.TryGetValue(value, out var leases);
        if (!pressed && (leases is null || !leases.Contains(lease)) && !dirty.Contains(value)) { return; }
        bool previous = leases is { Count: > 0 };
        // A failed release cannot leave a dead lease counted as a live input owner.
        if (!pressed) { leases?.Remove(lease); }
        bool effective = pressed || leases is { Count: > 0 };
        if (effective != previous || dirty.Contains(value))
        {
            dirty.Add(value); // Preserve uncertainty so disposal/new owners can retry after a failed engine write.
            write(value, effective);
            dirty.Remove(value);
        }
        if (pressed) { if (leases is null) { owners[value] = leases = []; } leases.Add(lease); }
        else if (leases is { Count: 0 }) { owners.Remove(value); }
    }
    private sealed class Lease(DiagnosticPhysicalInput owner) : IDiagnosticPhysicalInputLease
    {
        private bool _disposed;
        private readonly HashSet<DiagnosticPhysicalKey> _touchedKeys = [];
        private readonly HashSet<DiagnosticPhysicalButton> _touchedButtons = [];
        private readonly HashSet<DiagnosticPhysicalAxis> _touchedAxes = [];
        public void SetAxis(DiagnosticPhysicalAxis axis, float value)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!Enum.IsDefined(axis)) throw new ArgumentOutOfRangeException(nameof(axis));
            if (!float.IsFinite(value) || value is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(value));
            if (value != 0) _touchedAxes.Add(axis);
            owner.SetAxis(this, axis, value);
        }
        public void SetKey(DiagnosticPhysicalKey value, bool pressed)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (pressed) { _touchedKeys.Add(value); }
            Set(owner._keys, owner._dirtyKeys, this, value, pressed, owner._key);
        }
        public void SetButton(DiagnosticPhysicalButton value, bool pressed)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (pressed) { _touchedButtons.Add(value); }
            Set(owner._buttons, owner._dirtyButtons, this, value, pressed, owner._button);
        }
        public void Dispose()
        {
            _disposed = true;
            List<Exception>? failures = null;
            foreach (var value in _touchedKeys.ToArray())
                try { Set(owner._keys, owner._dirtyKeys, this, value, false, owner._key); _touchedKeys.Remove(value); } catch (Exception error) { (failures ??= []).Add(error); }
            foreach (var value in _touchedButtons.ToArray())
                try { Set(owner._buttons, owner._dirtyButtons, this, value, false, owner._button); _touchedButtons.Remove(value); } catch (Exception error) { (failures ??= []).Add(error); }
            foreach (var value in _touchedAxes.ToArray())
                try { owner.SetAxis(this, value, 0); _touchedAxes.Remove(value); } catch (Exception error) { (failures ??= []).Add(error); }
            if (failures is not null) { throw new AggregateException("Physical diagnostic input restoration failed.", failures); }
        }
    }
}
