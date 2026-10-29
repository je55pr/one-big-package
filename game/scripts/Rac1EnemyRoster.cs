using OBP.Core;

namespace OBP.Game;

/// <summary>One R&C1 scene's contacts. Retiring a scene cannot remove a successor's equal native IDs.</summary>
internal sealed class Rac1EnemyRoster<T>(Func<T, (ObpSourceGame Source, int Instance)> identity, Action<T> retire)
    : IDisposable, IEnumerable<KeyValuePair<int, T>> where T : class
{
    private readonly Dictionary<int, T> _entries = [];
    // A removed placement remains retired even before its queued scene node is freed.
    private readonly HashSet<int> _previouslyOwned = [];
    private bool _disposed;
    internal int Count => _entries.Count;
    internal IReadOnlyCollection<T> Values => _entries.Values;
    internal T this[int index] => _entries[index];
    internal bool ContainsKey(int index) => _entries.ContainsKey(index);
    internal bool PreviouslyOwned(int index) => _previouslyOwned.Contains(index);
    internal bool TryGetValue(int index, out T value) => _entries.TryGetValue(index, out value!);
    internal bool Contains(T value)
    {
        var key = identity(value);
        return !_disposed && key.Source == ObpSourceGame.Rac1 &&
            _entries.TryGetValue(key.Instance, out var current) && ReferenceEquals(current, value);
    }

    internal void Add(int index, T value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = identity(value);
        if (key.Source != ObpSourceGame.Rac1 || key.Instance != index)
            throw new InvalidDataException("Enemy contacts require the matching source-qualified R&C1 placement.");
        _entries.Add(index, value);
        _previouslyOwned.Add(index);
    }

    internal bool Remove(int index, out T value) => _entries.Remove(index, out value!);
    internal bool Remove(T expected, out T value)
    {
        if (!Contains(expected)) { value = null!; return false; }
        return _entries.Remove(identity(expected).Instance, out value!);
    }
    public IEnumerator<KeyValuePair<int, T>> GetEnumerator() => ((IEnumerable<KeyValuePair<int, T>>)_entries.ToArray()).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    internal void Clear()
    {
        var entries = _entries.Values.ToArray();
        _entries.Clear();
        List<Exception> failures = [];
        foreach (var entry in entries)
            try { retire(entry); }
            catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Enemy contact retirement failed.", failures);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }
}
