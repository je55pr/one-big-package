using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

/// <summary>Original wrench input, animation and staged-contact acceptance over copied facts.</summary>
public sealed class Rac1WrenchScenario(MovementDiagnosticContext context, IRac1WrenchDiagnosticFacts wrench,
    IRac1WeaponTargetFixture targets, IRac1CombatDiagnosticFixture combat, IDiagnosticControllerEquipment equipment, IWeaponDiagnosticCapture capture)
    : MovementScenarioSupport(context)
{
    public async Task RunAsync()
    {
        IDiagnosticGeometryLease? attachmentFixture = null;
        try
        {
            Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 0 }, "wrench smoke needs Veldin");
            var player = Player;
            using var frameRate = World.LimitFrameRate(60);
            ClearMovementSmokeInput();
            await WaitForGroundedAsync(Player, 360);
            Require(wrench.ReadWrench().ModelPresent && Animation.Read().AvatarPresent, "retail wrench/avatar did not load");
            equipment.Select(Rac1WeaponId.Wrench);
            await CaptureWrenchSmokeAsync("neutral-wrench");
            attachmentFixture = Geometry.Create("SyntheticWrenchRunIdleFloor", new(2000, 64, 2600));
            attachmentFixture.AddMesh("WrenchRunIdleFloor", 11,
                (new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20)),
                (new(-20, 0, -20), new(20, 0, 20), new(-20, 0, 20)));
            await PhysicsFramesAsync(3);
            player.ResetAt(attachmentFixture.GlobalPosition + Vector3.UnitY * .08f);
            await WaitForGroundedAsync(Player, 180);
            SetAnalogueSmokeInput(0, 1);
            await PhysicsFramesAsync(40);
            ClearMovementSmokeInput();
            bool sawStop = false, sawIdle = false;
            var handPositions = new HashSet<Vector3>();
            for (int tick = 0; tick < 120; tick++)
            {
                await PhysicsFramesAsync(1);
                var hand = wrench.ReadWrench();
                Require(hand.ExpectedHandLocal is not null, "run-to-idle lost its hand attachment metadata");
                Require(hand.HandLocal.DistanceTo(hand.ExpectedHandLocal!.Value) < .00001f,
                    "rendered wrench froze instead of following the run-to-idle hand");
                sawStop |= Animation.Read().OrdinaryStopSample is not null;
                if (Animation.Read().Sequence == 0)
                {
                    sawIdle = true;
                    handPositions.Add(wrench.ReadWrench().HandLocal);
                }
            }
            Require(sawStop && sawIdle && handPositions.Count > 10,
                $"run-to-stop-to-idle did not keep a moving wrench attachment: stop={sawStop}, idle={sawIdle}, handPoses={handPositions.Count}");
            await CaptureWrenchSmokeAsync("run-to-idle-wrench");
            Log("[wrench-smoke] neutral retracted model and run/stop/idle hand attachment PASS");
            player.ResetToSpawn();
            await WaitForGroundedAsync(Player, 180);
            attachmentFixture.Dispose();
            attachmentFixture = null;
            equipment.Select(Rac1WeaponId.FirstRanged);
            int ammo = equipment.Read().BombAmmo;
            await TapWrenchSmokeAsync();
            Require(wrench.ReadWrench().Frame is { Active: true, ProfileId: 0 } &&
                Animation.Read().Sequence == 23 && wrench.ReadWrench().NativeSequence == 3,
                "Square with Bomb Glove equipped did not select first wrench body/model clips");
            Require(wrench.ReadWrench().Visible && equipment.Read().BombAmmo == ammo,
                "wrench was hidden or consumed Bomb Glove ammo");
            Require(!wrench.ReadWrench().Frame.HitActive, "initial blend admitted an early hit");
            await CaptureWrenchSmokeAsync("first-swing");
            await WaitWrenchTimeAsync(26);
            await TapWrenchSmokeAsync();
            Require(wrench.ReadWrench().Frame.ProfileId == 1 && Animation.Read().Sequence == 24 &&
                wrench.ReadWrench().NativeSequence == 4, "second combo swing did not use native paired clips");
            await CaptureWrenchSmokeAsync("second-swing");
            await WaitWrenchTimeAsync(12);
            await TapWrenchSmokeAsync();
            Require(wrench.ReadWrench().Frame.ProfileId == 2 && Animation.Read().Sequence == 25 &&
                wrench.ReadWrench().NativeSequence == 5, "third combo swing did not use native paired clips");
            await CaptureWrenchSmokeAsync("third-swing");
            await Rac1SmokeWaitAsync(() => !wrench.ReadWrench().Frame.Active, 180, "combo recovery");
            Require(equipment.Read().BombAmmo == ammo, "combo spent selected-weapon ammo");
            Require(wrench.ReadWrench().Visible && !wrench.ReadWrench().GloveVisible,
                "wrench combo did not leave the last-used wrench in hand");
            await Rac1SmokeWaitAsync(() => Animation.Read().Sequence == 0, 60, "wrench standing recovery");
            var previousPose = Animation.Read().Pose;
            bool interpolated = false;
            for (int tick = 0; tick < 24; tick++)
            {
                await PhysicsFramesAsync(1);
                Require(Animation.Read().Sequence == 0 && Animation.Read().Pose is not null &&
                    (Animation.Read().OrdinaryStandingSample is not null || Animation.Read().OrdinaryStopSample is { Sequence: 0 }),
                    "wrench recovery flashed locomotion or lost the native standing clock");
                var pose = Animation.Read().Pose!;
                interpolated |= previousPose is not null && pose.FrameIndex == previousPose.FrameIndex &&
                    !pose.LocalPositions.AsSpan().SequenceEqual(previousPose.LocalPositions.AsSpan());
                previousPose = pose;
            }
            Require(interpolated, "post-wrench standing reverted to discrete low-rate keyframes");
            Log("[wrench-smoke] combo 23/24/25 + own clips 3/4/5, held equipment/ammo, recovery PASS");

            Driver.SetAction(MovementDiagnosticDriver.Action, true);
            await PhysicsFramesAsync(2);
            long heldGeneration = wrench.ReadWrench().Generation;
            await PhysicsFramesAsync(120);
            Require(!wrench.ReadWrench().Frame.Active && wrench.ReadWrench().Generation == heldGeneration,
                "holding Square automatically repeated the attack");
            Driver.SetAction(MovementDiagnosticDriver.Action, false);
            await PhysicsFramesAsync(2);

            player.ResetToSpawn();
            await WaitForGroundedAsync(Player, 180);
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            await PhysicsFramesAsync(16);
            Driver.SetAction(MovementDiagnosticDriver.Jump, false);
            await TapWrenchSmokeAsync();
            Require(wrench.ReadWrench().Frame.NativeAction == 0x14 && Animation.Read().Sequence == 43 &&
                wrench.ReadWrench().NativeSequence == 10, "airborne Square did not enter Hyper-Strike");
            Require(wrench.ReadWrench().Frame.Damage == 2, "Hyper-Strike lost native damage two");
            await CaptureWrenchSmokeAsync("hyper-strike");
            bool dived = false;
            for (int tick = 0; tick < 180 && wrench.ReadWrench().Frame.Active; tick++)
            {
                dived |= player.Read().Velocity.Y < -8;
                await PhysicsFramesAsync(1);
            }
            Require(dived && !wrench.ReadWrench().Frame.Active && player.Read().Grounded, "Hyper-Strike failed dive/landing/recovery");
            Log("[wrench-smoke] Hyper-Strike sequence43 + own clip10 rise/dive/landing PASS");

            player.ResetToSpawn();
            await WaitForGroundedAsync(Player, 180);
            Driver.SetAction(MovementDiagnosticDriver.Crouch, true);
            await PhysicsFramesAsync(3);
            await TapWrenchSmokeAsync();
            Require(wrench.ReadWrench().Frame.NativeAction == 0x15 && Animation.Read().Sequence == 26,
                "crouch/Square did not select Comet-Strike");
            Driver.SetAction(MovementDiagnosticDriver.Crouch, false);
            await Rac1SmokeWaitAsync(() => wrench.ReadWrench().ThrowActive, 100, "Comet-Strike release marker");
            Vector3 release = wrench.ReadWrench().WorldPosition;
            Require(release.DistanceTo(player.Read().GlobalPosition) > .3f, "throw spawned at feet instead of hand");
            await PhysicsFramesAsync(8);
            Require(wrench.ReadWrench().ThrowActive && wrench.ReadWrench().WorldPosition.DistanceTo(release) > 1,
                "thrown wrench did not travel outward");
            await CaptureWrenchSmokeAsync("comet-strike");
            await Rac1SmokeWaitAsync(() => !wrench.ReadWrench().ThrowActive && !wrench.ReadWrench().Frame.Active,
                240, "Comet-Strike return and loop recovery");
            Require(wrench.ReadWrench().Visible && !wrench.ReadWrench().GloveVisible,
                "caught wrench did not stay in hand");
            Log("[wrench-smoke] Comet-Strike release/flight/animated-hand catch/recovery PASS");

            // Contact fixtures are deliberately staged, unlike the separate
            // Veldin ordinary-play gate. All attacks still enter through input.
            equipment.Select(Rac1WeaponId.Wrench);
            player.ResetToSpawn();
            await WaitForGroundedAsync(Player, 180);
            var crate = targets.ReadCrates().First(node => node.Active && node.RewardCentre == 10);
            Vector3 forward = new(-(float)Math.Cos(player.Read().Rac1CurrentYaw), 0, (float)Math.Sin(player.Read().Rac1CurrentYaw));
            targets.StageCrate(crate.Key, player.Read().GlobalPosition + forward * .8f);
            int broken = combat.ReadLoot().DestroyedCrates;
            await TapWrenchSmokeAsync();
            Require(combat.ReadLoot().DestroyedCrates == broken, "crate broke during the entry blend");
            await Rac1SmokeWaitAsync(() => combat.ReadLoot().DestroyedCrates == broken + 1, 120, "animated wrench/crate contact");
            await Rac1SmokeWaitAsync(() => !wrench.ReadWrench().Frame.Active, 120, "contact recovery");
            Log("[wrench-smoke] staged mesh-bound swept crate contact PASS");
            player.ResetToSpawn();
            await WaitForGroundedAsync(Player, 180);
            var thrownCrate = targets.ReadCrates().First(node => node.Active && node.RewardCentre == 10);
            forward = new(-(float)Math.Cos(player.Read().Rac1CurrentYaw), 0, (float)Math.Sin(player.Read().Rac1CurrentYaw));
            targets.StageCrate(thrownCrate.Key, player.Read().GlobalPosition + forward * 4);
            int collected = combat.ReadLoot().CollectedBolts;
            Vector3 throwOrigin = player.Read().GlobalPosition;
            Driver.SetAction(MovementDiagnosticDriver.Crouch, true);
            await PhysicsFramesAsync(3);
            await TapWrenchSmokeAsync();
            Driver.SetAction(MovementDiagnosticDriver.Crouch, false);
            await Rac1SmokeWaitAsync(() => combat.ReadLoot().DestroyedCrates == broken + 2,
                160, "flying wrench/crate contact");
            await Rac1SmokeWaitAsync(() => combat.ReadLoot().CollectedBolts > collected,
                90, "flying wrench bolt collection");
            Require(HorizontalDistance(throwOrigin, player.Read().GlobalPosition) < .4f,
                "bolt collection depended on moving Ratchet to the distant crate");
            await Rac1SmokeWaitAsync(() => !wrench.ReadWrench().Frame.Active, 240, "throw contact recovery");
            Log("[wrench-smoke] staged throw/crate contact and distant bolt collection PASS");
            Log("[wrench-smoke] PASS");

        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogError($"[wrench-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
        finally
        {
            CleanupStep(() => attachmentFixture?.Dispose());
            ClearMovementSmokeInput();
        }
    }


    private Task TapWrenchSmokeAsync() => Rac1EncounterInput.TapWrenchAsync(Context);
    private Task WaitWrenchTimeAsync(float time) => Rac1SmokeWaitAsync(
        () => wrench.ReadWrench().Frame.Active && !wrench.ReadWrench().AnimationBlending &&
            Player.Read().Rac1TraversalAuthoredAnimationTime >= time, 180, $"wrench authored time {time}");
    private Task CaptureWrenchSmokeAsync(string name) => capture.CaptureAsync(WeaponDiagnosticCaptureKind.Wrench,
        name, Player.Read().GlobalPosition + Vector3.UnitY * .8f, true, Driver.Lifetime);
}
