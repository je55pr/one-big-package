using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    /// <summary>
    /// Exercises source decoded intro -> loop and authored-cave-zone selection
    /// with the real LEVEL1 music reader and Godot audio host. Source files
    /// remain on disc; no retail music is checked into OBP.
    /// </summary>
    private async Task RunRac1NovalisMusicSmokeAsync()
    {
        try
        {
            Require(_world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 1 } &&
                _rac1Music is { Tracks.Count: Rac1NovalisMusic.TrackCount },
                "Novalis music smoke requires all nine original VAGp entries");
            await PhysicsFramesAsync(6);
            Require(_rac1MusicArea == Rac1NovalisMusic.Area.Enemy && _rac1MusicSlot == 0,
                $"Initial Enemy intro not playing: area={_rac1MusicArea}, slot={_rac1MusicSlot}");
            OnRac1NovalisCrankMusic(296);
            Require(_rac1MusicArea == Rac1NovalisMusic.Area.Cave && _rac1MusicSlot == 2,
                "Cave entrance failed to select original Cave_Start");
            await PhysicsFramesAsync(3);
            TickRac1NovalisMusic(_rac1Music!.Tracks[2].DurationSeconds + .1);
            Require(_rac1MusicSlot == 3 && _rac1MusicIntroRemaining <= 0,
                "Cave intro never switched to Cave_Loop");
            Require(_worldHost.AudioDiagnostics.Any(item => item.Message.Contains("level Music started")),
                $"Decoded Cave_Loop not accepted by Godot: {_worldHost.AudioStatusLine}");
            OnRac1NovalisCrankMusic(297);
            Require(_rac1MusicArea == Rac1NovalisMusic.Area.Enemy && _rac1MusicSlot == 0,
                "Cave exit failed to return to Enemy_Start");
            GD.Print($"[novalis-music-smoke] PASS original Cave intro + loop, Enemy return; " +
                $"sector {_rac1Music!.Tracks[3].Sector}, decoded {_rac1MusicDecoded.Count} clips");
            ApplicationLifecycle.RequestQuit(this, "rac1-novalis-music-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[novalis-music-smoke] FAIL {ex}");
            ApplicationLifecycle.RequestQuit(this, "rac1-novalis-music-smoke-fail", 1);
        }
    }
}
