using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

/// <summary>Original Novalis acquisition/pursuit witnesses. Actor reads are copied, source-qualified facts.</summary>
public sealed class Rac1NovalisEnemyScenarios(MovementDiagnosticContext context, IRac1EnemyDiagnosticFacts enemies, IRac1EnemyDiagnosticActions actors)
    : Rac1EncounterScenarioSupport(context, enemies, actors)
{
    public async Task BirdbotAsync()
    {
        Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 1 } && Enemies.ReadPopulation().Birdbots == 25,
            "Original Novalis Birdbot smoke needs 25 authored class459 actors");
        using var rate = World.LimitFrameRate(60);
        Driver.Own(Actors.PreservePlayerPose());
        ClearMovementSmokeInput();
        await PhysicsFramesAsync(3);
        const int sample = 309;
        var key = Enemy(sample).Key;
        var initial = Enemies.ReadBirdbot(key);
        Require(initial.Mode == Rac1NovalisBirdbotSession.HostMode.Idle,
            "Original Birdbot should initialize in provisional idle host mode");
        long openingShots = Enemies.ReadPopulation().BirdbotShots;
        Player.ResetAt(Enemies.ReadEnemy(key).Position + new Vector3(5, .2f, 0));
        await PhysicsFramesAsync(6);
        Require(Player.Read().GlobalPosition.DistanceTo(Enemies.ReadEnemy(key).Position) < 12f,
            "Player was not staged within Birdbot acquisition distance");
        bool sawSampleShot = false;
        for (int tick = 0; tick < 145; tick++)
        {
            await PhysicsFramesAsync(1);
            sawSampleShot |= Enemies.ReadBirdbot(key).HasHostedShot;
        }
        var next = Enemies.ReadBirdbot(key);
        var population = Enemies.ReadPopulation();
        Require(population.BirdbotShots > openingShots && sawSampleShot, $"Birdbot i{sample} failed to create its own projectile (mode {next.Mode})");
        Log($"[novalis-birdbot-smoke] PASS class459 i{sample} mode={next.Mode} sequence={next.Sequence} " +
            $"projectilesFired={population.BirdbotShots} HP={population.Nanotech}");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Bug", "S1244:Floating point numbers should not be tested for equality",
        Justification = "The original sleeping-actor regression requires its animation clock to remain exactly unchanged.")]
    public async Task PeckbotAsync()
    {
        Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 1 } && Enemies.ReadPopulation().Peckbots == 68,
            "Novalis Peckbot smoke requires all 68 original class-577 actors");
        using var rate = World.LimitFrameRate(60);
        Driver.Own(Actors.PreservePlayerPose());
        ClearMovementSmokeInput();
        await PhysicsFramesAsync(3);
        const int sample = 586;
        var key = Enemy(sample).Key;
        var before = Enemies.ReadPeckbot(key);
        Require(before.NativeState == 1 && !before.InitialLoiter,
            $"Original Peckbot i{sample} is not a native standing placement");
        Player.ResetAt(Enemies.ReadEnemy(key).Position + new Vector3(3, .25f, 0));
        await PhysicsFramesAsync(6);
        Require(Player.Read().GlobalPosition.DistanceTo(Enemies.ReadEnemy(key).Position) < 8f,
            "Peckbot smoke cannot stage Ratchet near the source actor");
        Vector3 start = Enemies.ReadEnemy(key).Position;
        var far = Enemies.ReadEnemies().Where(actor => actor.Target.NativeClassId == Rac1NovalisPeckbotSession.ClassId)
            .OrderByDescending(actor => Vector3.DistanceSquared(Enemies.ReadEnemy(actor).Position, Player.Read().GlobalPosition)).First();
        float farAnimationBefore = Enemies.ReadEnemy(far).AnimationPosition;
        Vector3 farPositionBefore = Enemies.ReadEnemy(far).Position;
        Require(Vector3.DistanceSquared(farPositionBefore, Player.Read().GlobalPosition) > 42f * 42f,
            "Expected an authored Peckbot outside Novalis host wake distance");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await PhysicsFramesAsync(180);
        timer.Stop();
        Require(Enemies.ReadEnemy(far).AnimationPosition == farAnimationBefore &&
            Vector3.DistanceSquared(Enemies.ReadEnemy(far).Position, farPositionBefore) < 1e-5f,
            "Distant Peckbot still updated despite host sleep distance");
        int nearby = Enemies.ReadEnemies().Count(actor => Vector3.DistanceSquared(Enemies.ReadEnemy(actor).Position, Player.Read().GlobalPosition) < 42f * 42f);
        var performance = Enemies.ReadPerformance();
        Log($"[novalis-peckbot-smoke] PERF {nearby}/{Enemies.ReadPopulation().Enemies} enemies near Ratchet");
        Log($"[novalis-peckbot-smoke] PERF 180 physics frames in {timer.Elapsed.TotalMilliseconds:F0} ms; " +
            $"Godot physics={performance.PhysicsMilliseconds:F2} ms, process={performance.ProcessMilliseconds:F2} ms");
        var next = Enemies.ReadPeckbot(key);
        float moved = start.DistanceTo(Enemies.ReadEnemy(key).Position);
        Require(next.NativeState is >= 2 and <= 5, $"Original standing Peckbot never entered alert/pursuit: s{next.NativeState}");
        Log($"[novalis-peckbot-smoke] PASS class577 i{sample} state={next.NativeState} " +
            $"sequence={next.Sequence} moved={moved:F2}, RatchetHP={Enemies.ReadPopulation().Nanotech}");
    }
}
