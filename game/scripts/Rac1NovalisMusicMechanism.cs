using Godot;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Audio;

namespace OBP.Game;

/// <summary>One Novalis lease's decoded source music bank and area transitions.</summary>
internal sealed class Rac1NovalisMusicMechanism(Rac1AuthoredMechanismPorts ports) : Rac1AuthoredMechanism
{
    private FileRandomAccessReader? _reader;
    private Rac1NovalisMusic? _music;
    private readonly Dictionary<int, RuntimeAudioClip> _decoded = [];
    private Rac1NovalisMusic.Area? _area;
    private int _slot = -1;
    private double _introRemaining;
    private long _switches;
    private bool _hasLevelStream;

    internal Rac1NovalisMusic? Music => _music;
    internal Rac1NovalisMusic.Area? Area => _area;
    internal int Slot => _slot;
    internal double IntroRemaining => _introRemaining;
    internal int DecodedCount => _decoded.Count;
    internal bool ReaderOpen => _reader is not null;
    internal long SwitchCount => _switches;

    internal override void Configure(RuntimeWorld world, RuntimeWorldScene.Result scene)
    {
        var path = ports.SourcePath() ?? throw new InvalidOperationException("Novalis music needs the original disc source.");
        _reader = new FileRandomAccessReader(path);
        try
        {
            var level = Rac1DiscIndex.Read(_reader).Levels.Single(l => l.LevelId == 1);
            _music = Rac1NovalisMusic.Open(_reader, level);
            Play(Rac1NovalisMusic.Area.Enemy);
            GD.Print($"[rac1-music] Novalis: {_music.Tracks.Count} original VAGp streams, initial Enemy intro");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private bool Play(Rac1NovalisMusic.Area area)
    {
        if (_music is null || _area == area) return false;
        var (intro, _) = _music.Pair(area);
        if (!PlaySlot(intro, loop: false)) return false;
        _area = area;
        _introRemaining = _music.Tracks[intro].DurationSeconds;
        _switches++;
        GD.Print($"[rac1-music] {area} intro (slot {intro})");
        return true;
    }

    private bool PlaySlot(int slot, bool loop)
    {
        if (_music is null) return false;
        if (!_decoded.TryGetValue(slot, out var clip))
        {
            clip = _music.Decode(slot);
            _decoded[slot] = clip;
        }
        int started = ports.StartLevelMusic(new RuntimeAudioPlaybackIntent(clip, RuntimeAudioCategory.Music, loop: loop));
        if (started == 0) return false;
        _hasLevelStream = true;
        _slot = slot;
        return true;
    }

    internal override void Tick(double delta, Rac1AuthoredTickPhase phase, PlayerHost? player)
    {
        if (phase != Rac1AuthoredTickPhase.Music ||
            _music is null || _area is not { } area || _introRemaining <= 0) return;
        _introRemaining -= Math.Max(0, delta);
        if (_introRemaining > 0) return;
        int loop = _music.Pair(area).Loop;
        if (PlaySlot(loop, loop: true))
            GD.Print($"[rac1-music] {area} looping original stream slot {loop}");
    }

    internal void OnCrank(int index)
    {
        // Host zone policy, not a recovered native scene/sector trigger:
        // authored entrance crank 296 leads into the cave; 297 exits.
        if (index == 296) Play(Rac1NovalisMusic.Area.Cave);
        else if (index == 297) Play(Rac1NovalisMusic.Area.Enemy);
    }

    public override void Dispose()
    {
        if (_hasLevelStream) ports.StopLevelMusic();
        _hasLevelStream = false;
        _reader?.Dispose();
        _reader = null;
        _music = null;
        _decoded.Clear();
        _area = null;
        _slot = -1;
        _introRemaining = 0;
        _switches = 0;
    }
}
