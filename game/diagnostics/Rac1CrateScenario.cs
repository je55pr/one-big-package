using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

/// <summary>Original staged native crate/pickup acceptance; not a matched retail playthrough.</summary>
public sealed class Rac1CrateScenario(MovementDiagnosticContext context, Rac1LootScenarioPorts ports) : MovementScenarioSupport(context)
{
    private IRac1LootDiagnosticFixture Loot => ports.Loot;
    private IRac1WeaponTargetFixture Targets => ports.Targets;
    public async Task RunAsync()
    {
        IDiagnosticGeometryLease? fixture = null;
        try
        {
            Require(World.Read().Source == ObpSourceGame.Rac1, "crate smoke needs R&C1");
            using var frameRate = World.LimitFrameRate(60); ClearMovementSmokeInput();
            fixture = Geometry.Create("SyntheticCratePickupFixture", new(2000, 64, 2600));
            fixture.AddMesh("CrateFloor", 11,
                (new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20)), (new(-20, 0, -20), new(20, 0, 20), new(-20, 0, 20)));
            await PhysicsFramesAsync(3); Vector3 origin = fixture.GlobalPosition;
            Player.ResetAt(origin + new Vector3(-8, .05f, 0)); await WaitForGroundedAsync(Player, 180);
            var wood = Targets.ReadCrates().First(node => node.Active && node.Key.Target.NativeClassId == 500 && node.RewardCentre > 0);
            Targets.StageCrate(wood.Key, origin); await PhysicsFramesAsync(3);
            await CaptureAsync("intact", origin + Vector3.UnitY * .5f);
            int balance = Loot.Read().Wallet, soundCount = Loot.Read().Sounds;
            Require(Loot.StrikeCrate(wood.Key), "staged positive wrench damage failed");
            Require(!Targets.Read(wood.Key).Active && !Loot.CrateHasBody(wood.Key), "broken crate retained presence/contact");
            Require(Loot.Read().DebrisClasses.Length == 8 && Loot.Read().DebrisClasses.All(nativeClass => nativeClass is 357 or 358),
                "wood did not spawn its dedicated fragment models");
            int emitted = Loot.Read().Pickups.Where(pickup => pickup.Id > 0).Sum(pickup => Rac1BoltCrate.ValueForNativePickupClass(pickup.NativeClass));
            Require(Rac1BoltCrate.RewardRange(wood.RewardCentre!.Value).Contains(emitted), "payout escaped recovered reward range");
            Require(Loot.Read().Sounds > soundCount, "crate break sound was not remapped/played");
            await PhysicsFramesAsync(5); await CaptureAsync("burst", origin + Vector3.UnitY * .6f);
            await PhysicsFramesAsync(160);
            var pickups = Loot.Read().Pickups.Where(pickup => pickup.Id > 0).ToArray();
            Require(pickups.Length > 0 && pickups.All(pickup => pickup.NativeState == 3), "distant scattered bolts failed to bounce/settle");
            Require(Loot.Read().Wallet == balance, "distant bolt was credited without attraction");
            Require(Loot.OriginalPickupMeshes, "bolt presentation used a placeholder primitive");
            await CaptureAsync("settled", origin + Vector3.UnitY * .25f);
            Player.ResetAt(origin + Vector3.UnitY * .05f); await PhysicsFramesAsync(8);
            await CaptureAsync("homing", origin + Vector3.UnitY * .7f);
            // Approach the remaining scattered pickups; never move them or inject collection credit.
            for (int tick = 0; tick < 300 && Loot.Read().Pickups.Any(pickup => pickup.Id > 0); tick++)
            {
                if (tick >= 90 && tick % 30 == 0)
                    Player.ResetAt(Loot.Read().Pickups.First(pickup => pickup.Id > 0).WorldPosition + Vector3.UnitY * .05f);
                await PhysicsFramesAsync(1);
            }
            Require(!Loot.Read().Pickups.Any(pickup => pickup.Id > 0), "scattered bolts failed to attract/arrive after approach");
            Require(Loot.Read().Wallet == balance + emitted && Loot.Read().OutstandingCratePickups == 0, "bolt arrival failed exact wallet credit/cleanup");
            Require(Loot.Read().HudWallet == balance + emitted, "HUD lost collected balance");
            Log($"[crate-smoke] scatter/rebound/retail models/sounds/homing/wallet PASS; value={emitted}");
            var stack = Targets.ReadCrates().Where(node => node.Active && node.Key.Target.NativeClassId == 500).Take(2).ToArray();
            Require(stack.Length == 2, "The native stacked-crate fixture is unavailable.");
            Targets.StageCrate(stack[0].Key, origin + new Vector3(5, 0, 0)); Targets.StageCrate(stack[1].Key, origin + new Vector3(5, 1, 0));
            await PhysicsFramesAsync(10);
            Require(Math.Abs(Targets.Read(stack[1].Key).Position.Y - origin.Y - 1) < .05f, "intact stack lost support");
            Require(Loot.StrikeCrate(stack[0].Key), "bottom stack crate did not break"); await PhysicsFramesAsync(60);
            Require(Math.Abs(Targets.Read(stack[1].Key).Position.Y - origin.Y) < .05f, "unsupported upper crate remained suspended");
            Log("[crate-smoke] stacked-crate support/fall PASS");
            var ammo = Targets.ReadCrates().First(node => node.Active && node.Key.Target.NativeClassId == 511);
            Targets.StageCrate(ammo.Key, origin + new Vector3(-5, 0, 0)); int ammoBefore = Loot.Read().BombAmmo;
            Require(Loot.StrikeCrate(ammo.Key) && Loot.Read().Ammo.Length > 0, "ammo crate failed to emit retail pickup models");
            var bomb = new SourceWeaponKey(ObpSourceGame.Rac1, 10);
            int ammoAmount = Loot.Read().Ammo.Sum(pickup => pickup.Item == bomb ? pickup.Amount : 0);
            Require(ammoAmount > 0, "fixture expected native Bomb Glove preferred ammo");
            Player.ResetAt(origin + new Vector3(-5, .05f, 0));
            for (int i = 0; i < 240 && Loot.Read().Ammo.Length > 0; i++) await PhysicsFramesAsync(1);
            Require(Loot.Read().Ammo.Length == 0, $"ammo failed to collect: player={Player.Read().GlobalPosition}, health={Loot.Read().Health}, " +
                string.Join(';', Loot.Read().Ammo.Select(pickup => $"item{pickup.Item} pos={pickup.Position} vel={pickup.Velocity} delay={pickup.DelayTicks} ammo={pickup.CurrentAmmo}")));
            Require(Loot.Read().BombAmmo == Math.Min(40, ammoBefore + ammoAmount), "ammo grant changed the wrong resource");
            var health = Targets.ReadCrates().First(node => node.Active && node.Key.Target.NativeClassId == 501);
            Targets.StageCrate(health.Key, origin + new Vector3(-5, 0, 3)); Require(Loot.StrikeCrate(health.Key), "Nanotech crate failed to break");
            await PhysicsFramesAsync(60);
            Require(Loot.Read().HealthCloudAttraction is { Length: 1 } clouds && !clouds[0], "full health wasted the cloud");
            Loot.InjectRetainedClass749Damage(); int beforeArrivalSound = Loot.Read().Sounds;
            await WaitAsync(() => Loot.Read().HealthCloudAttraction.Length == 0, 120, "Nanotech cloud arrival");
            Require(Loot.Read().Health == 4, "cloud did not grant exactly one health point");
            Require(Loot.Read().Sounds > beforeArrivalSound, "Nanotech arrival did not play class501 sound1");
            Log("[crate-smoke] ammo grant and full-health Nanotech retention/arrival PASS");
            var metal = Targets.ReadCrates().FirstOrDefault(node => node.Active && node.Key.Target.NativeClassId == 502);
            if (metal is not null)
            {
                double yaw = Player.Read().Rac1CurrentYaw; Vector3 forward = new(-(float)Math.Cos(yaw), 0, (float)Math.Sin(yaw));
                Targets.StageCrate(metal.Key, Player.Read().GlobalPosition + forward * 3);
                Require(!Loot.StrikeCrate(metal.Key) && Targets.Read(metal.Key).Active, "metal admitted wrench-only flags");
                ports.Equipment.Select(Rac1WeaponId.FirstRanged); int beforeShot = Loot.Read().BombAmmo;
                await Rac1EncounterInput.TapPrimaryAsync(Context, ports.Equipment);
                await WaitAsync(() => !Targets.Read(metal.Key).Active, 180, "Bomb Glove projectile/metal crate contact");
                Require(!Targets.Read(metal.Key).Active && Loot.Read().DebrisClasses.Contains(354), "metal break failed native flag/debris path");
                Require(Loot.Read().BombAmmo == beforeShot - 1, "metal test failed to consume one Bomb Glove round");
                Log("[crate-smoke] metal rejects wrench/Bomb Glove input and projectile/metal fragments PASS");
            }
            await PhysicsFramesAsync(600); Require(Loot.Read().DebrisClasses.Length == 0, "debris failed to expire");
            if (ports.Breakables is { } breakables)
            {
                var start = breakables.Read();
                Require(start.Explosives.Select(entry => entry.Actor).Distinct().Count() == start.Explosives.Length,
                    "explosive registrations contain duplicate authored identities");
                Require(start.Novalis.Select(entry => entry.Actor).Distinct().Count() == start.Novalis.Length,
                    "Novalis registrations contain duplicate authored identities");
                Require(start.Explosives.All(entry => entry.Actor.Target.SourceGame == ObpSourceGame.Rac1 &&
                        entry.Actor.Target.NativeClassId == Rac1ExplosiveCrateSession.ClassId),
                    "explosive adapter admitted an unrelated source/native class");
                Require(start.Novalis.All(entry => entry.Actor.Target.SourceGame == ObpSourceGame.Rac1 &&
                        entry.Actor.Target.NativeClassId is 704 or 754),
                    "Novalis adapter admitted an unrelated source/native class");

                var explosive = start.Explosives.FirstOrDefault(entry =>
                    entry.Presence == RuntimeEntityPresence.Active && entry.HasCollider);
                if (explosive is not null)
                {
                    Require(breakables.TouchExplosive(explosive.Actor), "fixture could not start admitted native class-505 touch");
                    await WaitAsync(() => breakables.Read().Explosives.Any(entry =>
                        entry.Actor == explosive.Actor && entry.NativeState == Rac1ExplosiveCrateSession.BurstState), 220,
                        "native explosive countdown to state-3 burst");
                    Require(!breakables.Read().Explosives.Single(entry => entry.Actor == explosive.Actor).HasCollider,
                        "explosive detonation retained its contact body");
                    Require(!breakables.TouchExplosive(explosive.Actor), "spent explosive admitted repeated touch");
                    Log("[crate-smoke] lease-owned explosive touch/fuse/retirement PASS");
                }
                else
                {
                    Log("[crate-smoke] explosive touch/fuse checks SKIPPED (no active class-505 crate)");
                }

                var mushroom = start.Novalis.FirstOrDefault(entry =>
                    entry.Actor.Target.NativeClassId == Rac1NovalisBreakableSession.MushroomClass &&
                    entry.Presence == RuntimeEntityPresence.Active);
                if (mushroom is not null)
                {
                    Require(breakables.BreakNovalis(mushroom.Actor, Rac1NovalisBreakableSession.ContactKind.Wrench),
                        "admitted Novalis mushroom rejected host wrench contact");
                    Require(!breakables.BreakNovalis(mushroom.Actor, Rac1NovalisBreakableSession.ContactKind.Explosion),
                        "destroyed Novalis mushroom admitted duplicate break/reward");
                    Require(breakables.Read().Novalis.Single(entry => entry.Actor == mushroom.Actor).Presence ==
                        RuntimeEntityPresence.Inactive, "Novalis mushroom retained active presentation");
                    Log("[crate-smoke] Novalis mushroom contact and duplicate-break guards PASS");
                }
                else
                {
                    Log("[crate-smoke] Novalis mushroom checks SKIPPED (no active class-754 mushroom)");
                }

                var rock = start.Novalis.FirstOrDefault(entry =>
                    entry.Actor.Target.NativeClassId == Rac1NovalisBreakableSession.RockFormationClass &&
                    entry.Presence == RuntimeEntityPresence.Active);
                if (rock is not null)
                {
                    Require(!breakables.BreakNovalis(rock.Actor, Rac1NovalisBreakableSession.ContactKind.Wrench),
                        "existing class-704 host policy admitted wrench");
                    Require(breakables.BreakNovalis(rock.Actor, Rac1NovalisBreakableSession.ContactKind.Explosion),
                        "admitted class-704 explosion failed");
                    Log("[crate-smoke] Novalis rock contact guards PASS");
                }
                else
                {
                    Log("[crate-smoke] Novalis rock checks SKIPPED (no active class-704 rock)");
                }
            }
            Log($"[crate-smoke] PASS level={World.Read().LevelId}");
        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime) { throw; }
        catch (Exception error) { LogError($"[crate-smoke] FAIL: {error.Message}\n{error.StackTrace}"); throw; }
        finally { ClearMovementSmokeInput(); CleanupStep(() => fixture?.Dispose()); }
    }
    private Task CaptureAsync(string name, Vector3 focus) => ports.Capture.CaptureAsync(name, focus, Driver.Lifetime);
    private Task WaitAsync(Func<bool> ready, int frames, string label) => Driver.WaitUntilAsync(ready, frames, "Timed out waiting for RAC1 smoke phase: " + label + ".");
}
