namespace OBP.Game;

/// <summary>Bounded native-tick observations, independent of Godot voices and source samples.</summary>
internal sealed class Rac1WeaponSoundTrace
{
    private readonly List<(ulong Tick, int Class, int Sound, bool Loop)> _events = [];

    internal (ulong Tick, int Class, int Sound, bool Loop)[] Events => [.. _events];

    internal void Record(int cls, int sound, ulong tick, bool trace, bool loop = false)
    {
        if (!trace) return;
        if (_events.Count == 4096) _events.RemoveAt(0);
        _events.Add((tick, cls, sound, loop));
    }

    internal void Clear() => _events.Clear();
}
