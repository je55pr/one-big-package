namespace OBP.Godot;

/// <summary>Scoped process-outcome observation. Unrelated async flows retain ordinary quit behavior.</summary>
public static class ApplicationQuitDeferral
{
    private static readonly AsyncLocal<Scope?> Current = new();
    public static IDisposable Begin(Action<string, int> observe)
    {
        ArgumentNullException.ThrowIfNull(observe);
        var scope = new Scope(observe, Current.Value);
        Current.Value = scope;
        return scope;
    }
    public static bool TryHandle(string reason, int exitCode)
    {
        for (var scope = Current.Value; scope is not null; scope = scope.Previous)
        {
            if (!scope.Active) continue;
            scope.Observe(reason, exitCode);
            return true;
        }
        return false;
    }
    private sealed class Scope(Action<string, int> observe, Scope? previous) : IDisposable
    {
        public Action<string, int> Observe { get; } = observe;
        public Scope? Previous { get; } = previous;
        public bool Active { get; private set; } = true;
        public void Dispose()
        {
            Active = false;
            if (ReferenceEquals(Current.Value, this)) Current.Value = Previous;
        }
    }
}
