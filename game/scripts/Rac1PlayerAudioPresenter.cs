using System.Collections.Immutable;
using OBP.Core;
using OBP.Fusion;
using OBP.Game.Diagnostics;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Audio;

namespace OBP.Game;

internal enum Rac1PlayerAudioChannel { OneShot, Pack, ThrownWrench, Animation, Reaction }

internal interface IRac1PlayerAudioPlayback
{
    bool Play(Rac1PlayerAudioChannel channel, RuntimeAudioPlaybackIntent intent);
    bool PlayEquipment(RuntimeAudioPlaybackIntent intent);
    void Stop(Rac1PlayerAudioChannel channel, bool immediately);
    void SetPaused(bool paused);
    ImmutableArray<DiagnosticAudioVoice> ReadVoices();
}

/// <summary>The admitted bank and physical source stamp are independent of the destination.</summary>
internal sealed record Rac1PlayerAudioBinding(FusionSourceIdentity Source, int Level,
    (long Length, long Written)? FileStamp, WorldEntryRequest World);

/// <summary>
/// Owns native cue cursors, pitch randomness and world-local voice lifetimes.
/// Borrows the equipment owner's decoded bank; never decodes or copies samples.
/// Callers supply the selected player's pose and native clock, not world policy.
/// </summary>
internal sealed class Rac1PlayerAudioPresenter(IRac1PlayerAudioPlayback playback,
    Func<long> nativeTick, Action stopEquipment, Action<string> report) : IDisposable
{
    private Rac1PickupAssets.Catalogue? _assets;
    private Rac1PlayerAudioSession? _session;
    private Rac1PlayerReactionAudioSession? _reactions;
    private Rac1PickupRandom? _random;
    private int? _seed;
    private bool _trace, _traceSilence, _paused, _disposed;
    private readonly int?[] _loops = new int?[5];
    private readonly List<string> _problems = [];
    private readonly List<DiagnosticPlayerAudioEvent> _selected = [];
    internal DiagnosticPlayerAudioCollector Events { get; } = new();
    internal Rac1PlayerAudioBinding? Binding { get; private set; }
    internal bool Ready => Binding is not null && _session is not null;
    internal int? PackSound => _loops[(int)Rac1PlayerAudioChannel.Pack];
    internal int? ThrownWrenchSound => _loops[(int)Rac1PlayerAudioChannel.ThrownWrench];
    internal bool WrenchImpactAdmitted => Ready && !_paused && _session!.WrenchImpactAdmitted;
    // Equipment shares this stream, preserving the original pitch/probability order.
    internal Rac1PickupRandom Random => _random ??= Rac1PickupRandom.FromSeed(
        unchecked((uint)(_seed ?? Environment.TickCount)));

    internal void Bind(Rac1PlayerAudioBinding binding, Rac1PickupAssets.Catalogue? assets,
        int? seed, bool trace, bool traceSilence)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        binding.Source.Validate();
        if (binding.Source.Game != ObpSourceGame.Rac1 || binding.Source.Build != Rac1Authority.Primary.BuildId)
            throw new ArgumentException("Player audio requires the admitted R&C1 source/build.", nameof(binding));
        if (assets is not null && binding.FileStamp is null)
            throw new ArgumentException("Decoded player audio requires an admitted source stamp.", nameof(binding));
        RetireWorld();
        Binding = binding;
        _assets = assets;
        _seed = seed;
        _trace = trace; _traceSilence = traceSilence;
        _session = assets?.PlayerAudio is { } audio ? new(audio.Animations) : null;
        _reactions = assets?.PlayerAudio is { } reactions ? new(reactions.Animations) : null;
        _problems.Clear(); _selected.Clear(); Events.Clear();
        if (_session is null) Missing("Selected player audio bank is unavailable.");
    }

    internal void Step(Rac1PlayerAudioSession.Facts facts, long wrenchGeneration, bool suppressed, bool alive)
    {
        if (!Ready || _paused) return;
        if (suppressed || !alive) { Reset(includeReactions: alive); return; }
        var frame = _session!.Step(Binding!.Level, facts, wrenchGeneration);
        foreach (var cue in frame.OneShots) Play(cue);
        SetLoop(Rac1PlayerAudioChannel.Pack, frame.PackLoop);
        SetLoop(Rac1PlayerAudioChannel.Animation, frame.AnimationLoop);
    }

    internal void StepWrench(int action, float time, bool blending, int thrownState, bool nearCatch)
    {
        if (!Ready || _paused) return;
        var frame = _session!.StepWrench(action, time, blending, thrownState, nearCatch);
        foreach (var cue in frame.OneShots) Play(cue);
        SetLoop(Rac1PlayerAudioChannel.ThrownWrench, frame.Loop);
    }

    internal void WrenchContact(bool soft, bool world = false)
    {
        if (WrenchImpactAdmitted && _session!.WrenchContact(soft, world) is { } cue) Play(cue);
    }

    internal void StepReactions()
    {
        if (Ready && !_paused && _reactions?.Step() is int sound) Play(new(0, sound), reaction: true);
    }

    internal void NotifyDamage(int before, int after)
    {
        if (!Ready || after >= before) return;
        if (after == 0)
        {
            Reset(includeReactions: false);
            // Death stops avatar updates; release held equipment before scheduling the reaction.
            stopEquipment();
        }
        _reactions?.NotifyDamage(before, after);
    }

    internal void Play(Rac1PlayerAudioSession.Cue cue, bool reaction = false)
    {
        if (Binding is null || _paused || _disposed) return;
        Rac1CrateAudio.Sound? sound = null;
        if (cue.LevelSound) _assets?.PlayerAudio?.Footsteps.TryGetValue(cue.Sound, out sound);
        else _assets?.Sounds?.TryGetValue((cue.Class, cue.Sound), out sound);
        if (sound is null) { Missing(cue.Class, cue.Sound); return; }
        var clip = sound.SelectPitch(Random);
        if (clip.FrameCount == 0)
        {
            if (_traceSilence) Record(cue, false, DiagnosticAudioPlayback.NativeSilence);
            return;
        }
        // Listener-relative self audio is OBP policy; source gain/pitch/loops stay intact.
        if (playback.Play(reaction ? Rac1PlayerAudioChannel.Reaction : Rac1PlayerAudioChannel.OneShot,
            new(clip, RuntimeAudioCategory.SoundEffect, gain: sound.Gain))) Record(cue, false);
    }

    private void SetLoop(Rac1PlayerAudioChannel channel, int? wanted)
    {
        if (_loops[(int)channel] == wanted) return;
        Stop(channel, false);
        if (wanted is not int local || _assets?.Sounds?.TryGetValue((0, local), out var sound) != true) return;
        var clip = sound!.SelectPitch(Random);
        if (clip.FrameCount == 0) return;
        if (playback.Play(channel, new(clip, RuntimeAudioCategory.SoundEffect, loop: sound.Loop, gain: sound.Gain)))
        {
            _loops[(int)channel] = wanted;
            Record(new(0, local), true);
        }
    }

    internal bool PlayEquipmentSound(int nativeClass, int localSound, RuntimeAudioPosition position)
    {
        if (Binding is null || _paused || _disposed) return false;
        if (_assets?.Sounds?.TryGetValue((nativeClass, localSound), out var sound) != true)
        { Missing(nativeClass, localSound); return false; }
        var clip = sound!.SelectPitch(Random);
        if (clip.FrameCount == 0 || !playback.PlayEquipment(new(clip,
            RuntimeAudioCategory.SoundEffect, gain: sound.Gain, position: position))) return false;
        Record(new(nativeClass, localSound), false, equipment: true);
        return true;
    }

    private void Missing(int nativeClass, int localSound) => Missing(
        $"R&C1 {Binding?.Level} class {nativeClass} sound {localSound} is unavailable in the selected source bank.");
    private void Missing(string reason)
    {
        if (_problems.Contains(reason) || _problems.Count == 64) return;
        _problems.Add(reason); report(reason);
    }

    private void Record(Rac1PlayerAudioSession.Cue cue, bool loop,
        DiagnosticAudioPlayback outcome = DiagnosticAudioPlayback.Played, bool equipment = false,
        DiagnosticAudioTransition transition = DiagnosticAudioTransition.Started)
    {
        if (Binding is not { } binding) return;
        var item = new DiagnosticPlayerAudioEvent(binding.Source.Game, binding.Source.Build, binding.Level,
            nativeTick(), cue.Class, cue.Sound, loop, cue.LevelSound)
        {
            Playback = outcome,
            Transition = transition,
            WorldInstanceId = binding.World.WorldInstanceId,
            WorldGeneration = binding.World.Generation,
        };
        if (_trace && !equipment) Events.Record(item);
        if (outcome != DiagnosticAudioPlayback.Played || transition != DiagnosticAudioTransition.Started) return;
        if (_selected.Count == 1024) _selected.RemoveRange(0, 512);
        _selected.Add(item);
    }

    internal ImmutableArray<DiagnosticPlayerAudioEvent> ReadSelected() => [.. _selected];
    internal ImmutableArray<DiagnosticAudioVoice> ReadVoices() => playback.ReadVoices();
    internal void ResetDiagnosticRandom(uint seed) { _random = Rac1PickupRandom.FromSeed(seed); _selected.Clear(); }

    internal void SetPaused(bool paused)
    {
        // Keep cursors and voice allocation intact so resume consumes no new randomness.
        if (_paused == paused) return;
        playback.SetPaused(paused);
        _paused = paused;
    }

    private void Stop(Rac1PlayerAudioChannel channel, bool immediately)
    {
        playback.Stop(channel, immediately);
        if (_loops[(int)channel] is int sound)
            Record(new(0, sound), true, transition: DiagnosticAudioTransition.Stopped);
        _loops[(int)channel] = null;
    }

    internal void Reset(bool includeReactions = true, bool freeImmediately = false)
    {
        Stop(Rac1PlayerAudioChannel.OneShot, freeImmediately);
        Stop(Rac1PlayerAudioChannel.Pack, freeImmediately);
        Stop(Rac1PlayerAudioChannel.ThrownWrench, freeImmediately);
        Stop(Rac1PlayerAudioChannel.Animation, freeImmediately);
        _session?.Reset();
        if (includeReactions) { _reactions?.Reset(); Stop(Rac1PlayerAudioChannel.Reaction, freeImmediately); }
    }

    internal void RetireWorld(bool immediately = false)
    {
        Reset(freeImmediately: immediately);
        _session = null; _reactions = null; _assets = null; Binding = null;
        SetPaused(false);
    }

    public void Dispose()
    {
        RetireWorld(immediately: true);
        _disposed = true;
    }
}
