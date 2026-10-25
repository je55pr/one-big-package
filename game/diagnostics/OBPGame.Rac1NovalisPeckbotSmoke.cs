using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>
    /// Drive one authored standing Novalis Peckbot through alert and pursuit
    /// with the real level collider, baked retail animation and ordinary host
    /// fixed ticks. No PCSX2 or synthetic enemy placement is involved.
    /// </summary>
    private async Task RunRac1NovalisPeckbotSmokeAsync()
    {
        try
        {
            Require(_world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 1 } && _player is not null &&
                _rac1Peckbots is { RegisteredCount: 68 },
                "Novalis Peckbot smoke requires all 68 original class-577 actors");
            Engine.MaxFps = 60;
            ClearMovementSmokeInput();
            await PhysicsFramesAsync(3);
            const int sample = 586; // Authored standing Peckbot near the waterworks
            var enemy = _rac1Enemies[sample];
            var before = _rac1Peckbots!.Get(sample);
            Require(before.NativeState == 1 && !before.InitialLoiter,
                $"Original Peckbot i{sample} is not a native standing placement");
            _player!.ApplyDevelopmentReset(enemy.Node.Root.GlobalPosition + new Vector3(3f, .25f, 0f));
            await PhysicsFramesAsync(6);
            Require(_player.GlobalPosition.DistanceTo(enemy.Node.Root.GlobalPosition) < 8f,
                "Peckbot smoke cannot stage Ratchet near the source actor");
            Vector3 start = enemy.Node.Root.GlobalPosition;
            var far = _rac1Enemies.Values
                .Where(actor => actor.Node.Source.NativeClassId == Rac1NovalisPeckbotSession.ClassId)
                .OrderByDescending(actor => actor.Node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition))
                .First();
            float farAnimationBefore = far.Animation.Position;
            Vector3 farPositionBefore = far.Node.Root.GlobalPosition;
            Require(farPositionBefore.DistanceSquaredTo(_player.GlobalPosition) > 42f * 42f,
                "Expected an authored Peckbot outside Novalis host wake distance");
            var sampleTimer = System.Diagnostics.Stopwatch.StartNew();
            await PhysicsFramesAsync(180);
            sampleTimer.Stop();
            Require(far.Animation.Position == farAnimationBefore &&
                far.Node.Root.GlobalPosition.DistanceSquaredTo(farPositionBefore) < 1e-5f,
                "Distant Peckbot still updated despite host sleep distance");
            int nearby = _rac1Enemies.Values.Count(actor =>
                actor.Node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) < 42f * 42f);
            GD.Print($"[novalis-peckbot-smoke] PERF {nearby}/{_rac1Enemies.Count} enemies near Ratchet");
            GD.Print($"[novalis-peckbot-smoke] PERF 180 physics frames in {sampleTimer.Elapsed.TotalMilliseconds:F0} ms; " +
                $"Godot physics={Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:F2} ms, " +
                $"process={Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:F2} ms");
            var next = _rac1Peckbots.Get(sample);
            float moved = start.DistanceTo(enemy.Node.Root.GlobalPosition);
            Require(next.NativeState is >= 2 and <= 5,
                $"Original standing Peckbot never entered alert/pursuit: s{next.NativeState}");
            GD.Print($"[novalis-peckbot-smoke] PASS class577 i{sample} state={next.NativeState} " +
                $"sequence={next.Sequence} moved={moved:F2}, RatchetHP={_rac1Nanotech.Probe().Nanotech}");
            ApplicationLifecycle.RequestQuit(this, "rac1-novalis-peckbot-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[novalis-peckbot-smoke] FAIL {ex}");
            ApplicationLifecycle.RequestQuit(this, "rac1-novalis-peckbot-smoke-fail", 1);
        }
        finally
        {
            ClearMovementSmokeInput();
        }
    }
}
