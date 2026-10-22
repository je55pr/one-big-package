using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private async Task RunRac1ArsenalSmokeAsync()
    {
        RuntimeWorldScene.DynamicObjectNode Crate(float distance)
        {
            var node = _rac1CrateNodes.First(node => node.Source.NativeClassId == 500 && IsRac1GameplayActive(node));
            StageRac1CrateForSmoke(node, _player!.GlobalPosition + EquipmentForward * distance); return node;
        }
        async Task Equip(Rac1WeaponId weapon)
        {
            Input.ActionRelease(RawGamepadInput.Traversal);
            OnRac1WeaponSelectionRequested(weapon); await PhysicsFramesAsync(10);
        }
        await Equip(Rac1WeaponId.Devastator);
        var crate = Crate(7); await PhysicsFramesAsync(3);
        Vector3 rocketImpactFocus = crate.Root.GlobalPosition + Vector3.Up * .5f;
        int ammo = _rac1Weapons.GetAmmo(11); OnRac1PrimaryAttackRequested(); await PhysicsFramesAsync(3);
        Require(_rac1Weapons.GetAmmo(11) == ammo - 1 && _rac1ArsenalMissiles.Any(missile => missile.Weapon == Rac1WeaponId.Devastator), "Devastator launch/ammo");
        await CaptureRac1BombAsync("devastator-rocket", _player!.GlobalPosition + EquipmentForward * 3 + Vector3.Up);
        await Rac1SmokeWaitAsync(() => !IsRac1GameplayActive(crate), 90, "Devastator rocket/blast crate contact");
        Require(_rac1Particles?.Live.Any(p => p.Type == 8) == true && _rac1Particles.Live.Any(p => p.Type == 15),
            "rocket impact reused the Bomb Glove recipe instead of its puffs/sparks");
        await PhysicsFramesAsync(6);
        await CaptureRac1BombAsync("devastator-explosion", rocketImpactFocus);
        Require(_rac1WeaponSoundTrace.Any(cue => cue.Class == 157 && cue.Sound == 0), "Devastator firing sound absent");
        GD.Print("[arsenal-smoke] Devastator retail rocket, travel and blast PASS");

        await Equip(Rac1WeaponId.VisibombGun);
        crate = Crate(8); await PhysicsFramesAsync(3);
        ammo = _rac1Weapons.GetAmmo(13); OnRac1PrimaryAttackRequested(); await PhysicsFramesAsync(3);
        Require(_player.Rac1RemoteControlActive && _rac1MissileCamera?.Current == true && _rac1Weapons.GetAmmo(13) == ammo - 1,
            "Visibomb control handoff/ammo");
        await CaptureRac1BombAsync("visibomb-flight", _player.GlobalPosition + EquipmentForward * 3 + Vector3.Up);
        await Rac1SmokeWaitAsync(() => !_rac1ArsenalMissiles.Any(missile => missile.Weapon == Rac1WeaponId.VisibombGun), 100, "Visibomb impact");
        Require(!_player.Rac1RemoteControlActive && _rac1MissileCamera is null && _player.Camera.Current && !IsRac1GameplayActive(crate),
            "Visibomb blast/camera restoration");
        GD.Print("[arsenal-smoke] Visibomb controlled flight, blast and camera restoration PASS");

        await Equip(Rac1WeaponId.Ryno);
        long before = _rac1ArsenalId; ammo = _rac1Weapons.GetAmmo(23);
        OnRac1PrimaryAttackRequested(); await PhysicsFramesAsync(58);
        Require(_rac1RynoSalvo.Remaining == 0 && _rac1ArsenalId - before == 7 && _rac1Weapons.GetAmmo(23) == ammo - 1, "RYNO seven-missile salvo/accounting");
        Require(_rac1WeaponSoundTrace.Count(cue => cue.Class == 457 && cue.Sound == 1) == 7, "RYNO launch sounds are not source rocket voices");
        Require(_rac1ArsenalMissiles.Where(missile => missile.Weapon == Rac1WeaponId.Ryno)
            .All(missile => missile.View.NativeClass == 457 && missile.View.Scale == Vector3.One * .25f), "RYNO missile identity/quarter-scale");
        await CaptureRac1BombAsync("ryno-salvo", _player.GlobalPosition + EquipmentForward * 6 + Vector3.Up);
        await PhysicsFramesAsync(300);
        Require(_rac1ArsenalMissiles.Count == 0, "expired rockets leaked");
        GD.Print("[arsenal-smoke] RYNO seven staggered retail missiles and expiry PASS");

        await Equip(Rac1WeaponId.DecoyGlove); ammo = _rac1Weapons.GetAmmo(25);
        OnRac1PrimaryAttackRequested(); await Rac1SmokeWaitAsync(() => _rac1Decoys.Any(decoy => decoy.Grounded), 150, "Decoy landing");
        var decoy = _rac1Decoys.First();
        await PhysicsFramesAsync(85);
        Require(decoy.View.Sequence == 2, "Decoy failed inflation-to-idle animation");
        var enemy = _rac1Enemies.Values.First(enemy => !enemy.RewardEmitted && enemy.Node.Source.NativeClassId == 749);
        PlaceWeaponSmokeEnemy(enemy, decoy.View.GlobalPosition + Vector3.Right * 3);
        Require(ArsenalEnemyGoal(enemy, _player.GlobalPosition) == decoy.View.GlobalPosition && _rac1Weapons.GetAmmo(25) == ammo - 1,
            "Decoy did not attract enemy or account ammo");
        await CaptureRac1BombAsync("decoy-deployed", decoy.View.GlobalPosition + Vector3.Up * .5f);
        PlaceWeaponSmokeEnemy(enemy, _player.GlobalPosition + EquipmentForward * 20);
        int inflationSounds = _rac1WeaponSoundTrace.Count(cue => cue.Class == 203 && cue.Sound == 1);
        Require(inflationSounds == 1, "Decoy inflation sound replayed");
        decoy.GroundTicks = Rac1ArsenalRules.DecoyLifetime - 1; await PhysicsFramesAsync(2);
        Require(_rac1Decoys.Count == 1 && decoy.RetiringTicks >= 0 && decoy.View.Sequence == 0,
            "Decoy expiry removed the model without folding");
        await PhysicsFramesAsync(30);
        Require(_rac1Decoys.Count == 1, "Decoy folded away before its native60-tick transition");
        await PhysicsFramesAsync(32);
        Require(_rac1Decoys.Count == 0, "spent decoy leaked");
        GD.Print("[arsenal-smoke] Decoy native model, ground placement and enemy attraction PASS");

        await Equip(Rac1WeaponId.DroneDevice); ammo = _rac1Weapons.GetAmmo(24);
        OnRac1PrimaryAttackRequested(); await PhysicsFramesAsync(4);
        Require(_rac1Drones.Count == 6 && _rac1Weapons.GetAmmo(24) == ammo - 1, "Drone formation/ammo");
        await CaptureRac1BombAsync("drone-formation", _player.GlobalPosition + Vector3.Up);
        PlaceWeaponSmokeEnemy(enemy, _player.GlobalPosition + EquipmentForward * 4);
        await Rac1SmokeWaitAsync(() => enemy.RewardEmitted, 120, "Drone interception");
        Require(_rac1Drones.Count < 6, "intercepting drone was not consumed");
        foreach (var drone in _rac1Drones) drone.View.QueueFree(); _rac1Drones.Clear();
        GD.Print("[arsenal-smoke] Six retail drones, enemy interception and consumption PASS");

        // Let the interception effects drain so the Tesla capture actually
        // shows its ribbons rather than a previous weapon's blast.
        await PhysicsFramesAsync(180);

        await Equip(Rac1WeaponId.TeslaClaw);
        enemy = _rac1Enemies.Values.First(enemy => !enemy.RewardEmitted && enemy.Node.Source.NativeClassId == 1440);
        PlaceWeaponSmokeEnemy(enemy, _player.GlobalPosition + EquipmentForward * 6); ammo = _rac1Weapons.GetAmmo(19);
        _rac1WeaponWorldTick = 0;
        Input.ActionPress(RawGamepadInput.Traversal); await PhysicsFramesAsync(4);
        Require(_rac1ArsenalBeams.Count >= 4 && _rac1ArsenalBeams.Take(4).All(beam => beam.MaterialOverride is ShaderMaterial) &&
            _rac1Weapons.GetAmmo(19) < ammo, "Tesla textured core/glow layers/ammo");
        await CaptureRac1BombAsync("tesla-arcs", _player.GlobalPosition + EquipmentForward * 2 + Vector3.Up);
        await Rac1SmokeWaitAsync(() => enemy.RewardEmitted, 100, "Tesla contact damage");
        Input.ActionRelease(RawGamepadInput.Traversal); await PhysicsFramesAsync(2);
        Require(_rac1ArsenalBeams.All(beam => !beam.Visible), "Tesla arc persisted after release");
        Require(_rac1WeaponSoundTrace.Any(cue => cue.Class == 177 && cue.Sound == 1), "Tesla lock acquisition sound absent");
        GD.Print("[arsenal-smoke] Tesla lock-on arcs, ammo, damage and release PASS");

        await Equip(Rac1WeaponId.MorphORay);
        enemy = _rac1Enemies.Values.First(enemy => !enemy.RewardEmitted && enemy.Node.Source.NativeClassId == 749);
        PlaceWeaponSmokeEnemy(enemy, _player.GlobalPosition + EquipmentForward * 6);
        Input.ActionPress(RawGamepadInput.Traversal);
        await Rac1SmokeWaitAsync(() => _rac1Chickens.ContainsKey(enemy.Node.Source.InstanceIndex), 120, "Morph-o-Ray transformation");
        Input.ActionRelease(RawGamepadInput.Traversal);
        Require(!enemy.RewardEmitted && IsRac1GameplayActive(enemy.Node), "Morph incorrectly killed or rewarded the victim");
        Require(CaptureMovementState().World!.MorphedMobies!.Any(key => key.InstanceIndex == enemy.Node.Source.InstanceIndex),
            "Movement state lost the transformed victim");
        await CaptureRac1BombAsync("morph-chicken", enemy.Node.Root.GlobalPosition + Vector3.Up * .3f);
        GD.Print("[arsenal-smoke] Morph continuous exposure, real animated chicken and harmless victim PASS");

        await Equip(Rac1WeaponId.Walloper); crate = Crate(1.3f); await PhysicsFramesAsync(3);
        OnRac1PrimaryAttackRequested(); await PhysicsFramesAsync(3);
        Require(IsRac1GameplayActive(crate), "Walloper damaged before its attack window");
        await Rac1SmokeWaitAsync(() => !IsRac1GameplayActive(crate), 25, "Walloper punch window");
        await CaptureRac1BombAsync("walloper-punch", _player.GlobalPosition + EquipmentForward * .6f + Vector3.Up);
        await PhysicsFramesAsync(12);
        Require(_rac1WeaponSoundTrace.Any(cue => cue.Class == 0 && cue.Sound == 30), "Walloper source punch sound absent");
        GD.Print("[arsenal-smoke] Walloper hand, punch animation and delayed contact PASS");

        await Equip(Rac1WeaponId.Taunter); crate = Crate(5); await PhysicsFramesAsync(3);
        Input.ActionPress(RawGamepadInput.Traversal);
        await Rac1SmokeWaitAsync(() => !IsRac1GameplayActive(crate), 90, "Taunter crate interaction");
        await CaptureRac1BombAsync("taunter-rings", _player.GlobalPosition + EquipmentForward + Vector3.Up);
        Input.ActionRelease(RawGamepadInput.Traversal);
        GD.Print("[arsenal-smoke] Taunter rings, forward crate query and no invented ammo PASS");
        var state = CaptureMovementState();
        string repro = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "captures", "repros", "arsenal-smoke-state.json"));
        MovementReproStateFile.Save(repro, state); await LoadMovementStateAsync(repro);
        Require(_rac1Chickens.Count == 1 && _rac1Chickens.Values.All(chicken => IsRac1GameplayActive(chicken.Enemy.Node) && !chicken.Enemy.RewardEmitted),
            "Morph state did not survive an actual world reload");
        Require(_rac1ArsenalMissiles.Count == 0 && _rac1Decoys.Count == 0 && _rac1Drones.Count == 0 && _rac1MissileCamera is null,
            "transient arsenal objects survived world reconstruction");
        GD.Print("[arsenal-smoke] Morph world reload and transient controller cleanup PASS");
        var chickenVictim = _rac1Chickens.Values.Single();
        await Equip(Rac1WeaponId.SuckCannon);
        PlaceWeaponSmokeEnemy(chickenVictim.Enemy, _player!.GlobalPosition + EquipmentForward * 3);
        Input.ActionPress(RawGamepadInput.Traversal);
        await Rac1SmokeWaitAsync(() => _rac1AdditionalWeapons!.Cargo.Count == 1, 40, "Suck Cannon chicken capture");
        Input.ActionRelease(RawGamepadInput.Traversal); await PhysicsFramesAsync(25);
        Require(_rac1ChickenCargo.Count == 1, "captured chicken lost its presentation identity");
        MovementReproStateFile.Save(repro, CaptureMovementState()); await LoadMovementStateAsync(repro);
        Require(_rac1ChickenCargo.Count == 1 && _rac1Chickens.Count == 0, "captured chicken reverted on reload");
        OnRac1PrimaryAttackRequested(); await PhysicsFramesAsync(2);
        Require(_rac1EquipmentShots.Any(shot => shot.Weapon == Rac1WeaponId.SuckCannon &&
            shot.Node is Rac1EquipmentView { NativeClass: 270 }) && _rac1ChickenCargo.Count == 0,
            "Suck Cannon restored the original species instead of its captured chicken");
        GD.Print("[arsenal-smoke] Morph/capture/reload/release preserves chicken model PASS");
    }
}
