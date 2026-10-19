using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>Staged integration fixtures; these do not claim a retail gameplay replay.</summary>
    private async Task RunRac1CrateSmokeAsync()
    {
        Node3D? fixture = null;
        try
        {
            Require(_player is not null && _world is { Game: "rac1" }, "crate smoke needs R&C1");
            var player = _player!;
            Engine.MaxFps = 60;
            ClearMovementSmokeInput();
            fixture = new Node3D { Name = "SyntheticCratePickupFixture", Position = new(2000, 64, 2600) };
            AddChild(fixture);
            AddSyntheticTraversalMesh(fixture, "CrateFloor", 11,
                (new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20)),
                (new(-20, 0, -20), new(20, 0, 20), new(-20, 0, 20)));
            await PhysicsFramesAsync(3);
            Vector3 origin = fixture.GlobalPosition;
            player.ApplyDevelopmentReset(origin + new Vector3(-8, .05f, 0));
            await WaitForGroundedAsync(player, 180);
            var wood = _rac1CrateNodes.First(node => IsRac1GameplayActive(node) && node.Source.NativeClassId == 500 &&
                Rac1BoltCrate.ReadAuthored(node.Source)!.RewardCentre > 0);
            StageRac1CrateForSmoke(wood, origin);
            await PhysicsFramesAsync(3);
            await CaptureRac1CratesAsync("intact", origin + Vector3.Up * .5f);
            int balance = _rac1BoltWallet.Balance, soundCount = _rac1PickupSoundCount;
            Require(TryStrikeRac1Crate(wood), "staged positive wrench damage failed");
            Require(!IsRac1GameplayActive(wood) && !_rac1CrateBodies.ContainsKey(wood.Source.InstanceIndex),
                "broken crate retained presence/contact");
            Require(_rac1Debris.Count == 8 && _rac1Debris.All(piece => piece.Motion.NativeClassId is 357 or 358),
                "wood did not spawn its dedicated fragment models");
            int emitted = _rac1PickupMotion.Where(pair => pair.Key > 0).Sum(pair => Rac1BoltCrate.ValueForNativePickupClass(pair.Value.NativeClassId));
            Require(Rac1BoltCrate.RewardRange(Rac1BoltCrate.ReadAuthored(wood.Source)!.RewardCentre).Contains(emitted),
                "payout escaped recovered reward range");
            Require(_rac1PickupSoundCount > soundCount, "crate break sound was not remapped/played");
            await PhysicsFramesAsync(5);
            await CaptureRac1CratesAsync("burst", origin + Vector3.Up * .6f);
            await PhysicsFramesAsync(160);
            Require(_rac1PickupMotion.Keys.Any(id => id > 0) && _rac1PickupMotion.Where(pair => pair.Key > 0).All(pair => pair.Value.NativeState == 3),
                "distant scattered bolts failed to bounce/settle");
            Require(_rac1BoltWallet.Balance == balance, "distant bolt was credited without attraction");
            Require(_rac1PickupNodes.Values.All(node => node.GetChildren().OfType<MeshInstance3D>().All(mesh => mesh.Mesh is ArrayMesh)),
                "bolt presentation used a placeholder primitive");
            await CaptureRac1CratesAsync("settled", origin + Vector3.Up * .25f);
            player.ApplyDevelopmentReset(origin + Vector3.Up * .05f);
            await PhysicsFramesAsync(8);
            await CaptureRac1CratesAsync("homing", origin + Vector3.Up * .7f);
            // Scattering can leave a bolt beyond attraction range of the crate
            // origin. This staged fixture approaches remaining native pickups;
            // it never moves them or injects collection/credit state.
            for (int tick = 0; tick < 300 && _rac1PickupMotion.Keys.Any(id => id > 0); tick++)
            {
                if (tick >= 90 && tick % 30 == 0)
                {
                    var remaining = _rac1PickupMotion.First(pair => pair.Key > 0).Value;
                    player.ApplyDevelopmentReset(FromEffectNative(remaining.Position) + Vector3.Up * .05f);
                }
                await PhysicsFramesAsync(1);
            }
            Require(!_rac1PickupMotion.Keys.Any(id => id > 0), "scattered bolts failed to attract/arrive after approach");
            Require(_rac1BoltWallet.Balance == balance + emitted && _rac1BoltCrates.OutstandingPickupCount == 0,
                "bolt arrival failed exact wallet credit/cleanup");
            Require(Rac1HudProjection.Capture(_rac1Nanotech, _rac1Weapons, _rac1BoltWallet).Bolts!.Balance == balance + emitted,
                "HUD lost collected balance");
            GD.Print($"[crate-smoke] scatter/rebound/retail models/sounds/homing/wallet PASS; value={emitted}");

            var stack = _rac1CrateNodes.Where(node => IsRac1GameplayActive(node) && node.Source.NativeClassId == 500).Take(2).ToArray();
            StageRac1CrateForSmoke(stack[0], origin + new Vector3(5, 0, 0));
            StageRac1CrateForSmoke(stack[1], origin + new Vector3(5, 1, 0));
            await PhysicsFramesAsync(10);
            Require(Math.Abs(stack[1].Root.GlobalPosition.Y - origin.Y - 1) < .05f, "intact stack lost support");
            Require(TryStrikeRac1Crate(stack[0]), "bottom stack crate did not break");
            await PhysicsFramesAsync(60);
            Require(Math.Abs(stack[1].Root.GlobalPosition.Y - origin.Y) < .05f, "unsupported upper crate remained suspended");
            GD.Print("[crate-smoke] stacked-crate support/fall PASS");

            var ammo = _rac1CrateNodes.First(node => IsRac1GameplayActive(node) && node.Source.NativeClassId == 511);
            StageRac1CrateForSmoke(ammo, origin + new Vector3(-5, 0, 0));
            int ammoBefore = _rac1Weapons.FirstRangedAmmo;
            Require(TryStrikeRac1Crate(ammo) && _rac1AmmoPickups.Count > 0, "ammo crate failed to emit retail pickup models");
            int ammoAmount = _rac1AmmoPickups.Sum(pickup => pickup.Motion.Definition.Item == 10 ? pickup.Motion.Definition.Amount : 0);
            Require(ammoAmount > 0, "fixture expected native Bomb Glove preferred ammo");
            player.ApplyDevelopmentReset(origin + new Vector3(-5, .05f, 0));
            for (int i = 0; i < 240 && _rac1AmmoPickups.Count > 0; i++) await PhysicsFramesAsync(1);
            Require(_rac1AmmoPickups.Count == 0, $"ammo failed to collect: player={player.GlobalPosition}, health={_rac1Nanotech.Probe().Nanotech}, " +
                string.Join(';', _rac1AmmoPickups.Select(pickup => $"item{pickup.Motion.Definition.Item} pos={pickup.Motion.Position} vel={pickup.Motion.Velocity} delay={pickup.Motion.DelayTicks} ammo={_rac1Weapons.GetAmmo(pickup.Motion.Definition.Item)}")));
            Require(_rac1Weapons.FirstRangedAmmo == Math.Min(40, ammoBefore + ammoAmount), "ammo grant changed the wrong resource");

            var health = _rac1CrateNodes.First(node => IsRac1GameplayActive(node) && node.Source.NativeClassId == 501);
            StageRac1CrateForSmoke(health, origin + new Vector3(-5, 0, 3));
            Require(TryStrikeRac1Crate(health), "Nanotech crate failed to break");
            await PhysicsFramesAsync(60);
            Require(_rac1NanotechClouds.Count == 1 && !_rac1NanotechClouds[0].Motion.Attracting, "full health wasted the cloud");
            _rac1Nanotech.ApplyDamage(Rac1DamageRuntime.FromClass749Attack(new(749, 149), new(34, 1)));
            int beforeArrivalSound = _rac1PickupSoundCount;
            await Rac1SmokeWaitAsync(() => _rac1NanotechClouds.Count == 0, 120, "Nanotech cloud arrival");
            Require(_rac1Nanotech.Probe().Nanotech == 4, "cloud did not grant exactly one health point");
            Require(_rac1PickupSoundCount > beforeArrivalSound, "Nanotech arrival did not play class501 sound1");
            GD.Print("[crate-smoke] ammo grant and full-health Nanotech retention/arrival PASS");

            var metal = _rac1CrateNodes.FirstOrDefault(node => IsRac1GameplayActive(node) && node.Source.NativeClassId == 502);
            if (metal is not null)
            {
                Vector3 forward = new(-(float)Math.Cos(player.Rac1CurrentYaw), 0, (float)Math.Sin(player.Rac1CurrentYaw));
                StageRac1CrateForSmoke(metal, player.GlobalPosition + forward * 3);
                Require(!TryStrikeRac1Crate(metal) && IsRac1GameplayActive(metal), "metal admitted wrench-only flags");
                OnRac1WeaponSelectionRequested(Rac1WeaponId.FirstRanged);
                int beforeShot = _rac1Weapons.FirstRangedAmmo;
                await TapRac1PrimaryActionAsync();
                await Rac1SmokeWaitAsync(() => !IsRac1GameplayActive(metal), 180, "Bomb Glove projectile/metal crate contact");
                Require(!IsRac1GameplayActive(metal) && _rac1Debris.Any(piece => piece.Motion.NativeClassId == 354),
                    "metal break failed native flag/debris path");
                Require(_rac1Weapons.FirstRangedAmmo == beforeShot - 1, "metal test failed to consume one Bomb Glove round");
                GD.Print("[crate-smoke] metal rejects wrench/Bomb Glove input and projectile/metal fragments PASS");
            }
            await PhysicsFramesAsync(600);
            Require(_rac1Debris.Count == 0, "debris failed to expire");
            GD.Print($"[crate-smoke] PASS level={_world!.LevelId}");
            ApplicationLifecycle.RequestQuit(this, "rac1-crate-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[crate-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-crate-smoke-fail", 1);
        }
        finally
        {
            if (fixture is not null && IsInstanceValid(fixture)) fixture.QueueFree();
            ClearMovementSmokeInput();
        }
    }

    private void StageRac1CrateForSmoke(RuntimeWorldScene.DynamicObjectNode crate, Vector3 position)
    {
        crate.Root.GlobalPosition = position;
        if (_rac1FallingCrates.TryGetValue(crate.Source.InstanceIndex, out var fall))
            _rac1FallingCrates[crate.Source.InstanceIndex] = (crate.Root, new(position.Y + fall.FootOffset), fall.FootOffset);
    }

    private async Task CaptureRac1CratesAsync(string name, Vector3 focus)
    {
        string? directory = System.Environment.GetEnvironmentVariable("OBP_CRATE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var camera = new Camera3D { Current = true, Fov = 55, Far = 10000 };
        AddChild(camera);
        bool uiVisible = _ui.Visible;
        var playerHud = _player!.GetNode<CanvasLayer>("PlayerHud");
        bool playerHudVisible = playerHud.Visible;
        camera.GlobalPosition = focus + new Vector3(2.5f, 1.5f, 3.5f);
        camera.LookAt(focus);
        _ui.Visible = false;
        playerHud.Visible = false;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, $"level{_world!.LevelId}-{name}.png"));
        _ui.Visible = uiVisible;
        playerHud.Visible = playerHudVisible;
        camera.QueueFree();
        _player.Camera.MakeCurrent();
    }
}
