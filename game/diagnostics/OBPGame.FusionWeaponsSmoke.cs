using System.Text.Json;
using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>Ordinary starting point and input only; no world or inventory staging.</summary>
    private async Task RunFusionRecordingSmokeAsync()
    {
        try
        {
            for (int i = 0; i < 360 && _playtest?.WaitingForEntry == true; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_fusion is not null && _fusionAssets?.Problems.Count == 0, "fusion recording requires all three original sources");
            await WaitForGroundedAsync(_player!, 180);
            await PhysicsFramesAsync(20);
            int openingAmmo = FusionTestActive ? 10 : 20;
            Require(_rac1Weapons.FirstRangedAmmo == openingAmmo, "fresh Bomb Glove ammo");
            await CaptureFusionAsync("ordinary-opening");
            await SelectFusionByInputAsync(0);
            await FusionFireInputAsync(16); // The ordinary entry still draws the glove through the native Circle handoff.
            Require(_rac1Weapons.FirstRangedAmmo == openingAmmo - 1, "recorded Bomb Glove did not consume one round");
            await PhysicsFramesAsync(90);
            await FusionMarkAsync();
            await SelectFusionByInputAsync(1);
            await FusionFireInputAsync(60);
            Require(_fusion!.LancerSession.Ammo < 40, "recorded Lancer did not fire");
            await CaptureFusionAsync("ordinary-lancer");
            await PhysicsFramesAsync(30);
            await FusionMarkAsync();
            await SelectFusionByInputAsync(2);
            await FusionFireInputAsync(90);
            Require(_fusion.CannonSession.Ammo <= 9, "recorded Shock Cannon did not spread and charge");
            await CaptureFusionAsync("ordinary-cannon");
            await PhysicsFramesAsync(45);
            await FusionMarkAsync();
            Vector3 start = _player!.GlobalPosition;
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1 });
            await PhysicsFramesAsync(20);
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 0 });
            await PhysicsFramesAsync(15);
            Require(_player.GlobalPosition.DistanceTo(start) > .2f, $"movement did not recover after equipment selection: {start}->{_player.GlobalPosition}; phase={_player.Rac1MovementPhase}");
            GD.Print($"[fusion-record] PASS ordinary opening/input, three weapons, native ammo, charge and movement; foreign hits={_fusionAcceptedHits}");
            FusionKey(Key.F9, true);
        }
        catch (Exception error)
        {
            GD.PrintErr("[fusion-record] FAIL: " + error);
            ApplicationLifecycle.RequestQuit(this, "fusion-record-fail", 2);
        }
    }

    /// <summary>
    /// Staged collision fixtures using original authored target objects. Kept
    /// separate from the ordinary input recording above; not a retail replay.
    /// </summary>
    private async Task RunFusionWeaponsSmokeAsync()
    {
        Node3D? fixture = null;
        try
        {
            Require(_fusion is not null && _fusionAssets?.Problems.Count == 0 && _player is not null, "fusion smoke requires three verified sources on Veldin");
            Engine.MaxFps = 60; ClearMovementSmokeInput();
            string normalInventory = JsonSerializer.Serialize(_fusionOriginalCampaign!.Weapons.CapturePersistentState());
            await WaitForGroundedAsync(_player!, 180);
            await SelectFusionByInputAsync(1);
            Require(_fusionHeldViews[FusionWeaponSession.Lancer].GetChildren().OfType<MeshInstance3D>().Any(), "original Lancer mesh absent");
            Require(_rac1WrenchView is { Visible: false }, "Lancer retained the R&C1 wrench model");
            int beforeWrench = _fusion!.LancerSession.Ammo;
            FusionKey(Key.X, true);
            await Rac1SmokeWaitAsync(() => _player!.Rac1WrenchFrame.Active, 20, "Square wrench input");
            Require(_rac1WrenchView!.Visible && !_fusionHeldViews[FusionWeaponSession.Lancer].Visible, "Square did not own the hand model");
            FusionKey(Key.X, false);
            await Rac1SmokeWaitAsync(() => !_player!.Rac1WrenchFrame.Active, 120, "Square recovery");
            await PhysicsFramesAsync(2);
            Require(_fusion.LancerSession.Ammo == beforeWrench && !_rac1WrenchView.Visible &&
                _fusionHeldViews[FusionWeaponSession.Lancer].Visible, "Square fired the gun or left two held models");
            await SelectFusionByInputAsync(2);
            Require(_fusionHeldViews[FusionWeaponSession.ShockCannon].GetChildren().OfType<MeshInstance3D>().Any(), "original Cannon mesh absent");
            await SelectFusionByInputAsync(0);
            await CaptureFusionAsync("ordinary-selector");
            GD.Print("[fusion-smoke] three source-qualified entries, original held assets and ordinary selector input PASS");
            var attachedAssets = _fusionAssets!;
            using (var missingAssets = new FusionWeaponAssets(new(TrilogySourceDefinitions.All)))
            {
                _fusionAssets = missingAssets;
                try
                {
                    Require(missingAssets.Problems.Count == 3, "missing sources were silently admitted");
                    await SelectFusionByInputAsync(1);
                    int unavailableAmmo = _fusion!.LancerSession.Ammo;
                    await FusionFireInputAsync(20);
                    Require(_fusion.LancerSession.Ammo == unavailableAmmo && !_fusionHeldViews[FusionWeaponSession.Lancer].Visible &&
                        _hudState.Current.CurrentWeapon?.Status?.Contains("Game Sources", StringComparison.Ordinal) == true,
                        "missing source fired, displayed a model or lacked actionable status");
                    await CaptureFusionAsync("missing-source");
                }
                finally { _fusionAssets = attachedAssets; }
            }
            await SelectFusionByInputAsync(0);

            fixture = new Node3D { Name = "FusionStagedContactFixture", Position = new(-2000, 64, 2600) }; AddChild(fixture);
            AddSyntheticTraversalMesh(fixture, "FusionFloor", 11,
                (new(-30, 0, -30), new(30, 0, -30), new(30, 0, 30)),
                (new(-30, 0, -30), new(30, 0, 30), new(-30, 0, 30)));
            await PhysicsFramesAsync(3);
            _player!.ApplyDevelopmentReset(fixture.GlobalPosition + new Vector3(6, .05f, 0));
            await WaitForGroundedAsync(_player, 120);
            Rac1SmokePlaceGroundedFacing(_player.GlobalPosition, Vector3.Left);
            await PhysicsFramesAsync(10);
            var crates = _rac1CrateNodes.Where(n => n.Source.NativeClassId == 500 && IsRac1GameplayActive(n)).Take(5).ToArray();
            Require(crates.Length == 5, "five authored crate objects required");
            await FusionFireInputAsync(3);
            await Rac1SmokeWaitAsync(() => _rac1Projectiles.Values.Any(p => !p.Motion.Flying), 180, "Bomb Glove floor impact");
            Vector3 landing = FromEffectNative(_rac1Projectiles.Values.First(p => !p.Motion.Flying).Motion.Position);
            await PhysicsFramesAsync(120);
            StageRac1CrateForSmoke(crates[0], new(landing.X, fixture.GlobalPosition.Y, landing.Z));
            await PhysicsFramesAsync(3);
            await FusionFireInputAsync(3);
            await Rac1SmokeWaitAsync(() => !IsRac1GameplayActive(crates[0]), 180, "Bomb Glove authored crate reaction");
            Require(_rac1Weapons.FirstRangedAmmo == 8, "Bomb Glove staged ammo");
            await PhysicsFramesAsync(120);

            await SelectFusionByInputAsync(1);
            StageRac1CrateForSmoke(crates[1], _player.GlobalPosition + EquipmentForward * 4);
            await PhysicsFramesAsync(3);
            await FusionFireInputAsync(30);
            Require(!IsRac1GameplayActive(crates[1]) && _fusionLastDamage is { SourceDamage: .5f } &&
                _fusionLastDamage.Shot.Weapon == FusionWeaponSession.Lancer, "Lancer half-damage crate adapter");
            await CaptureFusionAsync("lancer-contact");
            await PhysicsFramesAsync(30); // Drain shots already past the broken crate before placing the robot.

            var robot = _rac1Enemies.Values.First(e => e.Node.Source.NativeClassId == 1440 && !e.RewardEmitted);
            PlaceWeaponSmokeEnemy(robot, _player.GlobalPosition + EquipmentForward * 5);
            await PhysicsFramesAsync(7);
            float health = _rac1Class1440!.Get(robot.Node.Source.InstanceIndex).Health;
            await FusionFireInputAsync(25);
            float after = _rac1Class1440.Get(robot.Node.Source.InstanceIndex).Health;
            Require(after == health - .5f && !robot.RewardEmitted, $"Lancer fractional robot reaction/cooldown: {health}->{after}");
            GD.Print("[fusion-smoke] Bomb Glove / Lancer original ammo and authored crate/robot consequences PASS");

            await SelectFusionByInputAsync(2);
            StageRac1CrateForSmoke(crates[2], _player.GlobalPosition + EquipmentForward * 3);
            await PhysicsFramesAsync(3);
            await FusionFireInputAsync(10);
            Require(!IsRac1GameplayActive(crates[2]) && _fusionLastDamage?.Shot.Weapon == FusionWeaponSession.ShockCannon, "Shock spread crate reaction");
            await PhysicsFramesAsync(30);
            FusionKey(Key.E, true);
            await PhysicsFramesAsync(15);
            StageRac1CrateForSmoke(crates[3], _player.GlobalPosition + EquipmentForward * 4);
            await Rac1SmokeWaitAsync(() => _fusion!.CannonFrame?.BeamUpdatesRemaining > 0, 90, "charged beam");
            Require(!IsRac1GameplayActive(crates[3]) && _fusionLastDamage is { SourceDamage: 50, Kind: "beam" }, "charged raw-damage crate reaction");
            Require(_fusionBeam is { Visible: true } && _fusionAudioStarted > 4, "source beam/audio feedback");
            await CaptureFusionAsync("charged-beam");
            FusionKey(Key.E, false);
            await PhysicsFramesAsync(45);

            // A wall must consume a projectile before a later eligible target.
            await SelectFusionByInputAsync(1);
            var wall = new StaticBody3D { Name = "FusionOcclusionWall", Position = _player.GlobalPosition + EquipmentForward * 2 + Vector3.Up * 2 };
            wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(.15f, 6, 8) } }); fixture.AddChild(wall); wall.GlobalPosition = _player.GlobalPosition + EquipmentForward * 2 + Vector3.Up * 2;
            StageRac1CrateForSmoke(crates[4], _player.GlobalPosition + EquipmentForward * 5);
            await PhysicsFramesAsync(3);
            await FusionFireInputAsync(25);
            Require(IsRac1GameplayActive(crates[4]), "Lancer hit an authored crate through blocking world geometry");
            wall.QueueFree();
            ClearFusionTransients();
            await PhysicsFramesAsync(3);
            Require(_fusionProjectiles.Count == 0 && _fusion!.LancerSession.ActiveShots.Count == 0 && _fusionEffects.Count == 0 &&
                _fusionPendingAudio.Count == 0 && _fusionAudioPlayers.Count == 0 && _rac1Projectiles.Count == 0,
                "fusion reset retained projectiles/effects/audio");
            Require(JsonSerializer.Serialize(_fusionOriginalCampaign.Weapons.CapturePersistentState()) == normalInventory, "fusion contaminated campaign inventory");
            _args = _args with { FusionTest = false };
            Require(OnDestinationChosen(_activeDestination!), "normal Veldin re-entry rejected");
            await PhysicsFramesAsync(5);
            Require(_fusion is not null && !FusionTestActive && _fusionAssets is null && _fusionHeldViews.Count == 0 && _fusionBeam is null,
                "world re-entry retained fusion ownership");
            Require(JsonSerializer.Serialize(_rac1Weapons.CapturePersistentState()) == normalInventory, "normal inventory was not restored");
            Require(ReferenceEquals(_fusion!.Rac1Inventory, _rac1CampaignSession.Weapons), "default weapons replaced the campaign inventory");
            FusionKey(Key.Q, true);
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_quickSelect!.Entries.Any(e => e.Weapon == FusionWeaponSession.Lancer) &&
                _quickSelect.Entries.Any(e => e.Weapon == FusionWeaponSession.ShockCannon), "default Quick Select omitted the two foreign weapons");
            FusionKey(Key.Q, false);
            await PhysicsFramesAsync(2);
            await SelectFusionByInputAsync(1);
            Require(!FusionTestActive && _fusionAssets!.Configuration.LoadoutId == FusionWeaponSession.DefaultLoadoutId,
                "ordinary selection entered the isolated test loadout");
            await FusionFireInputAsync(20);
            Require(_fusion.LancerSession.Ammo < 40, "default Lancer did not fire");
            await SelectFusionByInputAsync(2);
            await FusionFireInputAsync(12);
            Require(_fusion.CannonSession.Ammo < 20, "default Cannon did not fire");
            await SelectFusionByInputAsync(0);
            Require(JsonSerializer.Serialize(_rac1Weapons.CapturePersistentState()) == normalInventory &&
                ReferenceEquals(_rac1Weapons, _rac1CampaignSession.Weapons), "default weapons replaced or modified native inventory");
            GD.Print("[fusion-smoke] default Quick Select, both foreign weapons and normal native inventory PASS");
            GD.Print("[fusion-smoke] PASS: source models/audio/beam, ordinary selector, staged three-weapon damage, occlusion, reset and normal re-entry");
            ApplicationLifecycle.RequestQuit(this, "fusion-smoke-pass");
        }
        catch (Exception error)
        {
            GD.PrintErr("[fusion-smoke] FAIL: " + error);
            ApplicationLifecycle.RequestQuit(this, "fusion-smoke-fail", 3);
        }
        finally
        {
            ClearMovementSmokeInput();
            if (fixture is not null && IsInstanceValid(fixture)) fixture.QueueFree();
        }
    }

    private async Task SelectFusionByInputAsync(int slot)
    {
        FusionKey(Key.Q, true);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(QuickSelectOpen && _quickSelect!.Entries.Count == 3, "fusion Quick Select did not expose three entries");
        float angle = slot * MathF.PI / 4;
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = MathF.Sin(angle) });
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -MathF.Cos(angle) });
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_quickSelect!.SelectedWeapon == FusionWeaponSession.Weapons[slot], "wheel direction selected the wrong source key");
        await CaptureFusionAsync("selector-" + slot);
        FusionKey(Key.Q, false);
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0 });
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 0 });
        await PhysicsFramesAsync(3);
        Require(!QuickSelectOpen && _fusion!.Selected == FusionWeaponSession.Weapons[slot], "release did not equip the selected source");
    }

    private async Task FusionFireInputAsync(int frames)
    {
        FusionKey(Key.E, true); await PhysicsFramesAsync(frames); FusionKey(Key.E, false); await PhysicsFramesAsync(2);
    }

    private async Task FusionMarkAsync()
    {
        FusionKey(Key.F8, true); await PhysicsFramesAsync(1); FusionKey(Key.F8, false);
    }

    private static void FusionKey(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });

    private async Task CaptureFusionAsync(string name)
    {
        string? directory = System.Environment.GetEnvironmentVariable("OBP_FUSION_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory) || DisplayServer.GetName() == "headless") return;
        Directory.CreateDirectory(directory);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Require(image.SavePng(Path.Combine(directory, name + ".png")) == Error.Ok, "fusion capture failed");
    }
}
