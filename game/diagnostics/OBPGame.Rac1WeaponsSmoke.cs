using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>Host integration fixture, not an emulator/recovered input replay.</summary>
    private async Task RunRac1WeaponsSmokeAsync()
    {
        Node3D? fixture = null;
        try
        {
            Require(_player is not null && _world is { Game: "rac1", LevelId: 0 }, "weapons smoke needs opening Veldin");
            Engine.MaxFps = 60; ClearMovementSmokeInput();
            fixture = new Node3D { Name = "SyntheticEquipmentFixture", Position = new(-2000, 64, 2600) }; AddChild(fixture);
            AddSyntheticTraversalMesh(fixture, "EquipmentFloor", 11,
                (new(-30, 0, -30), new(30, 0, -30), new(30, 0, 30)),
                (new(-30, 0, -30), new(30, 0, 30), new(-30, 0, 30)));
            await PhysicsFramesAsync(3);
            _player!.ApplyDevelopmentReset(fixture.GlobalPosition + new Vector3(6, .05f, 0));
            await WaitForGroundedAsync(_player, 120);
            Rac1SmokePlaceGroundedFacing(_player.GlobalPosition, Vector3.Left);
            ToggleRac1WeaponPreview();
            Require(_rac1WeaponPreviewInventory is not null && !_rac1CampaignSession.Weapons.Owns(Rac1WeaponId.Blaster),
                "preview contaminated campaign ownership");
            await RunRac1WeaponIdleAndAudioSmokeAsync();
            foreach (var profile in Rac1WeaponProfiles.Additional)
            {
                OnRac1WeaponSelectionRequested(profile.Item); await PhysicsFramesAsync(8);
                var view = _rac1EquipmentViews[profile.Item];
                Require(view.Visible && _rac1WrenchView is { Visible: false }, $"{profile.Name} visibility");
                Require(view.GetChildren().OfType<MeshInstance3D>().All(mesh => mesh.Mesh is ArrayMesh), $"{profile.Name} placeholder model");
                Require(EquipmentMuzzle(profile.Item).DistanceTo(_player.GlobalPosition) < 3, $"{profile.Name} socket is detached");
                await CaptureRac1BombAsync(profile.Key + "-held", _player.GlobalPosition + Vector3.Up * .8f, true);
            }
            var crates = _rac1CrateNodes.Where(node => IsRac1GameplayActive(node) && node.Source.NativeClassId == 500).Take(4).ToArray();
            Require(crates.Length == 4, "weapon crate fixtures unavailable");
            OnRac1WeaponSelectionRequested(Rac1WeaponId.Blaster); await PhysicsFramesAsync(8);
            StageRac1CrateForSmoke(crates[0], _player.GlobalPosition + EquipmentForward * 4);
            await PhysicsFramesAsync(3);
            int ammo = _rac1Weapons.GetAmmo(15); OnRac1PrimaryAttackRequested(); await PhysicsFramesAsync(2);
            Require(_rac1Weapons.GetAmmo(15) == ammo - 1, "Blaster ammo");
            Require(_rac1EquipmentShots.Any(shot => shot.Weapon == Rac1WeaponId.Blaster), "Blaster bolt effect absent");
            await Rac1SmokeWaitAsync(() => !IsRac1GameplayActive(crates[0]), 30, "Blaster travelling crate hit");
            await CaptureRac1BombAsync("blaster-shot", _player.GlobalPosition + EquipmentForward * 2 + Vector3.Up * .6f);
            GD.Print("[weapons-smoke] five retail skins/sockets/HUD and Blaster shot PASS");
            var blasterToad = _rac1Enemies.Values.First(enemy => enemy.Node.Source.NativeClassId == 749 && !enemy.RewardEmitted);
            PlaceWeaponSmokeEnemy(blasterToad, _player.GlobalPosition + EquipmentForward * 4);
            await PhysicsFramesAsync(7); OnRac1PrimaryAttackRequested();
            await Rac1SmokeWaitAsync(() => blasterToad.RewardEmitted, 40, "Blaster shared reaction on a toad");
            var blasterRobot = _rac1Enemies.Values.First(enemy => enemy.Node.Source.NativeClassId == 1440 && !enemy.RewardEmitted);
            PlaceWeaponSmokeEnemy(blasterRobot, _player.GlobalPosition + EquipmentForward * 5);
            await PhysicsFramesAsync(8); Input.ActionPress(RawGamepadInput.Traversal);
            await Rac1SmokeWaitAsync(() => blasterRobot.RewardEmitted, 55, "Blaster robot kind/repeated-shot reaction");
            Input.ActionRelease(RawGamepadInput.Traversal); await PhysicsFramesAsync(8);
            GD.Print("[weapons-smoke] Blaster raw descriptor, victim-kind override and repeated enemy hits PASS");

            OnRac1WeaponSelectionRequested(Rac1WeaponId.Pyrocitor); await PhysicsFramesAsync(8);
            StageRac1CrateForSmoke(crates[1], _player.GlobalPosition + EquipmentForward * 3); await PhysicsFramesAsync(3);
            ammo = _rac1Weapons.GetAmmo(16);
            Input.ActionPress(RawGamepadInput.Traversal);
            await PhysicsFramesAsync(18);
            Require(_rac1Weapons.GetAmmo(16) < ammo && _rac1Flames.Count > 0, "Pyrocitor did not sustain flame/ammo");
            await CaptureRac1BombAsync("pyrocitor-stream", _player.GlobalPosition + EquipmentForward * 1.5f + Vector3.Up * .7f);
            Input.ActionRelease(RawGamepadInput.Traversal);
            await PhysicsFramesAsync(35);
            Require(!IsRac1GameplayActive(crates[1]) && !_rac1PyroEmitting && _rac1Flames.Count == 0, "Pyrocitor contact or stop lifecycle");
            GD.Print("[weapons-smoke] Pyrocitor travelling flame, damage and stop PASS");

            OnRac1WeaponSelectionRequested(Rac1WeaponId.MineGlove); await PhysicsFramesAsync(8);
            ammo = _rac1Weapons.GetAmmo(17); OnRac1PrimaryAttackRequested();
            await Rac1SmokeWaitAsync(() => _rac1EquipmentShots.Any(shot => shot.Weapon == Rac1WeaponId.MineGlove && shot.Grounded), 180, "mine ground contact");
            var mine = _rac1EquipmentShots.Single(shot => shot.Weapon == Rac1WeaponId.MineGlove);
            Require(_rac1Weapons.GetAmmo(17) == ammo - 1, "mine ammo");
            await PhysicsFramesAsync(15);
            Require(mine.Node is Rac1EquipmentView { Sequence: 2 }, "mine did not enter animated armed sequence");
            Require(mine.Position.Y - ((Rac1EquipmentView)mine.Node).FootOffset >= fixture.GlobalPosition.Y - .02f,
                "grounded mine is embedded in the floor");
            await CaptureRac1BombAsync("mine-armed", mine.Position);
            StageRac1CrateForSmoke(crates[2], mine.Position + new Vector3(.7f, -.05f, 0)); await PhysicsFramesAsync(3);
            var mineVictim = _rac1Enemies.Values.First(enemy => enemy.Node.Source.NativeClassId == 749 && !enemy.RewardEmitted);
            PlaceWeaponSmokeEnemy(mineVictim, mine.Position + new Vector3(1.5f, -.18f, 0));
            await Rac1SmokeWaitAsync(() => !_rac1EquipmentShots.Contains(mine), 120, "mine proximity/seek detonation");
            Require(!IsRac1GameplayActive(crates[2]), "mine source-owned blast failed");
            GD.Print("[weapons-smoke] Mine flight, ground/armed animation and blast PASS");

            OnRac1WeaponSelectionRequested(Rac1WeaponId.GloveOfDoom); await PhysicsFramesAsync(8);
            ammo = _rac1Weapons.GetAmmo(20); OnRac1PrimaryAttackRequested();
            await Rac1SmokeWaitAsync(() => _rac1DoomAgents.Count == 4, 210, "four Doom agents");
            Require(_rac1Weapons.GetAmmo(20) == ammo - 1 && !_rac1EquipmentShots.Any(shot => shot.Weapon == Rac1WeaponId.GloveOfDoom), "Doom canister did not retire after four agents");
            Require(_rac1DoomAgents.All(bot => bot.View.GetChildren().OfType<MeshInstance3D>().Any()), "Doom bots lack retail skin");
            var wandering = _rac1DoomAgents[0]; Vector3 wanderStart = wandering.Body.GlobalPosition;
            Vector3 initialWander = FromEffectNative(wandering.Motion.Wander);
            await PhysicsFramesAsync(40);
            Vector3 travelled = wandering.Body.GlobalPosition - wanderStart; travelled.Y = 0;
            Require(travelled.Length() > 1.8f && travelled.Normalized().Dot(initialWander) > .9f,
                "Doom floor contact turns its wander direction into a circle");
            await CaptureRac1BombAsync("doom-bots", _rac1DoomAgents[0].Body.GlobalPosition);
            var doomVictim = _rac1Enemies.Values.First(enemy => enemy.Node.Source.NativeClassId == 749 && !enemy.RewardEmitted);
            PlaceWeaponSmokeEnemy(doomVictim, _rac1DoomAgents[0].Body.GlobalPosition + new Vector3(1.5f, 0, 0));
            await Rac1SmokeWaitAsync(() => doomVictim.RewardEmitted, 120, "Doom agent seeking/contact damage");
            GD.Print("[weapons-smoke] Doom canister, four spaced animated bots and accounting PASS");

            OnRac1WeaponSelectionRequested(Rac1WeaponId.SuckCannon); await PhysicsFramesAsync(8);
            StageRac1CrateForSmoke(crates[3], _player.GlobalPosition + EquipmentForward * 3); await PhysicsFramesAsync(3);
            Input.ActionPress(RawGamepadInput.Traversal); await PhysicsFramesAsync(6);
            Require(_rac1AdditionalWeapons!.Cargo.Count == 1 && !IsRac1GameplayActive(crates[3]), "Suck Cannon failed capture/removal");
            await CaptureRac1BombAsync("suck-capture", _player.GlobalPosition + EquipmentForward * 1.5f + Vector3.Up * .6f);
            Input.ActionRelease(RawGamepadInput.Traversal); await PhysicsFramesAsync(20);
            Require(_rac1Weapons.GetAmmo(9) == 0, "capture wrote ordinary ammo");
            var state = CaptureMovementState();
            Require(state.World is { CapturedMobies.Count: 1, WeaponPreview: true }, "state lost cargo/preview boundary");
            string repro = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "captures", "repros", "weapon-smoke-state.json"));
            MovementReproStateFile.Save(repro, state);
            await LoadMovementStateAsync(repro);
            Require(_rac1AdditionalWeapons is { Cargo.Count: 1 } && _rac1WeaponPreviewInventory is not null &&
                !_rac1CampaignSession.Weapons.Owns(Rac1WeaponId.Blaster), "reload lost cargo or exported preview inventory");
            OnRac1PrimaryAttackRequested(); await PhysicsFramesAsync(2);
            Require(_rac1AdditionalWeapons.Cargo.Count == 0 && _rac1EquipmentShots.Any(shot => shot.Weapon == Rac1WeaponId.SuckCannon), "Suck Cannon did not release real captured model");
            await CaptureRac1BombAsync("suck-release", _player.GlobalPosition + EquipmentForward * 2 + Vector3.Up * .6f);
            await PhysicsFramesAsync(120);
            Require(!_rac1EquipmentShots.Any(shot => shot.Weapon == Rac1WeaponId.SuckCannon), "captured projectile leaked");
            GD.Print("[weapons-smoke] Suck capture, separate cargo/HUD/save and released Moby PASS");
            await RunRac1ArsenalSmokeAsync();
            ApplicationLifecycle.RequestQuit(this, "rac1-weapons-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[weapons-smoke] FAIL: {ex}"); ApplicationLifecycle.RequestQuit(this, "rac1-weapons-smoke-fail", 2);
        }
        finally { Input.ActionRelease(RawGamepadInput.Traversal); if (fixture is not null && IsInstanceValid(fixture)) fixture.QueueFree(); }
    }

    private static void PlaceWeaponSmokeEnemy(Rac1HostedEnemy enemy, Vector3 position)
    {
        enemy.NeedsGroundPlacement = false; enemy.Body.GlobalPosition = position;
        enemy.Body.Velocity = Vector3.Zero; enemy.Node.Root.GlobalPosition = position;
    }

    private async Task RunRac1WeaponIdleAndAudioSmokeAsync()
    {
        int idleShots = _rac1WeaponShotsFired;
        foreach (var profile in Rac1WeaponProfiles.Additional)
        {
            OnRac1WeaponSelectionRequested(profile.Item); await PhysicsFramesAsync(85);
            Require(_rac1EquipmentUseTicks == 0 && _rac1WeaponLoop is null && _rac1WeaponShotsFired == idleShots,
                $"{profile.Name} fires/plays a firing loop while idle");
            Require(_rac1EquipmentViews[profile.Item].Sequence == 1, $"{profile.Name} failed return to static neutral");
            await CaptureRac1BombAsync(profile.Key + "-neutral", _player!.GlobalPosition + Vector3.Up * .8f, true);
        }
        GD.Print("[weapons-smoke] every held weapon remains neutral and silent with no input PASS");
        foreach (var entry in new[] { (Rac1WeaponId.Pyrocitor, 176, 1), (Rac1WeaponId.Blaster, 168, 1),
            (Rac1WeaponId.SuckCannon, 849, 2), (Rac1WeaponId.TeslaClaw, 177, 4), (Rac1WeaponId.MorphORay, 185, 0) })
        {
            OnRac1WeaponSelectionRequested(entry.Item1); await PhysicsFramesAsync(85);
            int before = _rac1WeaponSoundTrace.Count;
            Input.ActionPress(RawGamepadInput.Traversal); await PhysicsFramesAsync(15);
            Require(_rac1WeaponLoop == (entry.Item2, entry.Item3), $"{entry.Item1} has no sustained firing voice");
            Require(_rac1PickupAssets!.Sounds!.TryGetValue((entry.Item2, entry.Item3), out var sound) &&
                sound.Clip.InterleavedPcm16.Any(sample => sample != 0), $"{entry.Item1} voice is absent/silent");
            if (entry.Item1 is Rac1WeaponId.TeslaClaw or Rac1WeaponId.MorphORay)
                Require(_rac1ArsenalBeams.Any(beam => beam.Visible), $"{entry.Item1} has no emitter feedback without a target");
            await CaptureRac1BombAsync(Rac1WeaponProfiles.Get(entry.Item1).Key + "-sustain",
                _player!.GlobalPosition + Vector3.Up * .8f, true);
            Input.ActionRelease(RawGamepadInput.Traversal); await PhysicsFramesAsync(45);
            Require(_rac1WeaponLoop is null && _rac1EquipmentUseTicks == 0, $"{entry.Item1} sound/pose survived release");
            Require(_rac1WeaponSoundTrace.Skip(before).Count(cue => cue.Class == entry.Item2 && cue.Sound == entry.Item3 && cue.Loop) == 1,
                $"{entry.Item1} restarted/stacked its sustained voice");
        }
        OnRac1WeaponSelectionRequested(Rac1WeaponId.Blaster); await PhysicsFramesAsync(85);
        int trace = _rac1WeaponSoundTrace.Count, shots = _rac1WeaponShotsFired;
        OnRac1PrimaryAttackRequested(); await PhysicsFramesAsync(2);
        Require(_rac1WeaponLoop == (168, 1), "Blaster tap has no voice");
        await PhysicsFramesAsync(10);
        Require(_rac1WeaponLoop is null && _rac1WeaponShotsFired == shots + 1 &&
            _rac1WeaponSoundTrace.Skip(trace).Count(cue => cue.Class == 168 && cue.Sound == 1) == 1,
            "Blaster tap plays multiple full firing samples or leaves its burst running");
        GD.Print("[weapons-smoke] all continuous voices start once, emit feedback, and stop on release; single Blaster tap stops before four-shot tail PASS");
        OnRac1WeaponSelectionRequested(Rac1WeaponId.Taunter); await PhysicsFramesAsync(85);
        trace = _rac1WeaponSoundTrace.Count;
        Input.ActionPress(RawGamepadInput.Traversal); await PhysicsFramesAsync(180);
        Input.ActionRelease(RawGamepadInput.Traversal); await PhysicsFramesAsync(10);
        Require(_rac1WeaponSoundTrace.Skip(trace).Where(cue => cue.Class == 175 && cue.Sound < 6).Select(cue => cue.Sound).Distinct().Count() >= 2,
            "Taunter does not advance its actual sound steps");
        GD.Print("[weapons-smoke] Taunter actual sound-step scheduling PASS");
    }
}
