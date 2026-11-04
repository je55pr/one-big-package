using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed class Rac1WeaponsScenario(MovementDiagnosticContext context, Rac1WeaponScenarioPorts ports) : Rac1WeaponScenarioSupport(context, ports)
{
    public async Task RunAsync()
    {
        IDiagnosticGeometryLease? fixture = null;
        try
        {
            Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 0 }, "weapons smoke needs opening Veldin");
            using var frameRate = World.LimitFrameRate(60); ClearMovementSmokeInput();
            fixture = Geometry.Create("SyntheticEquipmentFixture", new(-2000, 64, 2600));
            fixture.AddMesh("EquipmentFloor", 11,
                (new(-30, 0, -30), new(30, 0, -30), new(30, 0, 30)),
                (new(-30, 0, -30), new(30, 0, 30), new(-30, 0, 30)));
            await PhysicsFramesAsync(3);
            Player.ResetAt(fixture.GlobalPosition + new Vector3(6, .05f, 0));
            await WaitForGroundedAsync(Player, 120);
            Ports.Combat.PlacePlayerFacing(Player.Read().GlobalPosition, -Vector3.UnitX, true);
            Driver.Own(Additional.BeginPreview());
            Require(Additional.Read().Preview && !Additional.Read().CampaignOwnsBlaster,
                "preview contaminated campaign ownership");
            await RunRac1WeaponIdleAndAudioSmokeAsync();
            foreach (var profile in Rac1WeaponProfiles.Additional)
            {
                Ports.Equipment.Select(profile.Item); await PhysicsFramesAsync(8);
                var view = Additional.ReadHeld(profile.Item);
                Require(view.Visible && !Ports.Bomb.ReadView().WrenchVisible, $"{profile.Name} visibility");
                Require(view.OriginalMeshes, $"{profile.Name} placeholder model");
                Require(Additional.ReadMuzzle(profile.Item).DistanceTo(Player.Read().GlobalPosition) < 3, $"{profile.Name} socket is detached");
                await CaptureRac1BombAsync(profile.Key + "-held", Player.Read().GlobalPosition + Vector3.UnitY * .8f, true);
            }
            var crates = Targets.ReadCrates().Where(node => Targets.Read(node.Key).Active && node.Key.Target.NativeClassId == 500).Take(4).ToArray();
            Require(crates.Length == 4, "weapon crate fixtures unavailable");
            Ports.Equipment.Select(Rac1WeaponId.Blaster); await PhysicsFramesAsync(8);
            Targets.StageCrate(crates[0].Key, Player.Read().GlobalPosition + EquipmentForward * 4);
            await PhysicsFramesAsync(3);
            int ammo = Additional.ReadAmmo(15); Ports.Combat.RequestPrimaryAttack(); await PhysicsFramesAsync(2);
            Require(Additional.ReadAmmo(15) == ammo - 1, "Blaster ammo");
            Require(Additional.ReadShots().Any(shot => shot.Read().Weapon == Weapon(Rac1WeaponId.Blaster)), "Blaster bolt effect absent");
            await Rac1SmokeWaitAsync(() => !Targets.Read(crates[0].Key).Active, 30, "Blaster travelling crate hit");
            await CaptureRac1BombAsync("blaster-shot", Player.Read().GlobalPosition + EquipmentForward * 2 + Vector3.UnitY * .6f);
            Log("[weapons-smoke] five retail skins/sockets/HUD and Blaster shot PASS");
            var blasterToad = Targets.ReadEnemies().First(enemy => enemy.Key.Target.NativeClassId == 749 && !Targets.Read(enemy.Key).RewardEmitted);
            Targets.StageEnemy(blasterToad.Key, Player.Read().GlobalPosition + EquipmentForward * 4);
            await PhysicsFramesAsync(7); Ports.Combat.RequestPrimaryAttack();
            await Rac1SmokeWaitAsync(() => Targets.Read(blasterToad.Key).RewardEmitted, 40, "Blaster shared reaction on a toad");
            var blasterRobot = Targets.ReadEnemies().First(enemy => enemy.Key.Target.NativeClassId == 1440 && !Targets.Read(enemy.Key).RewardEmitted);
            Targets.StageEnemy(blasterRobot.Key, Player.Read().GlobalPosition + EquipmentForward * 5);
            await PhysicsFramesAsync(8); Driver.SetAction(MovementDiagnosticDriver.Traversal, true);
            await Rac1SmokeWaitAsync(() => Targets.Read(blasterRobot.Key).RewardEmitted, 55, "Blaster robot kind/repeated-shot reaction");
            Driver.SetAction(MovementDiagnosticDriver.Traversal, false); await PhysicsFramesAsync(8);
            Log("[weapons-smoke] Blaster raw descriptor, victim-kind override and repeated enemy hits PASS");

            Ports.Equipment.Select(Rac1WeaponId.Pyrocitor); await PhysicsFramesAsync(8);
            Targets.StageCrate(crates[1].Key, Player.Read().GlobalPosition + EquipmentForward * 3); await PhysicsFramesAsync(3);
            ammo = Additional.ReadAmmo(16);
            Driver.SetAction(MovementDiagnosticDriver.Traversal, true);
            await PhysicsFramesAsync(18);
            Require(Additional.ReadAmmo(16) < ammo && Ports.Bomb.ReadEffects().ParticleTypes.Contains(12), "Pyrocitor did not sustain flame/ammo");
            Require(Additional.Read().RenderedParticles > 0 && Additional.Read().ParticleDrawCalls < Additional.Read().RenderedParticles,
                "Pyrocitor particles did not use batched rendering");
            await CaptureRac1BombAsync("pyrocitor-stream", Player.Read().GlobalPosition + EquipmentForward * 1.5f + Vector3.UnitY * .7f);
            Driver.SetAction(MovementDiagnosticDriver.Traversal, false);
            await PhysicsFramesAsync(35);
            Require(!Targets.Read(crates[1].Key).Active && !Additional.Read().PyroEmitting && !Ports.Bomb.ReadEffects().ParticleTypes.Contains(12), "Pyrocitor contact or stop lifecycle");
            Log("[weapons-smoke] Pyrocitor travelling flame, damage and stop PASS");

            Ports.Equipment.Select(Rac1WeaponId.MineGlove); await PhysicsFramesAsync(8);
            ammo = Additional.ReadAmmo(17); Ports.Combat.RequestPrimaryAttack();
            await Rac1SmokeWaitAsync(() => Additional.ReadShots().Any(shot => shot.Read().Weapon == Weapon(Rac1WeaponId.MineGlove) && shot.Read().Grounded), 180, "mine ground contact");
            var mine = Additional.ReadShots().Single(shot => shot.Read().Weapon == Weapon(Rac1WeaponId.MineGlove));
            long mineId = mine.Read().Id;
            Require(Additional.ReadAmmo(17) == ammo - 1, "mine ammo");
            await PhysicsFramesAsync(15);
            Require(mine.Read().Model.Sequence == 2, "mine did not enter animated armed sequence");
            float footOffset = mine.Read().Model.FootOffset ?? throw new DiagnosticUnavailableException("The armed mine model has no native foot offset.");
            Require(mine.Read().Position.Y - footOffset >= fixture.GlobalPosition.Y - .02f,
                "grounded mine is embedded in the floor");
            await CaptureRac1BombAsync("mine-armed", mine.Read().Position);
            Targets.StageCrate(crates[2].Key, mine.Read().Position + new Vector3(.7f, -.05f, 0)); await PhysicsFramesAsync(3);
            var mineVictim = Targets.ReadEnemies().First(enemy => enemy.Key.Target.NativeClassId == 749 && !Targets.Read(enemy.Key).RewardEmitted);
            Targets.StageEnemy(mineVictim.Key, mine.Read().Position + new Vector3(1.5f, -.18f, 0));
            await Rac1SmokeWaitAsync(() => !Additional.ReadShots().Any(shot => shot.Read().Id == mineId), 120, "mine proximity/seek detonation");
            Require(!Targets.Read(crates[2].Key).Active, "mine source-owned blast failed");
            Require(Ports.Bomb.ReadEffects().ParticleTypes.Contains(16), "ground mine did not select native dust/fire/smoke sprays");
            await PhysicsFramesAsync(6);
            await CaptureRac1BombAsync("mine-explosion", mine.Read().Position);
            Log("[weapons-smoke] Mine flight, ground/armed animation and blast PASS");

            Ports.Equipment.Select(Rac1WeaponId.GloveOfDoom); await PhysicsFramesAsync(8);
            ammo = Additional.ReadAmmo(20); Ports.Combat.RequestPrimaryAttack();
            await Rac1SmokeWaitAsync(() => Additional.ReadDoomAgents().Length == 4, 210, "four Doom agents");
            Require(Additional.ReadAmmo(20) == ammo - 1 && !Additional.ReadShots().Any(shot => shot.Read().Weapon == Weapon(Rac1WeaponId.GloveOfDoom)), "Doom canister did not retire after four agents");
            Require(Additional.ReadDoomAgents().All(bot => bot.Read().Model.HasMeshes), "Doom bots lack retail skin");
            var wandering = Additional.ReadDoomAgents()[0]; Vector3 wanderStart = wandering.Read().Position;
            Vector3 initialWander = FromEffectNative(wandering.Read().NativeWander);
            await PhysicsFramesAsync(40);
            Vector3 travelled = wandering.Read().Position - wanderStart; travelled.Y = 0;
            Require(travelled.Length() > 1.8f && Vector3.Dot(travelled.Normalized(), initialWander) > .9f,
                "Doom floor contact turns its wander direction into a circle");
            await CaptureRac1BombAsync("doom-bots", Additional.ReadDoomAgents()[0].Read().Position);
            var doomVictim = Targets.ReadEnemies().First(enemy => enemy.Key.Target.NativeClassId == 749 && !Targets.Read(enemy.Key).RewardEmitted);
            Targets.StageEnemy(doomVictim.Key, Additional.ReadDoomAgents()[0].Read().Position + new Vector3(1.5f, 0, 0));
            await Rac1SmokeWaitAsync(() => Targets.Read(doomVictim.Key).RewardEmitted, 120, "Doom agent seeking/contact damage");
            Log("[weapons-smoke] Doom canister, four spaced animated bots and accounting PASS");

            Ports.Equipment.Select(Rac1WeaponId.SuckCannon); await PhysicsFramesAsync(8);
            Targets.StageCrate(crates[3].Key, Player.Read().GlobalPosition + EquipmentForward * 3); await PhysicsFramesAsync(3);
            Driver.SetAction(MovementDiagnosticDriver.Traversal, true); await PhysicsFramesAsync(6);
            Require(Additional.Read().Cargo == 1 && !Targets.Read(crates[3].Key).Active, "Suck Cannon failed capture/removal");
            await CaptureRac1BombAsync("suck-capture", Player.Read().GlobalPosition + EquipmentForward * 1.5f + Vector3.UnitY * .6f);
            Driver.SetAction(MovementDiagnosticDriver.Traversal, false); await PhysicsFramesAsync(20);
            Require(Additional.ReadAmmo(9) == 0, "capture wrote ordinary ammo");
            var state = Ports.Reproduction.CaptureBoundary();
            Require(state is { WorldPresent: true, CapturedMobies: 1, WeaponPreview: true }, "state lost cargo/preview boundary");
            Driver.Own(await Ports.Reproduction.SaveAndReloadAsync("weapon-smoke-state.json", Driver.Lifetime));
            Require(Additional.Read().Cargo == 1 && Additional.Read().Preview &&
                !Additional.Read().CampaignOwnsBlaster, "reload lost cargo or exported preview inventory");
            Ports.Combat.RequestPrimaryAttack(); await PhysicsFramesAsync(2);
            Require(Additional.Read().Cargo == 0 && Additional.ReadShots().Any(shot => shot.Read().Weapon == Weapon(Rac1WeaponId.SuckCannon)), "Suck Cannon did not release real captured model");
            await CaptureRac1BombAsync("suck-release", Player.Read().GlobalPosition + EquipmentForward * 2 + Vector3.UnitY * .6f);
            await PhysicsFramesAsync(120);
            Require(!Additional.ReadShots().Any(shot => shot.Read().Weapon == Weapon(Rac1WeaponId.SuckCannon)), "captured projectile leaked");
            Log("[weapons-smoke] Suck capture, separate cargo/HUD/save and released Moby PASS");
            await new Rac1ArsenalScenario(Context, Ports).RunAsync();

        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogError($"[weapons-smoke] FAIL: {ex}"); throw;
        }
        finally { Driver.SetAction(MovementDiagnosticDriver.Traversal, false); CleanupStep(() => fixture?.Dispose()); }
    }

    private async Task RunRac1WeaponIdleAndAudioSmokeAsync()
    {
        int idleShots = Additional.Read().ShotsFired;
        foreach (var profile in Rac1WeaponProfiles.Additional)
        {
            Ports.Equipment.Select(profile.Item); await PhysicsFramesAsync(85);
            Require(Additional.Read().UseTicks == 0 && Additional.Read().Loop is null && Additional.Read().ShotsFired == idleShots,
                $"{profile.Name} fires/plays a firing loop while idle");
            Require(Additional.ReadHeld(profile.Item).Sequence == 1, $"{profile.Name} failed return to static neutral");
            await CaptureRac1BombAsync(profile.Key + "-neutral", Player.Read().GlobalPosition + Vector3.UnitY * .8f, true);
        }
        Log("[weapons-smoke] every held weapon remains neutral and silent with no input PASS");
        foreach (var entry in new[] { (Rac1WeaponId.Pyrocitor, 176, 1), (Rac1WeaponId.Blaster, 168, 1),
            (Rac1WeaponId.SuckCannon, 849, 2), (Rac1WeaponId.TeslaClaw, 177, 4), (Rac1WeaponId.MorphORay, 185, 0) })
        {
            Ports.Equipment.Select(entry.Item1); await PhysicsFramesAsync(85);
            int before = Additional.ReadSoundTrace().Length;
            Driver.SetAction(MovementDiagnosticDriver.Traversal, true); await PhysicsFramesAsync(15);
            Require(Additional.Read().Loop == (entry.Item2, entry.Item3), $"{entry.Item1} has no sustained firing voice");
            Require(Additional.SourceVoiceAudible(entry.Item2, entry.Item3), $"{entry.Item1} voice is absent/silent");
            if (entry.Item1 is Rac1WeaponId.TeslaClaw or Rac1WeaponId.MorphORay)
                Require(Arsenal.Read().VisibleBeams.Any(visible => visible), $"{entry.Item1} has no emitter feedback without a target");
            await CaptureRac1BombAsync(Rac1WeaponProfiles.Get(entry.Item1).Key + "-sustain",
                Player.Read().GlobalPosition + Vector3.UnitY * .8f, true);
            Driver.SetAction(MovementDiagnosticDriver.Traversal, false); await PhysicsFramesAsync(45);
            Require(Additional.Read().Loop is null && Additional.Read().UseTicks == 0, $"{entry.Item1} sound/pose survived release");
            Require(Additional.ReadSoundTrace().Skip(before).Count(cue => cue.Class == entry.Item2 && cue.Sound == entry.Item3 && cue.Loop) == 1,
                $"{entry.Item1} restarted/stacked its sustained voice");
        }
        Ports.Equipment.Select(Rac1WeaponId.Blaster); await PhysicsFramesAsync(85);
        int trace = Additional.ReadSoundTrace().Length, shots = Additional.Read().ShotsFired;
        Ports.Combat.RequestPrimaryAttack(); await PhysicsFramesAsync(2);
        Require(Additional.Read().Loop == (168, 1), "Blaster tap has no voice");
        await PhysicsFramesAsync(10);
        Require(Additional.Read().Loop is null && Additional.Read().ShotsFired == shots + 1 &&
            Additional.ReadSoundTrace().Skip(trace).Count(cue => cue.Class == 168 && cue.Sound == 1) == 1,
            "Blaster tap plays multiple full firing samples or leaves its burst running");
        Log("[weapons-smoke] all continuous voices start once, emit feedback, and stop on release; single Blaster tap stops before four-shot tail PASS");
        Ports.Equipment.Select(Rac1WeaponId.Taunter); await PhysicsFramesAsync(85);
        trace = Additional.ReadSoundTrace().Length;
        Driver.SetAction(MovementDiagnosticDriver.Traversal, true); await PhysicsFramesAsync(180);
        Driver.SetAction(MovementDiagnosticDriver.Traversal, false); await PhysicsFramesAsync(10);
        Require(Additional.ReadSoundTrace().Skip(trace).Where(cue => cue.Class == 175 && cue.Sound < 6).Select(cue => cue.Sound).Distinct().Count() >= 2,
            "Taunter does not advance its actual sound steps");
        Log("[weapons-smoke] Taunter actual sound-step scheduling PASS");
    }
}
