using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

/// <summary>Original Novalis acquisition/pursuit witnesses. Actor reads are copied, source-qualified facts.</summary>
public sealed class Rac1NovalisEnemyScenarios(MovementDiagnosticContext context, IRac1EnemyDiagnosticFacts enemies, IRac1EnemyDiagnosticActions actors)
    : MovementScenarioSupport(context)
{
    public async Task BirdbotAsync()
    {
        Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 1 } && enemies.ReadPopulation().Birdbots == 25,
            "Original Novalis Birdbot smoke needs 25 authored class459 actors");
        using var rate = World.LimitFrameRate(60);
        Driver.Own(actors.PreservePlayerPose());
        ClearMovementSmokeInput();
        await PhysicsFramesAsync(3);
        const int sample = 309;
        var key = enemies.ReadEnemies().Single(actor => actor.Target.InstanceIndex == sample);
        var initial = enemies.ReadBirdbot(key);
        Require(initial.Mode == Rac1NovalisBirdbotSession.HostMode.Idle,
            "Original Birdbot should initialize in provisional idle host mode");
        Player.ResetAt(enemies.ReadEnemy(key).Position + new Vector3(5, .2f, 0));
        await PhysicsFramesAsync(6);
        Require(Player.Read().GlobalPosition.DistanceTo(enemies.ReadEnemy(key).Position) < 12f,
            "Player was not staged within Birdbot acquisition distance");
        await PhysicsFramesAsync(145);
        var next = enemies.ReadBirdbot(key);
        var population = enemies.ReadPopulation();
        Require(population.BirdbotShots > 0, $"Birdbot failed to create a projectile (mode {next.Mode})");
        Log($"[novalis-birdbot-smoke] PASS class459 i{sample} mode={next.Mode} sequence={next.Sequence} " +
            $"projectilesFired={population.BirdbotShots} HP={population.Nanotech}");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Bug", "S1244:Floating point numbers should not be tested for equality",
        Justification = "The original sleeping-actor regression requires its animation clock to remain exactly unchanged.")]
    public async Task PeckbotAsync()
    {
        Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 1 } && enemies.ReadPopulation().Peckbots == 68,
            "Novalis Peckbot smoke requires all 68 original class-577 actors");
        using var rate = World.LimitFrameRate(60);
        Driver.Own(actors.PreservePlayerPose());
        ClearMovementSmokeInput();
        await PhysicsFramesAsync(3);
        const int sample = 586;
        var key = enemies.ReadEnemies().Single(actor => actor.Target.InstanceIndex == sample);
        var before = enemies.ReadPeckbot(key);
        Require(before.NativeState == 1 && !before.InitialLoiter,
            $"Original Peckbot i{sample} is not a native standing placement");
        Player.ResetAt(enemies.ReadEnemy(key).Position + new Vector3(3, .25f, 0));
        await PhysicsFramesAsync(6);
        Require(Player.Read().GlobalPosition.DistanceTo(enemies.ReadEnemy(key).Position) < 8f,
            "Peckbot smoke cannot stage Ratchet near the source actor");
        Vector3 start = enemies.ReadEnemy(key).Position;
        var far = enemies.ReadEnemies().Where(actor => actor.Target.NativeClassId == Rac1NovalisPeckbotSession.ClassId)
            .OrderByDescending(actor => Vector3.DistanceSquared(enemies.ReadEnemy(actor).Position, Player.Read().GlobalPosition)).First();
        float farAnimationBefore = enemies.ReadEnemy(far).AnimationPosition;
        Vector3 farPositionBefore = enemies.ReadEnemy(far).Position;
        Require(Vector3.DistanceSquared(farPositionBefore, Player.Read().GlobalPosition) > 42f * 42f,
            "Expected an authored Peckbot outside Novalis host wake distance");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await PhysicsFramesAsync(180);
        timer.Stop();
        Require(enemies.ReadEnemy(far).AnimationPosition == farAnimationBefore &&
            Vector3.DistanceSquared(enemies.ReadEnemy(far).Position, farPositionBefore) < 1e-5f,
            "Distant Peckbot still updated despite host sleep distance");
        int nearby = enemies.ReadEnemies().Count(actor => Vector3.DistanceSquared(enemies.ReadEnemy(actor).Position, Player.Read().GlobalPosition) < 42f * 42f);
        var performance = enemies.ReadPerformance();
        Log($"[novalis-peckbot-smoke] PERF {nearby}/{enemies.ReadPopulation().Enemies} enemies near Ratchet");
        Log($"[novalis-peckbot-smoke] PERF 180 physics frames in {timer.Elapsed.TotalMilliseconds:F0} ms; " +
            $"Godot physics={performance.PhysicsMilliseconds:F2} ms, process={performance.ProcessMilliseconds:F2} ms");
        var next = enemies.ReadPeckbot(key);
        float moved = start.DistanceTo(enemies.ReadEnemy(key).Position);
        Require(next.NativeState is >= 2 and <= 5, $"Original standing Peckbot never entered alert/pursuit: s{next.NativeState}");
        Log($"[novalis-peckbot-smoke] PASS class577 i{sample} state={next.NativeState} " +
            $"sequence={next.Sequence} moved={moved:F2}, RatchetHP={enemies.ReadPopulation().Nanotech}");
    }
}
