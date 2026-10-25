using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>Staged integration checks; synthetic positions are not a retail replay.</summary>
    private async Task RunRac1EnemySmokeAsync()
    {
        if (_world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: Rac1AridiaEnemies.LevelId })
        {
            await RunRac1AridiaConstructorbotSmokeAsync();
            return;
        }
        try
        {
            Require(_player is not null && _world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 0 }, "enemy smoke needs opening Veldin");
            Engine.MaxFps = 60;
            ClearMovementSmokeInput();
            Require(_rac1Enemies.Count == 25 && _rac1Class1440!.RegisteredCount == 9, "enemy census differs from authored population");
            Require(_rac1Enemies.Values.All(enemy => enemy.Node.AnimationSample is not null && enemy.Body.CollisionLayer != 0),
                "enemy lacks animated model or contact body");
            var initialSamples = _rac1Enemies.ToDictionary(pair => pair.Key, pair => pair.Value.Node.AnimationSample);
            await PhysicsFramesAsync(30);
            Require(_rac1Enemies.Values.All(enemy => enemy.Node.AnimationSample != initialSamples[enemy.Node.Source.InstanceIndex]),
                "an enemy pose is frozen");
            foreach (var enemy in _rac1Enemies.Values.Where(enemy => enemy.Node.Source.NativeClassId == 749 &&
                _rac1Hostiles.Probe(enemy.Node.Source).NativeState == Rac1Class749Hostile.TargetSearchNativeState))
                Require(enemy.Animation.Sequence == Rac1Class749Hostile.RestSequenceId,
                    $"stationary toad i{enemy.Node.Source.InstanceIndex} is playing locomotion");
            foreach (var enemy in _rac1Enemies.Values.Where(enemy => enemy.Node.Source.NativeClassId == 1440))
            {
                var path = _rac1Class1440!.EntrancePath(enemy.Node.Source.InstanceIndex);
                if (path.Count != 0)
                    Require(System.Numerics.Vector3.Distance(ToEffectNative(enemy.Body.GlobalPosition), path[0]) < .01f,
                        $"robot i{enemy.Node.Source.InstanceIndex} retained its editor placement instead of path point0");
            }
            GD.Print("[enemy-smoke] census/independent animation clocks/contact bodies PASS");
            GD.Print("[enemy-smoke] stationary toad rest clips/robot path initialization PASS");
            await RunRac1ToadGroundingSmokeAsync();

            var fixture = new Node3D { Name = "SyntheticEnemyFixture", Position = new(2000, 64, 2600) };
            AddChild(fixture);
            AddSyntheticTraversalMesh(fixture, "EnemyFloor", 11,
                (new(-25, 0, -25), new(25, 0, -25), new(25, 0, 25)),
                (new(-25, 0, -25), new(25, 0, 25), new(-25, 0, 25)));
            await PhysicsFramesAsync(3);
            Vector3 origin = fixture.GlobalPosition;
            var player = _player!;
            player.ApplyDevelopmentReset(origin + new Vector3(-8, .05f, 0));
            await WaitForGroundedAsync(player, 120);
            var frog = _rac1Enemies[143];
            var robot = _rac1Enemies[197];
            frog.Node.Root.GlobalPosition = origin;
            robot.Node.Root.GlobalPosition = origin + new Vector3(3, 0, 0);
            await PhysicsFramesAsync(4);
            await CaptureRac1EnemiesAsync("idle", origin + new Vector3(1.4f, .8f, 0));

            int balance = _rac1BoltWallet.Balance;
            Require(TryStrikeRac1Hostile(frog.Node), "frog rejected ordinary wrench damage");
            Require(frog.Node.Root.Visible && _rac1Hostiles.Probe(frog.Node.Source).NativeState == 12,
                "lethal frog disappeared before its reaction");
            Require(!TryStrikeRac1Hostile(frog.Node), "frog emitted a duplicate lethal reward");
            int frogReward = _rac1PickupMotion.Where(pair => pair.Key > 0).Sum(pair => Rac1BoltCrate.ValueForNativePickupClass(pair.Value.NativeClassId));
            Require(Rac1BoltCrate.RewardRange(7).Contains(frogReward), "frog reward escaped source range");
            Require(TryStrikeRac1Hostile(robot.Node) && _rac1Class1440!.Get(197).Health == 1 && robot.Node.Root.Visible,
                "robot did not initialize to two health and stagger after one hit");
            await PhysicsFramesAsync(12);
            await CaptureRac1EnemiesAsync("reactions", origin + new Vector3(1.4f, .7f, 0));
            await Rac1SmokeWaitAsync(() => !frog.Node.Root.Visible && _rac1Class1440!.Get(197).HitCooldown == 0, 260, "enemy reaction completion");
            Require(frog.Body.CollisionLayer == 0 && frog.DeathPresented, "frog death retained contact");
            Require(TryStrikeRac1Hostile(robot.Node), "robot rejected second recovered wrench hit");
            await Rac1SmokeWaitAsync(() => !robot.Node.Root.Visible, 220, "robot lethal reaction");
            Require(_rac1Debris.Any(piece => piece.Motion.NativeClassId is 1933 or 1934 or 1935), "robot lacks dedicated death fragments");
            await CaptureRac1EnemiesAsync("robot-death", origin + new Vector3(3, .5f, 0));
            GD.Print("[enemy-smoke] health1/health2, stagger, delayed deaths, contact removal, retail fragments and one reward PASS");
            int emitted = _rac1PickupMotion.Where(pair => pair.Key > 0).Sum(pair => Rac1BoltCrate.ValueForNativePickupClass(pair.Value.NativeClassId));
            player.ApplyDevelopmentReset(origin + new Vector3(1, .05f, 0));
            for (int tick = 0; tick < 300 && _rac1PickupMotion.Keys.Any(id => id > 0); tick++)
            {
                if (tick % 30 == 0)
                {
                    var pickup = _rac1PickupMotion.First(pair => pair.Key > 0).Value;
                    player.ApplyDevelopmentReset(FromEffectNative(pickup.Position) + Vector3.Up * .05f);
                }
                await PhysicsFramesAsync(1);
            }
            Require(!_rac1PickupMotion.Keys.Any(id => id > 0), $"enemy rewards failed to home/arrive: player={player.GlobalPosition} life={_rac1Nanotech.Probe()} " +
                string.Join(';', _rac1PickupMotion.Where(pair => pair.Key > 0).Select(pair => $"id{pair.Key} s{pair.Value.NativeState} p={pair.Value.Position} step={pair.Value.CollectionStep}")));
            Require(_rac1BoltWallet.Balance == balance + emitted && _rac1BoltPickups.OutstandingCount == 0, "enemy bolt arrival lost or duplicated value");
            GD.Print($"[enemy-smoke] emitted bolt scatter/attraction/wallet PASS value={emitted}");

            // Exercise every authored entrance, including routes whose editor
            // placement lies at their endpoint. Observe their live contact
            // bodies along the source-driven XYZ route without floor projection,
            // with Ratchet parked away after supplying the activation polygon.
            var robots = _rac1Class1440!;
            for (int instance = 189; instance <= 196; instance++)
            {
                var moving = _rac1Enemies[instance];
                var region = robots.ActivationRegion(instance);
                var route = robots.EntrancePath(instance);
                var inside = region.Aggregate(System.Numerics.Vector3.Zero, (sum, point) => sum + point) / region.Count;
                Require(OBP.RAC1.Level.Rac1Paths.ContainsPlanar(region, inside), "entrance fixture centroid is outside its authored region");
                // Shared/overlapping trigger polygons can already have started
                // another robot. Do not approach a completed route again and
                // turn this entrance check into an attack-slot check.
                if (robots.Get(instance).NativeState == 1)
                {
                    player.ApplyDevelopmentReset(FromEffectNative(inside) + Vector3.Up * .05f);
                    // Enemy native ticks run on the world clock. Several
                    // physics signals can precede its next update under load.
                    await Rac1SmokeWaitAsync(() => robots.Get(instance).NativeState != 1, 60, $"robot i{instance} region activation");
                }
                Require(robots.Get(instance).NativeState is 2 or 3,
                    $"robot i{instance} did not enter its authored route: state={robots.Get(instance).NativeState} player={player.GlobalPosition} life={_rac1Nanotech.Probe()}");
                player.ApplyDevelopmentReset(origin + new Vector3(-8, .05f, 0));
                float deviation = 0;
                for (int tick = 0; tick < 1200 && robots.Get(instance).NativeState == 2; tick++)
                {
                    deviation = Math.Max(deviation, EnemySmokeDistanceToRoute(route, ToEffectNative(moving.Body.GlobalPosition)));
                    await PhysicsFramesAsync(1);
                }
                float endDistance = System.Numerics.Vector3.Distance(ToEffectNative(moving.Body.GlobalPosition), route[^1]);
                Require(robots.Get(instance).NativeState == 3 && endDistance < .55f && deviation < .75f,
                    $"robot i{instance} entrance strayed or failed to finish: state={robots.Get(instance).NativeState} end={endDistance} deviation={deviation}");
                GD.Print($"[enemy-smoke] entrance i{instance} endpoint={endDistance:F3} deviation={deviation:F3} PASS");
            }
            var entrance = _rac1Enemies[189];
            GD.Print("[enemy-smoke] all eight authored regions/waypoints/turning/contact-body positions PASS");

            // A short staged stand in front of the completed entrance tests the
            // socket-directed attack through actual Godot world/player contact.
            player.ApplyDevelopmentReset(entrance.Node.Root.GlobalPosition + EnemyForward(entrance.Node) * 2.5f + Vector3.Up * .05f);
            int healthBefore = _rac1Nanotech.Probe().Nanotech;
            await Rac1SmokeWaitAsync(() => robots.Get(189).NativeState == 5, 220, "robot wind-up/fire");
            await CaptureRac1EnemiesAsync("fire", entrance.Node.Root.GlobalPosition + Vector3.Up);
            if (_rac1Nanotech.Probe().Nanotech == healthBefore)
            {
                await Rac1SmokeWaitAsync(() => !entrance.Animation.Blending && entrance.Animation.Position > 1, 60, "robot active fire marker");
                player.ApplyDevelopmentReset(EnemySocket(entrance) + EnemyForward(entrance.Node) * 1.2f - Vector3.Up * .7f);
                await Rac1SmokeWaitAsync(() => _rac1Nanotech.Probe().Nanotech < healthBefore, 60, "robot socket/player contact");
            }
            Require(entrance.Flame is not null && robots.Get(189).NativeState == 6, "robot contact did not release its attack and recover");
            GD.Print("[enemy-smoke] wind-up/fire/socket/occlusion query/player damage/recovery PASS");

            player.ApplyDevelopmentReset(origin + new Vector3(-8, .05f, 0));
            await WaitForGroundedAsync(player, 120);
            Vector3 facing = new(-(float)Math.Cos(player.Rac1CurrentYaw), 0, (float)Math.Sin(player.Rac1CurrentYaw));
            var wrenchTarget = _rac1Enemies[190];
            wrenchTarget.Node.Root.GlobalPosition = player.GlobalPosition + facing * 1.3f;
            await PhysicsFramesAsync(4);
            await TapPhysicalKeyAsync(Key.Key1);
            await TapRac1PrimaryActionAsync();
            await Rac1SmokeWaitAsync(() => robots.Get(190).Health == 1, 100, "animated wrench/robot contact");
            await PhysicsFramesAsync(65);
            await Rac1SmokeApproachWrenchTargetAsync(() => wrenchTarget.Node.Root.GlobalPosition, 180, "staggered robot", minimumTravel: 0);
            await TapRac1PrimaryActionAsync();
            await Rac1SmokeWaitAsync(() => robots.Get(190).Health == 0, 100, "second animated wrench/robot contact");
            GD.Print("[enemy-smoke] ordinary wrench input/animated contact/two-hit robot PASS");

            await PhysicsFramesAsync(80);
            var bombTarget = _rac1Enemies[191];
            facing = new(-(float)Math.Cos(player.Rac1CurrentYaw), 0, (float)Math.Sin(player.Rac1CurrentYaw));
            bombTarget.Node.Root.GlobalPosition = player.GlobalPosition + facing * 3;
            await PhysicsFramesAsync(3);
            await TapPhysicalKeyAsync(Key.Key2);
            await TapRac1PrimaryActionAsync();
            await Rac1SmokeWaitAsync(() => robots.Get(191).Health == 0, 180, "Bomb Glove input/projectile/robot consequence");
            GD.Print("[enemy-smoke] ordinary Bomb Glove input/projectile/two-point robot damage PASS");

            player.ApplyDevelopmentReset(origin + new Vector3(12, .05f, 10));
            await PhysicsFramesAsync(3);
            Vector3 rayStart = origin + new Vector3(12, .7f, 5), rayEnd = player.GlobalPosition + Vector3.Up * .7f;
            Require(EnemyRayTouchesPlayer(bombTarget, rayStart, rayEnd), "unoccluded host attack ray missed player collider");
            Require(EnemyRayTouchesPlayer(bombTarget, rayEnd, rayEnd + Vector3.Up * .01f),
                "contact query beginning inside the player collider missed it");
            var wall = new StaticBody3D { Position = new(12, 1.5f, 7.5f) };
            wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(3, 3, .4f) } });
            fixture.AddChild(wall);
            await PhysicsFramesAsync(3);
            Require(!EnemyRayTouchesPlayer(bombTarget, rayStart, rayEnd), "enemy attack ray passed through a wall");
            GD.Print("[enemy-smoke] world occlusion blocks enemy contact PASS");

            // This final fixture supplies admitted descriptors to isolate OBP's
            // explicit retry UI, not to claim a recovered retail combat restart.
            while (!_rac1Nanotech.Probe().IsDead)
            {
                await PhysicsFramesAsync(78);
                ApplyRac1EnemyPlayerDamage(Rac1DamageRuntime.FromClass749Attack(new(749, 143), new(34, 1)));
            }
            await PhysicsFramesAsync(2);
            Require(_rac1CombatRetry is { Visible: true }, "combat death offers no explicit level retry");
            int switches = _worldSwitches;
            _rac1CombatRetry!.GetChildren().OfType<VBoxContainer>().Single().GetChildren().OfType<Button>().Single().EmitSignal(Button.SignalName.Pressed);
            await PhysicsFramesAsync(3);
            Require(_worldSwitches == switches + 1 && _rac1Nanotech.Probe().Nanotech == 4 && _rac1Enemies.Count == 25,
                "explicit retry did not rebuild Veldin and restore playable health");
            GD.Print("[enemy-smoke] combat-death explicit level retry PASS (OBP policy)");
            GD.Print("[enemy-smoke] PASS");
            ApplicationLifecycle.RequestQuit(this, "rac1-enemy-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[enemy-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-enemy-smoke-fail", 1);
        }
        finally { ClearMovementSmokeInput(); }
    }

    private async Task RunRac1ToadGroundingSmokeAsync()
    {
        var pair = new[] { _rac1Enemies[143], _rac1Enemies[144] };
        var worst = new Dictionary<int, float>();
        var starts = pair.ToDictionary(enemy => enemy.Node.Source.InstanceIndex, enemy => enemy.Body.GlobalPosition);
        float lowestFloor = float.PositiveInfinity;
        float lowestBody = float.PositiveInfinity;
        foreach (var enemy in pair)
        {
            int instance = enemy.Node.Source.InstanceIndex;
            float separation = EnemySmokeGroundSeparation(enemy) ?? throw new InvalidOperationException($"no spawn floor under toad i{instance}");
            worst[instance] = separation;
            lowestFloor = Math.Min(lowestFloor, enemy.Body.GlobalPosition.Y - separation);
            GD.Print($"[enemy-smoke] toad i{instance} spawn={enemy.Body.GlobalPosition} ground-separation={separation:F4}");
        }
        _player!.ApplyDevelopmentReset(new(-150, 29.6f, 120));
        for (int frame = 0; frame < 180; frame++)
        {
            foreach (var enemy in pair)
            {
                int instance = enemy.Node.Source.InstanceIndex;
                // A capsule can bridge an edge with no floor directly under
                // its centre. Observe actual below-floor clearance when that
                // ray hits, and bound world height even after tunnelling would
                // put the floor outside the ray's upward reach.
                if (EnemySmokeGroundSeparation(enemy) is { } separation)
                    worst[instance] = Math.Min(worst[instance], separation);
                lowestBody = Math.Min(lowestBody, enemy.Body.GlobalPosition.Y);
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        foreach (var enemy in pair)
        {
            int instance = enemy.Node.Source.InstanceIndex;
            GD.Print($"[enemy-smoke] toad i{instance} approach={enemy.Body.GlobalPosition} worst-separation={worst[instance]:F4} state={_rac1Hostiles.Probe(enemy.Node.Source).NativeState}");
            Require(worst[instance] >= -.03f && float.IsFinite(worst[instance]), $"toad i{instance} spawned or moved below the opening floor: {worst[instance]}");
            Require(enemy.Body.GlobalPosition.DistanceTo(starts[instance]) > 1, $"toad i{instance} did not pursue during the ground-contact check");
        }
        Require(lowestBody >= lowestFloor - .03f, $"opening toad fell below the local floor range: {lowestBody} < {lowestFloor}");
        GD.Print("[enemy-smoke] both opening toads spawn/approach ground clearance PASS");
    }

    private float? EnemySmokeGroundSeparation(Rac1HostedEnemy enemy)
    {
        Vector3 position = enemy.Body.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(position + Vector3.Up * 2, position + Vector3.Down * 8);
        query.Exclude = new global::Godot.Collections.Array<Rid>(_rac1Enemies.Values.Select(other => other.Body.GetRid()).Append(_player!.GetRid()));
        var hit = enemy.Body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.TryGetValue("position", out var foot) && hit.TryGetValue("normal", out var normal) && normal.AsVector3().Y > .65f
            ? position.Y - foot.AsVector3().Y : null;
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

    private async Task CaptureRac1EnemiesAsync(string name, Vector3 focus)
    {
        string? directory = System.Environment.GetEnvironmentVariable("OBP_ENEMY_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var camera = new Camera3D { Current = true, Fov = 55, Far = 10000 };
        AddChild(camera);
        camera.GlobalPosition = focus + new Vector3(3, 2, 6);
        camera.LookAt(focus);
        bool visible = _ui.Visible;
        var hud = _player!.GetNode<CanvasLayer>("PlayerHud");
        bool hudVisible = hud.Visible;
        _ui.Visible = false;
        hud.Visible = false;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, name + ".png"));
        _ui.Visible = visible;
        hud.Visible = hudVisible;
        camera.QueueFree(); _player!.Camera.MakeCurrent();
    }
}
