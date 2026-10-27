using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

/// <summary>Staged Veldin actor/contact/reward witnesses; not a retail playthrough.</summary>
public sealed class Rac1EnemyScenario(MovementDiagnosticContext context, IRac1EnemyDiagnosticFacts enemies,
    IRac1EnemyDiagnosticActions actors, IRac1DiagnosticLootFacts loot, IRac1EncounterLifecycle lifecycle,
    IRac1EnemyDiagnosticCapture capture, IRac1WrenchApproachFixture approach, IDiagnosticControllerEquipment equipment)
    : Rac1EncounterScenarioSupport(context, enemies, actors)
{
    /// <summary>Staged integration checks; synthetic positions are not a retail replay.</summary>
    public async Task RunAsync()
    {
        try
        {
            Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 0 }, "enemy smoke needs opening Veldin");
            using var frameRate = World.LimitFrameRate(60);
            Driver.Own(Actors.PreservePlayerPose());
            ClearMovementSmokeInput();
            Require(Enemies.ReadPopulation().Enemies == 25 && Enemies.ReadPopulation().Class1440 == 9, "enemy census differs from authored population");
            Require(ReadEnemies().All(enemy => enemy.Read().AnimationSample is not null && enemy.Read().CollisionLayer != 0),
                "enemy lacks animated model or contact body");
            var initialSamples = ReadEnemies().ToDictionary(enemy => enemy.Key.Target.InstanceIndex, enemy => enemy.Read().AnimationSample);
            await PhysicsFramesAsync(30);
            Require(ReadEnemies().All(enemy => enemy.Read().AnimationSample != initialSamples[enemy.Key.Target.InstanceIndex]),
                "an enemy pose is frozen");
            foreach (var enemy in ReadEnemies().Where(enemy => enemy.Key.Target.NativeClassId == 749 &&
                Enemies.ReadToad(enemy.Key).NativeState == Rac1Class749Hostile.TargetSearchNativeState))
                Require(enemy.Read().AnimationSequence == Rac1Class749Hostile.RestSequenceId,
                    $"stationary toad i{enemy.Key.Target.InstanceIndex} is playing locomotion");
            foreach (var enemy in ReadEnemies().Where(enemy => enemy.Key.Target.NativeClassId == 1440))
            {
                var path = Enemies.ReadEntrancePath(Enemy(enemy.Key.Target.InstanceIndex).Key);
                if (path.Length != 0)
                    Require(System.Numerics.Vector3.Distance(ToEffectNative(enemy.Read().BodyPosition), path[0]) < .01f,
                        $"robot i{enemy.Key.Target.InstanceIndex} retained its editor placement instead of path point0");
            }
            Log("[enemy-smoke] census/independent animation clocks/contact bodies PASS");
            Log("[enemy-smoke] stationary toad rest clips/robot path initialization PASS");
            await RunRac1ToadGroundingSmokeAsync();

            var fixture = Driver.Own(Geometry.Create("SyntheticEnemyFixture", new(2000, 64, 2600)));
            fixture.AddMesh("EnemyFloor", 11,
                (new(-25, 0, -25), new(25, 0, -25), new(25, 0, 25)),
                (new(-25, 0, -25), new(25, 0, 25), new(-25, 0, 25)));
            await PhysicsFramesAsync(3);
            Vector3 origin = fixture.GlobalPosition;
            var player = Player;
            player.ResetAt(origin + new Vector3(-8, .05f, 0));
            await WaitForGroundedAsync(player, 120);
            var frog = Enemy(143);
            var robot = Enemy(197);
            Actors.StageRoot(frog.Key, origin);
            Actors.StageRoot(robot.Key, origin + new Vector3(3, 0, 0));
            await PhysicsFramesAsync(4);
            await CaptureRac1EnemiesAsync("idle", origin + new Vector3(1.4f, .8f, 0));

            int balance = loot.ReadLoot().Wallet;
            Require(Actors.TryStrike(frog.Key), "frog rejected ordinary wrench damage");
            Require(frog.Read().Visible && Enemies.ReadToad(frog.Key).NativeState == 12,
                "lethal frog disappeared before its reaction");
            Require(!Actors.TryStrike(frog.Key), "frog emitted a duplicate lethal reward");
            int frogReward = loot.ReadLoot().Pickups.Where(pickup => pickup.Id > 0).Sum(pickup => Rac1BoltCrate.ValueForNativePickupClass(pickup.NativeClassId));
            Require(Rac1BoltCrate.RewardRange(7).Contains(frogReward), "frog reward escaped source range");
#pragma warning disable S1244 // Exact native health witnesses; preserve the original acceptance comparison.
            Require(Actors.TryStrike(robot.Key) && Enemies.ReadRobot(Enemy(197).Key).Health == 1 && robot.Read().Visible,
                "robot did not initialize to two health and stagger after one hit");
#pragma warning restore S1244
            await PhysicsFramesAsync(12);
            await CaptureRac1EnemiesAsync("reactions", origin + new Vector3(1.4f, .7f, 0));
            await Rac1SmokeWaitAsync(() => !frog.Read().Visible && Enemies.ReadRobot(Enemy(197).Key).HitCooldown == 0, 260, "enemy reaction completion");
            Require(frog.Read().CollisionLayer == 0 && frog.Read().DeathPresented, "frog death retained contact");
            Require(Actors.TryStrike(robot.Key), "robot rejected second recovered wrench hit");
            await Rac1SmokeWaitAsync(() => !robot.Read().Visible, 220, "robot lethal reaction");
            Require(loot.ReadLoot().DebrisClasses.Any(nativeClass => nativeClass is 1933 or 1934 or 1935), "robot lacks dedicated death fragments");
            await CaptureRac1EnemiesAsync("robot-death", origin + new Vector3(3, .5f, 0));
            Log("[enemy-smoke] health1/health2, stagger, delayed deaths, contact removal, retail fragments and one reward PASS");
            int emitted = loot.ReadLoot().Pickups.Where(pickup => pickup.Id > 0).Sum(pickup => Rac1BoltCrate.ValueForNativePickupClass(pickup.NativeClassId));
            player.ResetAt(origin + new Vector3(1, .05f, 0));
            for (int tick = 0; tick < 300 && loot.ReadLoot().Pickups.Any(pickup => pickup.Id > 0); tick++)
            {
                if (tick % 30 == 0)
                {
                    var pickup = loot.ReadLoot().Pickups.First(pickup => pickup.Id > 0);
                    player.ResetAt(FromEffectNative(pickup.NativePosition) + Vector3.UnitY * .05f);
                }
                await PhysicsFramesAsync(1);
            }
            Require(!loot.ReadLoot().Pickups.Any(pickup => pickup.Id > 0), $"enemy rewards failed to home/arrive: player={player.Read().GlobalPosition} life={lifecycle.ReadNanotech()} " +
                string.Join(';', loot.ReadLoot().Pickups.Where(pickup => pickup.Id > 0).Select(pickup => $"id{pickup.Id} s{pickup.NativeState} p={pickup.NativePosition} step={pickup.CollectionStep}")));
            Require(loot.ReadLoot().Wallet == balance + emitted && loot.ReadLoot().Outstanding == 0, "enemy bolt arrival lost or duplicated value");
            Log($"[enemy-smoke] emitted bolt scatter/attraction/wallet PASS value={emitted}");

            // Exercise every authored entrance, including routes whose editor
            // placement lies at their endpoint. Observe their live contact
            // bodies along the source-driven XYZ route without floor projection,
            // with Ratchet parked away after supplying the activation polygon.
            for (int instance = 189; instance <= 196; instance++)
            {
                var moving = Enemy(instance);
                var region = Enemies.ReadActivationRegion(Enemy(instance).Key);
                var route = Enemies.ReadEntrancePath(Enemy(instance).Key);
                var inside = region.Aggregate(System.Numerics.Vector3.Zero, (sum, point) => sum + point) / region.Length;
                Require(OBP.RAC1.Level.Rac1Paths.ContainsPlanar(region, inside), "entrance fixture centroid is outside its authored region");
                // Shared/overlapping trigger polygons can already have started
                // another robot. Do not approach a completed route again and
                // turn this entrance check into an attack-slot check.
                if (Enemies.ReadRobot(Enemy(instance).Key).NativeState == 1)
                {
                    player.ResetAt(FromEffectNative(inside) + Vector3.UnitY * .05f);
                    // Enemy native ticks run on the world clock. Several
                    // physics signals can precede its next update under load.
                    await Rac1SmokeWaitAsync(() => Enemies.ReadRobot(Enemy(instance).Key).NativeState != 1, 60, $"robot i{instance} region activation");
                }
                Require(Enemies.ReadRobot(Enemy(instance).Key).NativeState is 2 or 3,
                    $"robot i{instance} did not enter its authored route: state={Enemies.ReadRobot(Enemy(instance).Key).NativeState} player={player.Read().GlobalPosition} life={lifecycle.ReadNanotech()}");
                player.ResetAt(origin + new Vector3(-8, .05f, 0));
                float deviation = 0;
                for (int tick = 0; tick < 1200 && Enemies.ReadRobot(Enemy(instance).Key).NativeState == 2; tick++)
                {
                    deviation = Math.Max(deviation, EnemySmokeDistanceToRoute(route, ToEffectNative(moving.Read().BodyPosition)));
                    await PhysicsFramesAsync(1);
                }
                float endDistance = System.Numerics.Vector3.Distance(ToEffectNative(moving.Read().BodyPosition), route[^1]);
                Require(Enemies.ReadRobot(Enemy(instance).Key).NativeState == 3 && endDistance < .55f && deviation < .75f,
                    $"robot i{instance} entrance strayed or failed to finish: state={Enemies.ReadRobot(Enemy(instance).Key).NativeState} end={endDistance} deviation={deviation}");
                Log($"[enemy-smoke] entrance i{instance} endpoint={endDistance:F3} deviation={deviation:F3} PASS");
            }
            var entrance = Enemy(189);
            Log("[enemy-smoke] all eight authored regions/waypoints/turning/contact-body positions PASS");

            // A short staged stand in front of the completed entrance tests the
            // socket-directed attack through actual Godot world/player contact.
            player.ResetAt(entrance.Read().Position + entrance.Read().Forward * 2.5f + Vector3.UnitY * .05f);
            int healthBefore = lifecycle.ReadNanotech().Nanotech;
            await Rac1SmokeWaitAsync(() => Enemies.ReadRobot(Enemy(189).Key).NativeState == 5, 220, "robot wind-up/fire");
            await CaptureRac1EnemiesAsync("fire", entrance.Read().Position + Vector3.UnitY);
            if (lifecycle.ReadNanotech().Nanotech == healthBefore)
            {
                await Rac1SmokeWaitAsync(() => !entrance.Read().AnimationBlending && entrance.Read().AnimationPosition > 1, 60, "robot active fire marker");
                player.ResetAt(entrance.Read().Socket + entrance.Read().Forward * 1.2f - Vector3.UnitY * .7f);
                await Rac1SmokeWaitAsync(() => lifecycle.ReadNanotech().Nanotech < healthBefore, 60, "robot socket/player contact");
            }
            Require(entrance.Read().FlamePresent && Enemies.ReadRobot(Enemy(189).Key).NativeState == 6, "robot contact did not release its attack and recover");
            Log("[enemy-smoke] wind-up/fire/socket/occlusion query/player damage/recovery PASS");

            player.ResetAt(origin + new Vector3(-8, .05f, 0));
            await WaitForGroundedAsync(player, 120);
            Vector3 facing = new(-(float)Math.Cos(player.Read().Rac1CurrentYaw), 0, (float)Math.Sin(player.Read().Rac1CurrentYaw));
            var wrenchTarget = Enemy(190);
            Actors.StageRoot(wrenchTarget.Key, player.Read().GlobalPosition + facing * 1.3f);
            await PhysicsFramesAsync(4);
            await TapPhysicalKeyAsync(DiagnosticPhysicalKey.Key1);
            await TapRac1PrimaryActionAsync();
#pragma warning disable S1244 // One admitted native wrench hit must leave exactly one health.
            await Rac1SmokeWaitAsync(() => Enemies.ReadRobot(Enemy(190).Key).Health == 1, 100, "animated wrench/robot contact");
#pragma warning restore S1244
            await PhysicsFramesAsync(65);
            await Rac1SmokeApproachWrenchTargetAsync(() => wrenchTarget.Read().Position, 180, "staggered robot", minimumTravel: 0);
            await TapRac1PrimaryActionAsync();
            await Rac1SmokeWaitAsync(() => Enemies.ReadRobot(Enemy(190).Key).Health == 0, 100, "second animated wrench/robot contact");
            Log("[enemy-smoke] ordinary wrench input/animated contact/two-hit robot PASS");

            await PhysicsFramesAsync(80);
            var bombTarget = Enemy(191);
            facing = new(-(float)Math.Cos(player.Read().Rac1CurrentYaw), 0, (float)Math.Sin(player.Read().Rac1CurrentYaw));
            Actors.StageRoot(bombTarget.Key, player.Read().GlobalPosition + facing * 3);
            await PhysicsFramesAsync(3);
            await TapPhysicalKeyAsync(DiagnosticPhysicalKey.Key2);
            await TapRac1PrimaryActionAsync();
            await Rac1SmokeWaitAsync(() => Enemies.ReadRobot(Enemy(191).Key).Health == 0, 180, "Bomb Glove input/projectile/robot consequence");
            Log("[enemy-smoke] ordinary Bomb Glove input/projectile/two-point robot damage PASS");

            player.ResetAt(origin + new Vector3(12, .05f, 10));
            await PhysicsFramesAsync(3);
            Vector3 rayStart = origin + new Vector3(12, .7f, 5), rayEnd = player.Read().GlobalPosition + Vector3.UnitY * .7f;
            Require(Actors.RayTouchesPlayer(bombTarget.Key, rayStart, rayEnd), "unoccluded host attack ray missed player collider");
            Require(Actors.RayTouchesPlayer(bombTarget.Key, rayEnd, rayEnd + Vector3.UnitY * .01f),
                "contact query beginning inside the player collider missed it");
            Driver.Own(fixture.AddBox("EnemyOcclusionWall", new(3, 3, .4f), origin + new Vector3(12, 1.5f, 7.5f), 0));
            await PhysicsFramesAsync(3);
            Require(!Actors.RayTouchesPlayer(bombTarget.Key, rayStart, rayEnd), "enemy attack ray passed through a wall");
            Log("[enemy-smoke] world occlusion blocks enemy contact PASS");

            // This final fixture supplies admitted descriptors to isolate OBP's
            // explicit retry UI, not to claim a recovered retail combat restart.
            while (!lifecycle.ReadNanotech().IsDead)
            {
                await PhysicsFramesAsync(78);
                lifecycle.ApplyAdmittedPlayerDamage(Rac1DamageRuntime.FromClass749Attack(new(749, 143), new(34, 1)));
            }
            await PhysicsFramesAsync(2);
            Require(Enemies.ReadPopulation().RetryVisible, "combat death offers no explicit level retry");
            int switches = Enemies.ReadPopulation().WorldSwitches;
            lifecycle.RetryLevel();
            await PhysicsFramesAsync(3);
            Require(Enemies.ReadPopulation().WorldSwitches == switches + 1 && lifecycle.ReadNanotech().Nanotech == 4 && Enemies.ReadPopulation().Enemies == 25,
                "explicit retry did not rebuild Veldin and restore playable health");
            bool retiredActorRejected = false;
            try { Enemies.ReadEnemy(frog.Key); }
            catch (DiagnosticUnavailableException) { retiredActorRejected = true; }
            Require(retiredActorRejected, "The encounter fixture rebound a retired actor key after retry.");
            Log("[enemy-smoke] combat-death explicit level retry PASS (OBP policy)");
            Log("[enemy-smoke] PASS");

        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogError($"[enemy-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
        finally { CleanupStep(ClearMovementSmokeInput); }
    }


    private async Task RunRac1ToadGroundingSmokeAsync()
    {
        var pair = new[] { Enemy(143), Enemy(144) };
        var worst = new Dictionary<int, float>();
        var starts = pair.ToDictionary(enemy => enemy.Key.Target.InstanceIndex, enemy => enemy.Read().BodyPosition);
        float lowestFloor = float.PositiveInfinity;
        float lowestBody = float.PositiveInfinity;
        foreach (var enemy in pair)
        {
            int instance = enemy.Key.Target.InstanceIndex;
            float separation = Actors.MeasureGroundSeparation(enemy.Key) ?? throw new InvalidOperationException($"no spawn floor under toad i{instance}");
            worst[instance] = separation;
            lowestFloor = Math.Min(lowestFloor, enemy.Read().BodyPosition.Y - separation);
            Log($"[enemy-smoke] toad i{instance} spawn={enemy.Read().BodyPosition} ground-separation={separation:F4}");
        }
        Player.ResetAt(new(-150, 29.6f, 120));
        for (int frame = 0; frame < 180; frame++)
        {
            foreach (var enemy in pair)
            {
                int instance = enemy.Key.Target.InstanceIndex;
                // A capsule can bridge an edge with no floor directly under
                // its centre. Observe actual below-floor clearance when that
                // ray hits, and bound world height even after tunnelling would
                // put the floor outside the ray's upward reach.
                if (Actors.MeasureGroundSeparation(enemy.Key) is { } separation)
                    worst[instance] = Math.Min(worst[instance], separation);
                lowestBody = Math.Min(lowestBody, enemy.Read().BodyPosition.Y);
            }
            await ProcessFrameAsync();
        }
        foreach (var enemy in pair)
        {
            int instance = enemy.Key.Target.InstanceIndex;
            Log($"[enemy-smoke] toad i{instance} approach={enemy.Read().BodyPosition} worst-separation={worst[instance]:F4} state={Enemies.ReadToad(enemy.Key).NativeState}");
            Require(worst[instance] >= -.03f && float.IsFinite(worst[instance]), $"toad i{instance} spawned or moved below the opening floor: {worst[instance]}");
            Require(enemy.Read().BodyPosition.DistanceTo(starts[instance]) > 1, $"toad i{instance} did not pursue during the ground-contact check");
        }
        Require(lowestBody >= lowestFloor - .03f, $"opening toad fell below the local floor range: {lowestBody} < {lowestFloor}");
        Log("[enemy-smoke] both opening toads spawn/approach ground clearance PASS");
    }


    private static float EnemySmokeDistanceToRoute(IReadOnlyList<System.Numerics.Vector3> route, System.Numerics.Vector3 point)
    {
        float closest = float.PositiveInfinity;
        for (int i = 1; i < route.Count; i++)
        {
            var segment = route[i] - route[i - 1];
            float fraction = segment.LengthSquared() == 0 ? 0 : Math.Clamp(System.Numerics.Vector3.Dot(point - route[i - 1], segment) / segment.LengthSquared(), 0, 1);
            closest = Math.Min(closest, System.Numerics.Vector3.Distance(point, route[i - 1] + segment * fraction));
        }
        return closest;
    }

    private Task CaptureRac1EnemiesAsync(string name, Vector3 focus) => capture.CaptureIfRequestedAsync(name, focus, Driver.Lifetime);
    private Task TapRac1PrimaryActionAsync() => Rac1EncounterInput.TapPrimaryAsync(Context, equipment);
    private Task<float> Rac1SmokeApproachWrenchTargetAsync(Func<Vector3> targetCenterProvider, int maxFrames, string label,
        float inputScale = 1, float minimumTravel = .5f, float maximumPlanarDistance = float.PositiveInfinity) =>
        new Rac1WrenchApproachScenario(Context, approach).RunAsync(targetCenterProvider, maxFrames, label, inputScale, minimumTravel, maximumPlanarDistance);

}
