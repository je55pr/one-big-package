using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    private async Task RunRac1NovalisBirdbotSmokeAsync()
    {
        try
        {
            Require(_world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 1 } &&
                _player is not null && _rac1Birdbots is { RegisteredCount: 25 },
                "Original Novalis Birdbot smoke needs 25 authored class459 actors");
            Engine.MaxFps = 60;
            ClearMovementSmokeInput();
            await PhysicsFramesAsync(3);
            const int sample = 309;
            var enemy = _rac1Enemies[sample];
            var initial = _rac1Birdbots!.Get(sample);
            Require(initial.Mode == Rac1NovalisBirdbotSession.HostMode.Idle,
                "Original Birdbot should initialize in provisional idle host mode");
            _player!.ApplyDevelopmentReset(enemy.Node.Root.GlobalPosition + new Vector3(5, .2f, 0));
            await PhysicsFramesAsync(6);
            Require(_player.GlobalPosition.DistanceTo(enemy.Node.Root.GlobalPosition) < 12f,
                "Player was not staged within Birdbot acquisition distance");
            await PhysicsFramesAsync(145);
            var next = _rac1Birdbots.Get(sample);
            Require(_rac1BirdbotShotCount > 0,
                $"Birdbot failed to create a projectile (mode {next.Mode})");
            GD.Print($"[novalis-birdbot-smoke] PASS class459 i{sample} mode={next.Mode} " +
                $"sequence={next.Sequence} projectilesFired={_rac1BirdbotShotCount} HP={_rac1Nanotech.Probe().Nanotech}");
            ApplicationLifecycle.RequestQuit(this, "rac1-novalis-birdbot-smoke-pass", 0);
        }
        catch (OperationCanceledException error) when (Diagnostics.LegacyDiagnosticCancellation.IsOwned(error))
        {
            throw;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[novalis-birdbot-smoke] FAIL {ex}");
            ApplicationLifecycle.RequestQuit(this, "rac1-novalis-birdbot-smoke-fail", 1);
        }
        finally
        {
            ClearMovementSmokeInput();
        }
    }
}
