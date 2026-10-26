using System.Collections.Immutable;
using OBP.Core;

namespace OBP.Game.Diagnostics;

public enum DiagnosticAudioPlayback { Played, NativeSilence }
public sealed record DiagnosticPlayerAudioEvent(ObpSourceGame Source, string Build, int Level, long NativeTick,
    int Class, int Sound, bool Loop, bool LevelSound)
{
    public DiagnosticAudioPlayback Playback { get; init; } = DiagnosticAudioPlayback.Played;
}
public interface IDiagnosticPlayerAudioEvents
{
    ImmutableArray<DiagnosticPlayerAudioEvent> Read();
    ImmutableArray<DiagnosticPlayerAudioEvent> ReadOutcomes();
    void Clear();
}

/// <summary>Bounded, source-qualified per-fixture evidence. Overflow invalidates evidence rather than silently dropping a required cue.</summary>
public sealed class DiagnosticPlayerAudioCollector(int capacity = 4096) : IDiagnosticPlayerAudioEvents
{
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly List<DiagnosticPlayerAudioEvent> _events = [];
    private bool _overflow;
    public void Record(DiagnosticPlayerAudioEvent cue)
    {
        if (_events.Count == _capacity) { _overflow = true; return; }
        _events.Add(cue);
    }
    public ImmutableArray<DiagnosticPlayerAudioEvent> Read() => [.. ReadOutcomes().Where(cue => cue.Playback == DiagnosticAudioPlayback.Played)];
    public ImmutableArray<DiagnosticPlayerAudioEvent> ReadOutcomes() => _overflow
        ? throw new DiagnosticUnavailableException("Player audio evidence exceeded the fixture's bounded collector.") : [.. _events];
    public void Clear() { _events.Clear(); _overflow = false; }
}
