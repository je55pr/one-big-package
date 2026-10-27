using Godot;
using OBP.IO;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Audio;

namespace OBP.Game;

public partial class OBPGame
{
    private FileRandomAccessReader? _rac1MusicReader;
    private Rac1NovalisMusic? _rac1Music;
    private readonly Dictionary<int, RuntimeAudioClip> _rac1MusicDecoded = [];
    private Rac1NovalisMusic.Area? _rac1MusicArea;
    private int _rac1MusicSlot = -1;
    private double _rac1MusicIntroRemaining;
    private long _rac1MusicSwitches;

    private void ResetRac1NovalisMusic()
    {
        _rac1MusicReader?.Dispose();
        _rac1MusicReader = null;
        _rac1Music = null;
        _rac1MusicDecoded.Clear();
        _rac1MusicArea = null;
        _rac1MusicSlot = -1;
        _rac1MusicIntroRemaining = 0;
        _rac1MusicSwitches = 0;
    }

    private void ConfigureRac1NovalisMusic(RuntimeWorld world)
    {
        if (world is not { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 1 }) return;
        var source = Sources.Setup.Library.Get(ObpSourceGame.Rac1)
            ?? throw new InvalidOperationException("Novalis music needs the original disc source.");
        _rac1MusicReader = new FileRandomAccessReader(source.Path);
        try
        {
            var level = Rac1DiscIndex.Read(_rac1MusicReader).Levels.Single(l => l.LevelId == 1);
            _rac1Music = Rac1NovalisMusic.Open(_rac1MusicReader, level);
            PlayRac1NovalisMusic(Rac1NovalisMusic.Area.Enemy);
            GD.Print($"[rac1-music] Novalis: {_rac1Music.Tracks.Count} original VAGp streams, initial Enemy intro");
        }
        catch
        {
            ResetRac1NovalisMusic();
            throw;
        }
    }

    private bool PlayRac1NovalisMusic(Rac1NovalisMusic.Area area)
    {
        if (_rac1Music is null || _rac1MusicArea == area) return false;
        var (intro, _) = _rac1Music.Pair(area);
        if (!PlayRac1NovalisMusicSlot(intro, loop: false)) return false;
        _rac1MusicArea = area;
        _rac1MusicIntroRemaining = _rac1Music.Tracks[intro].DurationSeconds;
        _rac1MusicSwitches++;
        GD.Print($"[rac1-music] {area} intro (slot {intro})");
        return true;
    }

    private bool PlayRac1NovalisMusicSlot(int slot, bool loop)
    {
        if (_rac1Music is null) return false;
        if (!_rac1MusicDecoded.TryGetValue(slot, out var clip))
        {
            clip = _rac1Music.Decode(slot);
            _rac1MusicDecoded[slot] = clip;
        }
        int started = _worldHost.StartLevelAudio(
            [new RuntimeAudioPlaybackIntent(clip, RuntimeAudioCategory.Music, loop: loop)]);
        if (started == 0) return false;
        _rac1MusicSlot = slot;
        return true;
    }

    private void TickRac1NovalisMusic(double delta)
    {
        if (_rac1Music is null || _rac1MusicArea is not { } area ||
            _rac1MusicIntroRemaining <= 0) return;
        _rac1MusicIntroRemaining -= Math.Max(0, delta);
        if (_rac1MusicIntroRemaining > 0) return;
        int loop = _rac1Music.Pair(area).Loop;
        if (PlayRac1NovalisMusicSlot(loop, loop: true))
            GD.Print($"[rac1-music] {area} looping original stream slot {loop}");
    }

    private void OnRac1NovalisCrankMusic(int crankIndex)
    {
        // Host zone policy, not recovered native scene/sector trigger:
        // authored entrance crank 296 leads into the cave, and linked
        // exit crank 297 returns toward the outdoor area.
        if (crankIndex == 296) PlayRac1NovalisMusic(Rac1NovalisMusic.Area.Cave);
        else if (crankIndex == 297) PlayRac1NovalisMusic(Rac1NovalisMusic.Area.Enemy);
    }
}
