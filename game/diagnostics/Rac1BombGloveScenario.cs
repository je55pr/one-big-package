using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

/// <summary>Staged Bomb Glove host integration; not a retail playthrough witness.</summary>
public sealed class Rac1BombGloveScenario(MovementDiagnosticContext context, IRac1BombAcceptanceFixture bomb,
    IRac1WeaponTargetFixture targets, IRac1CombatDiagnosticFixture combat, IDiagnosticControllerEquipment equipment,
    IWeaponDiagnosticCapture capture, IWeaponMovementStateFixture reproduction) : MovementScenarioSupport(context)
{
    public async Task RunAsync()
    {
        IDiagnosticGeometryLease? fixture = null;
        try
        {
            Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 0 }, "Bomb Glove smoke needs Veldin");
            using var frameRate = World.LimitFrameRate(60);
            ClearMovementSmokeInput();
            string? savedRepro = bomb.SavedReproductionPath;
            if (!string.IsNullOrWhiteSpace(savedRepro))
            {
                await RunRac1SavedBombReproAsync(savedRepro);
                
                return;
            }
            fixture = Geometry.Create("SyntheticBombGloveFixture", new(-2000, 64, 2600));
            fixture.AddMesh("BombFloor", 11,
                (new(-24, 0, -24), new(24, 0, -24), new(24, 0, 24)),
                (new(-24, 0, -24), new(24, 0, 24), new(-24, 0, 24)));
            await PhysicsFramesAsync(3);
            Vector3 start = fixture.GlobalPosition + new Vector3(6, .05f, 0);
            Player.ResetAt(start);
            await WaitForGroundedAsync(Player, 180);
            combat.PlacePlayerFacing(Player.Read().GlobalPosition, -Vector3.UnitX, true);
            equipment.Select(Rac1WeaponId.FirstRanged);
            await PhysicsFramesAsync(4);
            Require(bomb.ReadView().GloveVisible && bomb.ReadView().StagedVisible, "glove/staged bomb is absent");
            Require(bomb.ReadView() is { VertexCount: 1140, TriangleCount: 1200 }, "placeholder glove geometry");
            Require(!bomb.ReadView().WrenchVisible, "equipping Bomb Glove left wrench visible");
            Vector3 socket = bomb.ReadView().Socket;
            bool socketMoved = false;
            for (int tick = 0; tick < 24; tick++)
            {
                await PhysicsFramesAsync(1);
                socketMoved |= bomb.ReadView().Socket.DistanceTo(socket) > .001f;
                Require(bomb.ReadView().StagedFollowsSocket, "staged bomb lost animated glove socket");
            }
            Require(socketMoved, "idle glove attachment is frozen");
            await CaptureRac1BombAsync("held-glove", Player.Read().GlobalPosition + Vector3.UnitY * .8f, close: true);
            int ammo = equipment.Read().BombAmmo;
            combat.RequestPrimaryAttack();
            await PhysicsFramesAsync(2);
            Require(equipment.Read().BombAmmo == ammo - 1 && combat.ReadBombs().Projectiles.Length == 1, "fire did not consume exactly one round");
            Require(Animation.Read().Sequence == 44, "throw did not select retail sequence44");
            var shot = bomb.CaptureSingleShot();
            Require(shot.Read().OriginalModel, "projectile still uses procedural sphere");
            Vector3 initial = FromEffectNative(shot.Read().NativePosition);
            float peak = initial.Y;
            await CaptureRac1BombAsync("throw", Player.Read().GlobalPosition + Vector3.UnitY * .8f, close: true);
            while (shot.Read().Flying)
            {
                await PhysicsFramesAsync(1);
                peak = MathF.Max(peak, FromEffectNative(shot.Read().NativePosition).Y);
            }
            Vector3 impact = FromEffectNative(shot.Read().NativePosition);
            Require(peak > initial.Y + 1 && impact.Y < initial.Y, "bomb did not rise and fall onto floor");
            Require(bomb.ReadEffects().Fragments == 15 && bomb.ReadEffects().Bursts >= 3, "impact lacks retail fragments/layers");
            await CaptureRac1BombAsync("floor-impact", impact);
            await PhysicsFramesAsync(6);
            await CaptureRac1BombAsync("expanding-blast", impact);
            await Rac1SmokeWaitAsync(() => combat.ReadBombs().Projectiles.Length == 0 && Animation.Read().Sequence == 0, 90, "blast/throw recovery");
            Log("[bomb-glove-smoke] retail model/socket, sequence44, arc and floor impact PASS");

            var crates = targets.ReadCrates().Where(node => node.Active && node.Key.Target.NativeClassId == 500).Take(2).ToArray();
            Require(crates.Length == 2, "two live crate fixtures unavailable");
            targets.StageCrate(crates[0].Key, new(impact.X, fixture.GlobalPosition.Y, impact.Z - .7f));
            targets.StageCrate(crates[1].Key, new(impact.X, fixture.GlobalPosition.Y, impact.Z + .7f));
            await PhysicsFramesAsync(3);
            Require(bomb.HasLineOfSight(targets.Read(crates[0].Key).Position + new Vector3(.9f, .5f, .9f),
                targets.Read(crates[0].Key).Position + Vector3.UnitY * .5f), "crate's own diagonal bounds occluded its blast query");
            int destroyed = combat.ReadLoot().DestroyedCrates;
            combat.RequestPrimaryAttack();
            await Rac1SmokeWaitAsync(() => combat.ReadLoot().DestroyedCrates >= destroyed + 2, 150, "one blast reaching two crates");
            Require(equipment.Read().BombAmmo == ammo - 2, "blast spent additional ammo");
            Require(combat.ReadBombs().LastContact!.DamageResults.All(result => result.NativeDamage == 2), "blast damage differs from retail envelope");
            await PhysicsFramesAsync(35);
            Log("[bomb-glove-smoke] nearby-target blast and once-per-victim damage PASS");

            var hiddenCrate = targets.ReadCrates().First(node => node.Active && node.Key.Target.NativeClassId == 500);
            targets.StageCrate(hiddenCrate.Key, fixture.GlobalPosition + new Vector3(0, 0, 6));
            await PhysicsFramesAsync(3);
            Vector3 centre = targets.Read(hiddenCrate.Key).Position + Vector3.UnitY * .4f;
            Require(bomb.ProbeWorldContact(centre + -Vector3.UnitX * 2, centre + Vector3.UnitX * 2) is not null,
                "crate collision fixture was not active");
            combat.SetPresence(hiddenCrate.Key, OBP.Runtime.Gameplay.RuntimeEntityPresence.Inactive);
            Require(bomb.ProbeWorldContact(centre + -Vector3.UnitX * 2, centre + Vector3.UnitX * 2) is null,
                "inactive crate retained an invisible projectile obstruction");
            Log("[bomb-glove-smoke] inactive crate collision removal PASS");

            Vector3 wallPosition = fixture.GlobalPosition + new Vector3(3, 3, 0);
            var wall = Driver.Own(fixture.AddBox("BombWall", new(.15f, 6, 8), wallPosition, 0));
            var shieldedCrate = targets.ReadCrates().First(node => node.Active && node.Key.Target.NativeClassId == 500);
            targets.StageCrate(shieldedCrate.Key, fixture.GlobalPosition + new Vector3(2, 0, 0));
            await PhysicsFramesAsync(3);
            int impacts = bomb.ReadEffects().Impacts;
            combat.RequestPrimaryAttack();
            await Rac1SmokeWaitAsync(() => bomb.ReadEffects().Impacts > impacts, 40, "wall impact");
            var wallShot = bomb.CaptureSingleShot();
            Require(FromEffectNative(wallShot.Read().NativePosition).X > wallPosition.X, "bomb passed through wall");
            Require(!bomb.HasLineOfSight(FromEffectNative(wallShot.Read().NativePosition), targets.Read(shieldedCrate.Key).Position + Vector3.UnitY * .5f),
                "wall did not occlude the blast");
            await PhysicsFramesAsync(20);
            Require(targets.Read(shieldedCrate.Key).Active, "blast damaged a crate through a wall");
            wall.Dispose();
            equipment.Select(Rac1WeaponId.Wrench);
            Require(!bomb.ReadView().GloveVisible && !bomb.ReadView().StagedVisible && bomb.ReadView().WrenchVisible, "weapon switch presentation did not recover");
            await PhysicsFramesAsync(210);
            Require(combat.ReadBombs().Projectiles.Length == 0 && bomb.ReadEffects().Bursts == 0 && bomb.ReadEffects().Fragments == 0 && bomb.ReadEffects().Particles == 0,
                "bomb effect lifecycle leaked nodes");
            Log("[bomb-glove-smoke] wall contact, weapon switch and effect cleanup PASS");
            equipment.Select(Rac1WeaponId.FirstRanged);
            // A thin wall between body and muzzle must block the launch itself,
            // even if the muzzle endpoint has already crossed to its far side.
            Vector3 nearWallPosition = Player.Read().GlobalPosition + new Vector3(-.58f, 2, 0);
            var nearWall = Driver.Own(fixture.AddBox("ThinMuzzleWall", new(.08f, 4, 4), nearWallPosition, 0));
            await PhysicsFramesAsync(3);
            impacts = bomb.ReadEffects().Impacts;
            combat.RequestPrimaryAttack();
            await PhysicsFramesAsync(2);
            Require(bomb.ReadEffects().Impacts == impacts + 1 && bomb.CaptureSingleShot().Read().NativeState == 2 &&
                FromEffectNative(bomb.CaptureSingleShot().Read().NativePosition).X > nearWallPosition.X,
                "bomb spawned through a thin wall between Ratchet and the muzzle");
            nearWall.Dispose();
            await PhysicsFramesAsync(25);
            Log("[bomb-glove-smoke] body-to-muzzle clearance PASS");
            // Stage two remaining shots for this bounded empty-ammo check;
            // the ordinary opening now has twenty rounds rather than six.
            Require(equipment.Read().BombAmmo >= 2 &&
                bomb.ConsumeAmmo(equipment.Read().BombAmmo - 2),
                "unable to stage the held-fire ammo fixture");
            SetAnalogueSmokeInput(0, 1);
            Driver.SetAction(MovementDiagnosticDriver.Traversal, true);
            await PhysicsFramesAsync(45);
            Driver.SetAction(MovementDiagnosticDriver.Traversal, false);
            ClearMovementSmokeInput();
            Require(equipment.Read().BombAmmo == 0 && bomb.ReadView().GloveVisible && !bomb.ReadView().StagedVisible,
                "held firing did not follow 20-tick cadence or empty glove still displays a bomb");
            await PhysicsFramesAsync(150);
            Require(Animation.Read().Sequence == 0, "running throw did not recover standing");
            var controller = Animation.Read();
            Require(controller.OrdinaryStandingSample is not null || controller.OrdinaryStopSample is { Sequence: 0 },
                "throw recovery lost interpolated native standing clock");
            Log("[bomb-glove-smoke] held fire, empty ammo, run/throw/idle recovery PASS");
            Log("[bomb-glove-smoke] PASS");
            
        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogError($"[bomb-glove-smoke] FAIL: {exception.Message}\n{exception.StackTrace}");
            throw;
        }
        finally
        {
            ClearMovementSmokeInput();
            CleanupStep(() => fixture?.Dispose());
        }
    }

    private async Task RunRac1SavedBombReproAsync(string path)
    {
        await reproduction.LoadAsync(path, Driver.Lifetime);
        Require(!reproduction.Loading, "saved bomb setup did not load");
        equipment.Select(Rac1WeaponId.FirstRanged);
        await PhysicsFramesAsync(2);
        int destroyed = combat.ReadLoot().DestroyedCrates;
        combat.RequestPrimaryAttack();
        await PhysicsFramesAsync(2);
        Require(combat.ReadBombs().Projectiles.Length == 1, "saved bomb setup did not fire");
        var motion = bomb.CaptureSingleShot();
        Log($"[bomb-repro] launch position={FromEffectNative(motion.Read().NativePosition)} velocity={motion.Read().NativeVelocity}");
        for (int tick = 0; motion.Read().Flying && tick < 310; tick++) await PhysicsFramesAsync(1);
        Require(!motion.Read().Flying, "saved bomb never impacted");
        Vector3 impact = FromEffectNative(motion.Read().NativePosition);
        Log($"[bomb-repro] impact={impact}");
        var nearby = targets.ReadCrates().Where(node => node.Active)
            .OrderBy(node => Vector3.DistanceSquared(targets.Read(node.Key).Position + Vector3.UnitY * .5f, impact)).Take(3).ToArray();
        Require(nearby.Length > 0 && nearby.All(node =>
            (targets.Read(node.Key).Position + Vector3.UnitY * .5f).DistanceTo(impact) < 2.5f), "saved bomb setup has no nearby crate cluster");
        foreach (var crate in nearby)
        {
            Vector3 centre = targets.Read(crate.Key).Position + Vector3.UnitY * .5f;
            string obstruction = bomb.DescribeRay(impact, centre);
            Log($"[bomb-repro] crate={crate.Key.Target.InstanceIndex} active={targets.Read(crate.Key).Active} " +
                $"position={targets.Read(crate.Key).Position} distance={impact.DistanceTo(centre)} " +
                $"los={bomb.HasLineOfSight(impact, centre)} ray={obstruction}");
        }
        await CaptureRac1BombAsync("saved-impact", impact);
        await PhysicsFramesAsync(20);
        Log($"[bomb-repro] broken={combat.ReadLoot().DestroyedCrates - destroyed}");
        Require(nearby.All(node => !targets.Read(node.Key).Active), "saved bomb explosion missed the nearby crate cluster");
        Log("[bomb-repro] PASS");
    }


    private Task CaptureRac1BombAsync(string name, Vector3 focus, bool close = false) =>
        capture.CaptureAsync(WeaponDiagnosticCaptureKind.Bomb, name, focus, close, Driver.Lifetime);
    private static Vector3 FromEffectNative(Vector3 native) => new(-native.X, native.Z, native.Y);
}
