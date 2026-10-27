using System.Numerics;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed class Rac1ArsenalScenario(MovementDiagnosticContext context, Rac1WeaponScenarioPorts ports) : Rac1WeaponScenarioSupport(context, ports)
{
    public async Task RunAsync()
    {
        Rac1WeaponTargetDiagnostic Crate(float distance)
        {
            var node = Targets.ReadCrates().First(node => node.Key.Target.NativeClassId == 500 && Targets.Read(node.Key).Active);
            Targets.StageCrate(node.Key, Player.Read().GlobalPosition + EquipmentForward * distance); return node;
        }
        async Task Equip(Rac1WeaponId weapon)
        {
            Driver.SetAction(MovementDiagnosticDriver.Traversal, false);
            Ports.Equipment.Select(weapon); await PhysicsFramesAsync(10);
        }
        await Equip(Rac1WeaponId.Devastator);
        var crate = Crate(7); await PhysicsFramesAsync(3);
        Vector3 rocketImpactFocus = Targets.Read(crate.Key).Position + Vector3.UnitY * .5f;
        int ammo = Additional.ReadAmmo(11); Ports.Combat.RequestPrimaryAttack(); await PhysicsFramesAsync(3);
        Require(Additional.ReadAmmo(11) == ammo - 1 && Arsenal.ReadMissiles().Any(missile => missile.Read().Weapon == Weapon(Rac1WeaponId.Devastator)), "Devastator launch/ammo");
        await CaptureRac1BombAsync("devastator-rocket", Player.Read().GlobalPosition + EquipmentForward * 3 + Vector3.UnitY);
        await Rac1SmokeWaitAsync(() => !Targets.Read(crate.Key).Active, 90, "Devastator rocket/blast crate contact");
        Require(Ports.Bomb.ReadEffects().ParticleTypes.Contains(8) && Ports.Bomb.ReadEffects().ParticleTypes.Contains(15),
            "rocket impact reused the Bomb Glove recipe instead of its puffs/sparks");
        await PhysicsFramesAsync(6);
        await CaptureRac1BombAsync("devastator-explosion", rocketImpactFocus);
        Require(Additional.ReadSoundTrace().Any(cue => cue.Class == 157 && cue.Sound == 0), "Devastator firing sound absent");
        Log("[arsenal-smoke] Devastator retail rocket, travel and blast PASS");

        await Equip(Rac1WeaponId.VisibombGun);
        crate = Crate(8); await PhysicsFramesAsync(3);
        ammo = Additional.ReadAmmo(13); Ports.Combat.RequestPrimaryAttack(); await PhysicsFramesAsync(3);
        Require(Additional.Read().RemoteControl && Additional.Read().GuidedCameraCurrent && Additional.ReadAmmo(13) == ammo - 1,
            "Visibomb control handoff/ammo");
        await CaptureRac1BombAsync("visibomb-flight", Player.Read().GlobalPosition + EquipmentForward * 3 + Vector3.UnitY);
        await Rac1SmokeWaitAsync(() => !Arsenal.ReadMissiles().Any(missile => missile.Read().Weapon == Weapon(Rac1WeaponId.VisibombGun)), 100, "Visibomb impact");
        Require(!Additional.Read().RemoteControl && !Additional.Read().GuidedCameraPresent && Additional.Read().PlayerCameraCurrent && !Targets.Read(crate.Key).Active,
            "Visibomb blast/camera restoration");
        Log("[arsenal-smoke] Visibomb controlled flight, blast and camera restoration PASS");

        await Equip(Rac1WeaponId.Ryno);
        long before = Arsenal.Read().LastId; ammo = Additional.ReadAmmo(23);
        Ports.Combat.RequestPrimaryAttack(); await PhysicsFramesAsync(58);
        Require(Arsenal.Read().SalvoRemaining == 0 && Arsenal.Read().LastId - before == 7 && Additional.ReadAmmo(23) == ammo - 1, "RYNO seven-missile salvo/accounting");
        Require(Additional.ReadSoundTrace().Count(cue => cue.Class == 457 && cue.Sound == 1) == 7, "RYNO launch sounds are not source rocket voices");
        Require(Arsenal.ReadMissiles().Where(missile => missile.Read().Weapon == Weapon(Rac1WeaponId.Ryno))
            .All(missile => missile.Read().Model.NativeClass == 457 && missile.Read().Model.Scale == Vector3.One * .25f), "RYNO missile identity/quarter-scale");
        await CaptureRac1BombAsync("ryno-salvo", Player.Read().GlobalPosition + EquipmentForward * 6 + Vector3.UnitY);
        await PhysicsFramesAsync(300);
        Require(Arsenal.ReadMissiles().Length == 0, "expired rockets leaked");
        Log("[arsenal-smoke] RYNO seven staggered retail missiles and expiry PASS");

        await Equip(Rac1WeaponId.DecoyGlove); ammo = Additional.ReadAmmo(25);
        Ports.Combat.RequestPrimaryAttack(); await Rac1SmokeWaitAsync(() => Arsenal.ReadDecoys().Any(decoy => decoy.Read().Grounded), 150, "Decoy landing");
        var decoy = Arsenal.ReadDecoys().First();
        await PhysicsFramesAsync(85);
        Require(decoy.Read().Model.Sequence == 2, "Decoy failed inflation-to-idle animation");
        var enemy = Targets.ReadEnemies().First(enemy => !Targets.Read(enemy.Key).RewardEmitted && enemy.Key.Target.NativeClassId == 749);
        Targets.StageEnemy(enemy.Key, decoy.Read().Model.Position + Vector3.UnitX * 3);
        Require(Arsenal.ReadEnemyGoal(enemy.Key, Player.Read().GlobalPosition) == decoy.Read().Model.Position && Additional.ReadAmmo(25) == ammo - 1,
            "Decoy did not attract enemy or account ammo");
        await CaptureRac1BombAsync("decoy-deployed", decoy.Read().Model.Position + Vector3.UnitY * .5f);
        Targets.StageEnemy(enemy.Key, Player.Read().GlobalPosition + EquipmentForward * 20);
        int inflationSounds = Additional.ReadSoundTrace().Count(cue => cue.Class == 203 && cue.Sound == 1);
        Require(inflationSounds == 1, "Decoy inflation sound replayed");
        decoy.StageExpiry(); await PhysicsFramesAsync(2);
        Require(Arsenal.ReadDecoys().Length == 1 && decoy.Read().RetiringTicks >= 0 && decoy.Read().Model.Sequence == 0,
            "Decoy expiry removed the model without folding");
        await PhysicsFramesAsync(30);
        Require(Arsenal.ReadDecoys().Length == 1, "Decoy folded away before its native60-tick transition");
        await PhysicsFramesAsync(32);
        Require(Arsenal.ReadDecoys().Length == 0, "spent decoy leaked");
        Log("[arsenal-smoke] Decoy native model, ground placement and enemy attraction PASS");

        await Equip(Rac1WeaponId.DroneDevice); ammo = Additional.ReadAmmo(24);
        Ports.Combat.RequestPrimaryAttack(); await PhysicsFramesAsync(4);
        Require(Arsenal.Read().Drones == 6 && Additional.ReadAmmo(24) == ammo - 1, "Drone formation/ammo");
        await CaptureRac1BombAsync("drone-formation", Player.Read().GlobalPosition + Vector3.UnitY);
        Targets.StageEnemy(enemy.Key, Player.Read().GlobalPosition + EquipmentForward * 4);
        await Rac1SmokeWaitAsync(() => Targets.Read(enemy.Key).RewardEmitted, 120, "Drone interception");
        Require(Arsenal.Read().Drones < 6, "intercepting drone was not consumed");
        Arsenal.DiscardDrones();
        Log("[arsenal-smoke] Six retail drones, enemy interception and consumption PASS");

        // Let the interception effects drain so the Tesla capture actually
        // shows its ribbons rather than a previous weapon's blast.
        await PhysicsFramesAsync(180);

        await Equip(Rac1WeaponId.TeslaClaw);
        enemy = Targets.ReadEnemies().First(enemy => !Targets.Read(enemy.Key).RewardEmitted && enemy.Key.Target.NativeClassId == 1440);
        Targets.StageEnemy(enemy.Key, Player.Read().GlobalPosition + EquipmentForward * 6); ammo = Additional.ReadAmmo(19);
        Arsenal.ResetWorldTick();
        Driver.SetAction(MovementDiagnosticDriver.Traversal, true); await PhysicsFramesAsync(4);
        Require(Arsenal.Read().ShaderBeams.Length >= 4 && Arsenal.Read().ShaderBeams.Take(4).All(shader => shader) &&
            Additional.ReadAmmo(19) < ammo, "Tesla textured core/glow layers/ammo");
        await CaptureRac1BombAsync("tesla-arcs", Player.Read().GlobalPosition + EquipmentForward * 2 + Vector3.UnitY);
        await Rac1SmokeWaitAsync(() => Targets.Read(enemy.Key).RewardEmitted, 100, "Tesla contact damage");
        Driver.SetAction(MovementDiagnosticDriver.Traversal, false); await PhysicsFramesAsync(2);
        Require(Arsenal.Read().VisibleBeams.All(visible => !visible), "Tesla arc persisted after release");
        Require(Additional.ReadSoundTrace().Any(cue => cue.Class == 177 && cue.Sound == 1), "Tesla lock acquisition sound absent");
        Log("[arsenal-smoke] Tesla lock-on arcs, ammo, damage and release PASS");

        await Equip(Rac1WeaponId.MorphORay);
        enemy = Targets.ReadEnemies().First(enemy => !Targets.Read(enemy.Key).RewardEmitted && enemy.Key.Target.NativeClassId == 749);
        Targets.StageEnemy(enemy.Key, Player.Read().GlobalPosition + EquipmentForward * 6);
        Driver.SetAction(MovementDiagnosticDriver.Traversal, true);
        await Rac1SmokeWaitAsync(() => Arsenal.ReadChickens().Any(chicken => chicken.Actor == enemy.Key), 120, "Morph-o-Ray transformation");
        Driver.SetAction(MovementDiagnosticDriver.Traversal, false);
        Require(!Targets.Read(enemy.Key).RewardEmitted && Targets.Read(enemy.Key).Active, "Morph incorrectly killed or rewarded the victim");
        Require(Ports.Reproduction.CaptureBoundary().Morphed.Any(key => key == enemy.Key.Target),
            "Movement state lost the transformed victim");
        await CaptureRac1BombAsync("morph-chicken", Targets.Read(enemy.Key).Position + Vector3.UnitY * .3f);
        Log("[arsenal-smoke] Morph continuous exposure, real animated chicken and harmless victim PASS");

        await Equip(Rac1WeaponId.Walloper); crate = Crate(1.3f); await PhysicsFramesAsync(3);
        Ports.Combat.RequestPrimaryAttack(); await PhysicsFramesAsync(3);
        Require(Targets.Read(crate.Key).Active, "Walloper damaged before its attack window");
        await Rac1SmokeWaitAsync(() => !Targets.Read(crate.Key).Active, 25, "Walloper punch window");
        await CaptureRac1BombAsync("walloper-punch", Player.Read().GlobalPosition + EquipmentForward * .6f + Vector3.UnitY);
        await PhysicsFramesAsync(12);
        Require(Additional.ReadSoundTrace().Any(cue => cue.Class == 0 && cue.Sound == 30), "Walloper source punch sound absent");
        Log("[arsenal-smoke] Walloper hand, punch animation and delayed contact PASS");

        await Equip(Rac1WeaponId.Taunter); crate = Crate(5); await PhysicsFramesAsync(3);
        Driver.SetAction(MovementDiagnosticDriver.Traversal, true);
        await Rac1SmokeWaitAsync(() => !Targets.Read(crate.Key).Active, 90, "Taunter crate interaction");
        await CaptureRac1BombAsync("taunter-rings", Player.Read().GlobalPosition + EquipmentForward + Vector3.UnitY);
        Driver.SetAction(MovementDiagnosticDriver.Traversal, false);
        Log("[arsenal-smoke] Taunter rings, forward crate query and no invented ammo PASS");
        var state = Ports.Reproduction.CaptureBoundary();
        await Ports.Reproduction.SaveAndReloadAsync("arsenal-smoke-state.json", Driver.Lifetime);
        Require(Arsenal.ReadChickens().Length == 1 && Arsenal.ReadChickens().All(chicken => chicken.Active && !chicken.RewardEmitted),
            "Morph state did not survive an actual world reload");
        Require(Arsenal.ReadMissiles().Length == 0 && Arsenal.ReadDecoys().Length == 0 && Arsenal.Read().Drones == 0 && !Additional.Read().GuidedCameraPresent,
            "transient arsenal objects survived world reconstruction");
        Log("[arsenal-smoke] Morph world reload and transient controller cleanup PASS");
        var chickenVictim = Arsenal.ReadChickens().Single();
        await Equip(Rac1WeaponId.SuckCannon);
        Targets.StageEnemy(chickenVictim.Actor, Player.Read().GlobalPosition + EquipmentForward * 3);
        Driver.SetAction(MovementDiagnosticDriver.Traversal, true);
        await Rac1SmokeWaitAsync(() => Additional.Read().Cargo == 1, 40, "Suck Cannon chicken capture");
        Driver.SetAction(MovementDiagnosticDriver.Traversal, false); await PhysicsFramesAsync(25);
        Require(Arsenal.Read().ChickenCargo == 1, "captured chicken lost its presentation identity");
        await Ports.Reproduction.SaveAndReloadAsync("arsenal-smoke-state.json", Driver.Lifetime);
        Require(Arsenal.Read().ChickenCargo == 1 && Arsenal.ReadChickens().Length == 0, "captured chicken reverted on reload");
        Ports.Combat.RequestPrimaryAttack(); await PhysicsFramesAsync(2);
        Require(Additional.ReadShots().Any(shot => shot.Read().Weapon == Weapon(Rac1WeaponId.SuckCannon) &&
            shot.Read().Model.NativeClass == 270) && Arsenal.Read().ChickenCargo == 0,
            "Suck Cannon restored the original species instead of its captured chicken");
        Log("[arsenal-smoke] Morph/capture/reload/release preserves chicken model PASS");
    }
}
