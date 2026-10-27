using OBP.Core;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed class Rac1NovalisMusicScenario(MovementDiagnosticContext context, IRac1NovalisMusicFixture music) : MovementScenarioSupport(context)
{
    public async Task RunAsync()
    {
        var initial = music.Read();
        Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 1 } && initial.Tracks.Length == Rac1NovalisMusic.TrackCount,
            "Novalis music smoke requires all nine original VAGp entries");
        await PhysicsFramesAsync(6);
        var state = music.Read();
        Require(state.Area == Rac1NovalisMusic.Area.Enemy && state.Slot == 0, $"Initial Enemy intro not playing: area={state.Area}, slot={state.Slot}");
        music.SelectAuthoredCrank(296); state = music.Read();
        Require(state.Area == Rac1NovalisMusic.Area.Cave && state.Slot == 2, "Cave entrance failed to select original Cave_Start");
        await PhysicsFramesAsync(3);
        music.Tick(initial.Tracks[2].DurationSeconds + .1); state = music.Read();
        Require(state.Slot == 3 && state.IntroRemaining <= 0, "Cave intro never switched to Cave_Loop");
        Require(state.MixerEvents.Any(message => message.Contains("level Music started", StringComparison.Ordinal)), $"Decoded Cave_Loop not accepted by Godot: {state.AudioStatus}");
        music.SelectAuthoredCrank(297); state = music.Read();
        Require(state.Area == Rac1NovalisMusic.Area.Enemy && state.Slot == 0, "Cave exit failed to return to Enemy_Start");
        Log($"[novalis-music-smoke] PASS original Cave intro + loop, Enemy return; sector {initial.Tracks[3].Sector}, decoded {state.DecodedClips} clips");
    }
}
