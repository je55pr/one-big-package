using System.Numerics;
using System.Buffers.Binary;
using OBP.Core;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

public sealed class MovementScenario(MovementDiagnosticContext context) : MovementScenarioSupport(context)
{
    public async Task RunAsync(ObpDestination destination)
    {
        var player = Player;

        try
        {
            using var frameRate = World.LimitFrameRate(60);
            ClearMovementSmokeInput();
            await WaitForGroundedAsync(player, 360);
            player.ResetCollisionDiagnostics();

            Require(World.Read().CollisionBodies > 0, "world has no Godot collision body");
            Require(player.Read().Grounded, "player did not settle on Godot collision");
            Require(
                player.Read().MovementControllerLabel == "rac1-retail-derived-common-base",
                "unexpected ordinary movement controller");

            if (player.Read().HasNativeLocomotion)
            {
                Require(
                    player.Read().FloorSnapLength <= 0.000001f,
                    "R&C1 ordinary movement still has host floor snap enabled");
                Require(
                    !player.Read().FloorConstantSpeed,
                    "R&C1 still delegates slope speed projection to Godot");
                Require(
                    Math.Abs(player.Read().FloorMaxAngle -
                        (float)Rac1OrdinaryGroundContactMotion.OrdinarySupportMaxAngleRadians) < 0.000001f,
                    "R&C1 ordinary support angle is not using the recovered retail gate");
                Require(
                    Animation.Read() is { AvatarPresent: true, NativeController: true, SinkPresent: true },
                    "R&C1 did not attach its source-aware native player avatar");
                await WaitForPlayerAvatarSequenceAsync(
                    Rac1RatchetSequenceSelection.StandingSequenceId,
                    60,
                    "grounded standing");
            }
            else
            {
                Require(
                    Animation.Read() is { AvatarPresent: false, NativeController: false, SinkPresent: false },
                    "Explicit diagnostic player unexpectedly attached a native player avatar");
            }

            if (destination.Game == ObpSourceGame.Rac1)
            { await new Rac1SmallStepRegression(Context).RunAsync(); }

            if (destination.Game == ObpSourceGame.Rac1)
            { await new Rac1SlopeJumpRegression(Context).RunAsync(); }

            SetAnalogueSmokeInput(0f, 0.60f);
            await PhysicsFramesAsync(48);
            float walkSpeed = HorizontalSpeed(player);
            double walkMagnitude = player.Read().Rac1AnalogueMagnitude;
            double walkTarget = player.Read().Rac1TargetPlanarStep;
            Require(walkMagnitude >= 0.25d && walkMagnitude < 0.83d, "low stick did not land in the recovered walk band");
            Require(walkTarget > 0d && walkTarget < 0.03d, "low stick did not select the walk plateau");
            Require(walkSpeed > 0.1f, "low stick produced no host movement");
            if (destination.Game == ObpSourceGame.Rac1)
            {
                Require(
                    CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.LocomotionStartSequenceId &&
                    PlayerAvatarPresentationIsSynchronized(),
                    "R&C1 low-stick locomotion did not retain sequence 3");
                // Veldin 0x228c18 selects 3/4 by scalar speed. Clip completion
                // wraps sequence 3; it does not promote a steady walk to 4.
                await RequireWalkingAvatarLoopAsync(180);
            }

            SetAnalogueSmokeInput(0f, 1f);
            await PhysicsFramesAsync(48);
            float runSpeed = HorizontalSpeed(player);
            double cardinalYaw = player.Read().Rac1MovementTargetYaw;
            Require(runSpeed > walkSpeed + 1.0f, "full stick did not progress from walk toward run");
            Require(player.Read().Rac1TargetPlanarStep > walkTarget, "full stick target did not exceed walk target");

            SetAnalogueSmokeInput(0.65f, 0.80f);
            await PhysicsFramesAsync(24);
            double diagonalYaw = player.Read().Rac1MovementTargetYaw;
            Require(
                Math.Abs(WrapAngle(diagonalYaw - cardinalYaw)) > 0.15d,
                "arbitrary-angle stick input did not change steering target");
            if (destination.Game == ObpSourceGame.Rac1)
            {
                await WaitForPlayerAvatarSequenceAsync(
                    Rac1RatchetSequenceSelection.SustainedLocomotionSequenceId,
                    30,
                    "ordinary turning locomotion");
                // The walking loop observes render frames, so its accumulated
                // travel varies when several physics updates precede a render.
                // Give the moving-jump trial its own settled authored start.
                ClearMovementSmokeInput();
                Player.ResetToSpawn();
                await WaitForGroundedAsync(player, 180);
                SetAnalogueSmokeInput(0f, 1f);
                await PhysicsFramesAsync(48);
                Require(player.Read().Grounded && HorizontalSpeed(player) > 5.5f,
                    "moving-jump trial did not start at supported full speed");
            }

            float movingJump = await MeasureJumpAsync(
                player,
                holdFrames: 8,
                applyPartialAirControl: false,
                movingAtLaunch: true);
            Require(movingJump > 0.5f, "moving jump produced no useful apex");

            ClearMovementSmokeInput();
            await WaitForAnimationStateAsync(player, PlayerAnimationState.Idle, 120, "post-run idle");
            if (destination.Game == ObpSourceGame.Rac1)
            {
                await WaitForPlayerAvatarSequenceAsync(
                    Rac1RatchetSequenceSelection.StandingSequenceId,
                    30,
                    "post-run standing");
            }

            // The traversal portion can finish at Veldin's death-plane edge.
            // Crouch needs a settled start, independently of an automatic
            // environmental restart after the moving jump.
            Player.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            Driver.SetAction(MovementDiagnosticDriver.Crouch, true);
            await PhysicsFramesAsync(4);
            Require(
                player.Read().Rac1LocomotionState.ToString() is "Crouched" or "CrouchTurning",
                $"crouch action did not reach the common controller; state={player.Read().Rac1LocomotionState}; " +
                $"phase={player.Read().Rac1MovementPhase}; grounded={player.Read().Grounded}; alive={player.Read().Rac1GameplayAlive}");
            if (destination.Game == ObpSourceGame.Rac1)
            {
                await WaitForPlayerAvatarSequenceAsync(
                    Rac1RatchetSequenceSelection.CrouchSequenceId,
                    30,
                    "crouch entry presentation");
                SetAnalogueSmokeInput(1f, 0f);
                int firstTurn = 0;
                for (int tick = 0; tick < 60 && firstTurn is not (14 or 15); tick++)
                {
                    await PhysicsFramesAsync(1);
                    firstTurn = CurrentPlayerAvatarSourceSequence() ?? 0;
                }
                Require(firstTurn is 14 or 15, "crouch steering did not select a turning clip");
                SetAnalogueSmokeInput(-1f, 0f);
                await WaitForPlayerAvatarSequenceAsync(firstTurn == 14 ? 15 : 14, 90,
                    "crouch turn reversal");
                SetAnalogueSmokeInput(0f, 0f);
                await WaitForPlayerAvatarSequenceAsync(13, 180, "crouch turn settling");
            }
            Driver.SetAction(MovementDiagnosticDriver.Crouch, false);
            if (destination.Game == ObpSourceGame.Rac1)
            { await WaitForPlayerAvatarSequenceAsync(0, 30, "crouch release presentation"); }
            await PhysicsFramesAsync(20);

            if (destination.Game == ObpSourceGame.Rac1)
            {
                await RequireOrdinaryStopRestartAsync(player);
                await RequireRunToCrouchStopAsync(player);
                await RequireBlockedRunCrouchCarryAsync(player);
            }

            Player.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            float shortJump = await MeasureJumpAsync(player, holdFrames: 2, applyPartialAirControl: false);

            Player.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            float longJump = await MeasureJumpAsync(player, holdFrames: 16, applyPartialAirControl: false);
            Require(longJump > shortJump + 0.20f, "held jump did not exceed tap jump apex");

            Player.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            float partialAirTravel = await MeasureAirControlAsync(player);
            Require(partialAirTravel > 0.10f, "partial-stick air control produced no planar travel");

            Player.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            SendPhysicalKey(DiagnosticPhysicalKey.W, true);
            await PhysicsFramesAsync(30);
            float keyboardSpeed = HorizontalSpeed(player);
            SendPhysicalKey(DiagnosticPhysicalKey.W, false);
            Require(keyboardSpeed > 0.1f, "WASD keyboard fallback produced no movement");
            await PhysicsFramesAsync(3);
            Vector3 beforeFly = player.Read().GlobalPosition;
            await TapPhysicalKeyAsync(DiagnosticPhysicalKey.F);
            Require(Player.Read().FlyEnabled, "F did not enable development fly");
            SendPhysicalKey(DiagnosticPhysicalKey.W, true);
            await PhysicsFramesAsync(18);
            SendPhysicalKey(DiagnosticPhysicalKey.W, false);
            float flyTravel = HorizontalDistance(beforeFly, player.Read().GlobalPosition);
            Require(flyTravel > 0.5f, "development fly did not move with keyboard input");
            await TapPhysicalKeyAsync(DiagnosticPhysicalKey.F);
            Require(!Player.Read().FlyEnabled, "F did not disable development fly");

            Vector3 spawn = Player.Read().SpawnPosition;
            if (destination.Game == ObpSourceGame.Rac1)
            { Player.ResetToSpawn(); }
            else
                await TapPhysicalKeyAsync(DiagnosticPhysicalKey.R);
            await WaitForGroundedAsync(player, 180);
            float respawnError = HorizontalDistance(spawn, player.Read().GlobalPosition);
            Require(respawnError < 1.0f, "development respawn did not return to the host spawn");
            Require(player.Read().Grounded, "respawned player did not re-enter Godot floor contact");
            if (destination.Game == ObpSourceGame.Rac1)
            {
                Require(
                    player.Read().Rac1MaxObservedSlideCollisions < player.Read().MaxSlides,
                    $"R&C1 ordinary movement saturated Godot MaxSlides={player.Read().MaxSlides}; " +
                    $"observed={player.Read().Rac1MaxObservedSlideCollisions}");
            }

            if (destination.Game == ObpSourceGame.Rac1)
            {
                await new Rac1AirborneRegression(Context).RunAsync();
                await new Rac1LandingRegression(Context).RunAsync();
                await new Rac1TraversalContactRegression(Context).RunAsync();
                await new Rac1SwingshotRegression(Context).RunAsync();
            }

            Log(
                $"[movement-smoke] PASS {destination.DestinationId} " +
                $"walk={walkSpeed:0.000} run={runSpeed:0.000} movingJump={movingJump:0.000} shortJump={shortJump:0.000} " +
                $"longJump={longJump:0.000} partialAir={partialAirTravel:0.000} keyboard={keyboardSpeed:0.000} " +
                $"fly={flyTravel:0.000} respawnError={respawnError:0.000} " +
                $"maxSlideContacts={player.Read().Rac1MaxObservedSlideCollisions}/{player.Read().MaxSlides}");
        }
        catch (OperationCanceledException error) when (error.CancellationToken == Context.Driver.Lifetime && Context.Driver.Lifetime.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogError($"[movement-smoke] FAIL {destination.DestinationId}: {ex.Message}");
            throw;
        }
        finally
        {
            ClearMovementSmokeInput();
            SendPhysicalKey(DiagnosticPhysicalKey.W, false);
            SendPhysicalKey(DiagnosticPhysicalKey.Space, false);
        }
    }


    public async Task<float> MeasureJumpAsync(
        IDiagnosticMovementPlayer player,
        int holdFrames,
        bool applyPartialAirControl,
        bool movingAtLaunch = false)
    {
        float baseline = player.Read().GlobalPosition.Y;
        float apex = baseline;
        bool sawAir = false;

        Driver.SetAction(MovementDiagnosticDriver.Jump, true);
        await PhysicsFramesAsync(holdFrames);
        Driver.SetAction(MovementDiagnosticDriver.Jump, false);

        for (int frame = 0; frame < 240; frame++)
        {
            await PhysicsFramesAsync(1);
            if (!player.Read().Grounded)
            {
                if (!sawAir)
                {
                    sawAir = true;
                    if (World.Read().Source == ObpSourceGame.Rac1)
                    {
                        await WaitForPlayerAvatarSequenceAsync(
                            Rac1RatchetSequenceSelection.SelectJumpSequence(movingAtLaunch),
                            30,
                            movingAtLaunch ? "moving jump" : "stationary jump");
                    }
                }
                if (applyPartialAirControl && frame == 0)
                { SetAnalogueSmokeInput(0.58f, 0f); }
            }

            apex = Math.Max(apex, player.Read().GlobalPosition.Y);
            if (sawAir && player.Read().Grounded)
            {
                if (movingAtLaunch && World.Read().Source == ObpSourceGame.Rac1)
                {
                    // Retain movement across touchdown: clearing it here hid
                    // the permanent loss of the native locomotion clock.
                    for (int update = 0; update < 5; update++)
                    {
                        await PhysicsFramesAsync(1);
                        await ProcessFrameAsync();
                        var controller = Animation.Read();
                        Require(controller?.OrdinaryLocomotionSample is not null &&
                            controller.Pose is not null && PlayerAvatarPresentationIsSynchronized(),
                            "moving landing did not reconnect physics-sampled locomotion");
                        int sequence = controller!.OrdinaryLocomotionSample!.Value.Sequence;
                        // This traversal can leave support again immediately.
                        // Native ordinary edge updates retain the assigned rate;
                        // only supported helper calls consume the current scalar.
                        if (player.Read().Rac1AnimationHelperAdmitted)
                        {
                            float expected = Rac1OrdinaryLocomotionAnimation.PlaybackMultiplier(
                                sequence, player.Read().Rac1AnimationScalarAtSelection);
                            Require(Math.Abs(controller.OrdinaryLocomotionMultiplier!.Value - expected) < .00001f,
                                $"post-jump locomotion lost its recovered speed multiplier: update={update}, " +
                                $"actual={controller.OrdinaryLocomotionMultiplier}, expected={expected}, " +
                                $"sequence={sequence}, scalar={player.Read().Rac1AnimationScalarAtSelection}");
                        }
                    }
                    Log("[movement-smoke] moving landing retained the native locomotion clock and speed multiplier");
                }
                ClearMovementSmokeInput();
                return apex - baseline;
            }
        }

        throw new InvalidOperationException("jump did not land within smoke timeout");
    }


    private async Task<float> MeasureAirControlAsync(IDiagnosticMovementPlayer player)
    {
        Vector3 start = player.Read().GlobalPosition;
        Driver.SetAction(MovementDiagnosticDriver.Jump, true);
        await PhysicsFramesAsync(10);
        Driver.SetAction(MovementDiagnosticDriver.Jump, false);

        bool sawAir = false;
        for (int frame = 0; frame < 240; frame++)
        {
            await PhysicsFramesAsync(1);
            if (!player.Read().Grounded)
            {
                if (!sawAir)
                {
                    sawAir = true;
                    start = player.Read().GlobalPosition;
                    SetAnalogueSmokeInput(0.58f, 0f);
                }
            }
            else if (sawAir)
            {
                float travel = HorizontalDistance(start, player.Read().GlobalPosition);
                ClearMovementSmokeInput();
                return travel;
            }
        }

        throw new InvalidOperationException("partial-air-control jump did not land within smoke timeout");
    }


    private async Task RequireWalkingAvatarLoopAsync(int frames)
    {
        int previousFrame = Animation.Read().Frame ?? -1;
        bool advanced = false;
        bool wrapped = false;
        for (int frame = 0; frame < frames; frame++)
        {
            await ProcessFrameAsync();
            int currentFrame = Animation.Read().Frame ?? -1;
            Require(
                currentFrame >= 0 &&
                CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.LocomotionStartSequenceId &&
                Animation.Read().Loop == true &&
                PlayerAvatarPresentationIsSynchronized(),
                $"steady low-stick walk lost synchronized looping sequence 3; frame={currentFrame}; " +
                $"sequence={CurrentPlayerAvatarSourceSequence()}; semantic={Player.Read().AnimationState}");
            if (World.Read().Source == ObpSourceGame.Rac1)
            {
                Require(Animation.Read() is { OrdinaryLocomotionSample: not null, OrdinaryLocomotionMultiplier: > 2.35f and < 2.36f, SinkPosePresent: true },
                    "steady low-stick walk is not using the physics-sampled native locomotion clock/rate");
            }
            advanced |= currentFrame > previousFrame;
            wrapped |= currentFrame < previousFrame;
            previousFrame = currentFrame;
        }
        Require(advanced && wrapped,
            $"steady walking animation did not advance and wrap; advanced={advanced}; " +
            $"wrapped={wrapped}; frame={previousFrame}");
        Log("[movement-smoke] steady walking sequence 3 advanced and wrapped");
    }


    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "S1244", Justification =
        "Strength is a scripted fixture choice, exactly .6f or 1f; it is not a computed physical measurement.")]
    private async Task RequireOrdinaryStopRestartAsync(IDiagnosticMovementPlayer player)
    {
        foreach (float strength in new[] { .6f, 1f })
        {
            ClearMovementSmokeInput();
            Player.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            SetAnalogueSmokeInput(0, strength);
            await PhysicsFramesAsync(48);
            SetAnalogueSmokeInput(0, 0);
            bool sawStop = false;
            for (int update = 0; update < 12; update++)
            {
                await PhysicsFramesAsync(1);
                await ProcessFrameAsync();
                int? sequence = CurrentPlayerAvatarSourceSequence();
                sawStop |= sequence is 5 or 6 or 20;
                if (strength == 1 && sawStop)
                { break; }
            }
            var controller = Animation.Read();
            if (strength == 1)
            {
                Require(sawStop && controller.OrdinaryStopSample is not null &&
                    controller.Pose is not null && PlayerAvatarPresentationIsSynchronized(),
                    "run release did not enter the sampled native stop family");
                // Reverse direction while the stop blend is still active.
                SetAnalogueSmokeInput(0, -1);
                await PhysicsFramesAsync(3);
                await ProcessFrameAsync();
                controller = Animation.Read();
                Require(controller.OrdinaryLocomotionSample is not null &&
                    controller.OrdinaryStopSample is null && CurrentPlayerAvatarSourceSequence() == 3 &&
                    PlayerAvatarPresentationIsSynchronized(), "quick reverse restart did not re-enter native locomotion");
                SetAnalogueSmokeInput(0, 0);
            }
            await PhysicsFramesAsync(100);
            await ProcessFrameAsync();
            Require(CurrentPlayerAvatarSourceSequence() == 0 && PlayerAvatarPresentationIsSynchronized(),
                "ordinary release did not settle to synchronized standing");
            if (strength < 1)
            { Require(!sawStop, "low-speed walk release unexpectedly used the running stop family"); }
            Log($"[movement-smoke] ordinary stop/restart passed strength={strength:F1}, stopFamily={sawStop}");
        }
    }


    private async Task RequireRunToCrouchStopAsync(IDiagnosticMovementPlayer player)
    {
        foreach (bool keepDirection in new[] { true, false })
        {
            ClearMovementSmokeInput();
            Player.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            SetAnalogueSmokeInput(0f, 1f);
            await PhysicsFramesAsync(48);
            await ProcessFrameAsync();
            float startSpeed = HorizontalSpeed(player);
            Require(startSpeed > 5.5f && player.Read().Grounded, "run-to-crouch trial did not start at supported full speed");
            var contactMotion = player.Read().Rac1ContactProcessedMotion;
            Require(contactMotion.HasValue, "run-to-crouch trial did not record static-contact motion");
            double entryPlanar = Math.Sqrt(contactMotion!.Value.X * contactMotion.Value.X +
                contactMotion.Value.Y * contactMotion.Value.Y);
            Vector3 start = player.Read().GlobalPosition;
            if (!keepDirection)
            { SetAnalogueSmokeInput(0f, 0f); }
            Driver.SetAction(MovementDiagnosticDriver.Crouch, true);
            int stoppedAt = 0;
            ulong startedPhysicsFrame = Driver.PhysicsFrame;
            float previousSpeed = startSpeed;
            for (int tick = 1; tick <= 65; tick++)
            {
                await PhysicsFramesAsync(1);
                await ProcessFrameAsync();
                float speed = HorizontalSpeed(player);
                Require(player.Read().Rac1LocomotionState is Rac1RatchetLocomotionState.Crouched or Rac1RatchetLocomotionState.CrouchTurning,
                    $"run-to-crouch lost its state at tick {tick}; phase={player.Read().Rac1MovementPhase}; floor={player.Read().Grounded}; speed={speed:F4}");
                Require(speed <= previousSpeed + 0.01f,
                    $"run-to-crouch accelerated at tick {tick}: {previousSpeed:F4} -> {speed:F4}");
                if (player.Read().Rac1ControllerPlanarStep < 1e-8d && stoppedAt == 0)
                { stoppedAt = checked((int)(Driver.PhysicsFrame - startedPhysicsFrame)); }
                previousSpeed = speed;
            }
            float distance = HorizontalDistance(start, player.Read().GlobalPosition);
            var entryCarry = Rac1ContactMotionProjection.EnterCrouch(contactMotion.Value);
            double entryCarryPlanar = Math.Sqrt(entryCarry.X * entryCarry.X + entryCarry.Y * entryCarry.Y);
            int expectedStop = (int)Math.Ceiling(entryCarryPlanar / Rac1RatchetMovementController.CrouchDecelerationPerTick);
            // Count actual physics updates. Awaiting a render frame between
            // observations can skip a physics tick under host scheduling load.
            Require(stoppedAt > 0 && Math.Abs(stoppedAt - expectedStop) <= 2,
                $"run-to-crouch stop timing changed: stop={stoppedAt}, expected={expectedStop}, distance={distance:F4}");
            // Distance is an observation, pending a matched retail trajectory.
            // Do not require OBP's current result as a retail-distance oracle.
            double standingHeight = Animation.Read().StandingHeight;
            Log($"[movement-smoke] run-to-crouch heldDirection={keepDirection} startSpeed={startSpeed:F4} entryContactPlanar={entryPlanar:F6} stopTicks={stoppedAt} distance={distance:F4} standingHeights={distance / standingHeight:F4}");
            ClearMovementSmokeInput();
        }
    }


    private async Task RequireBlockedRunCrouchCarryAsync(IDiagnosticMovementPlayer player)
    {
        // Synthetic Godot fixture: verifies the adapter consumes completed
        // collision feedback. This is not evidence for native wall collision.
        ClearMovementSmokeInput();
        Player.ResetToSpawn();
        await WaitForGroundedAsync(player, 180);
        SetAnalogueSmokeInput(0f, 1f);
        await PhysicsFramesAsync(48);
        await ProcessFrameAsync();
        Vector3 direction = new Vector3(player.Read().Velocity.X, 0, player.Read().Velocity.Z).Normalized();
        using var fixture = Geometry.Create("CrouchCarrySmoke", Vector3.Zero);
        using var wall = fixture.AddBox("CrouchCarrySmokeWall", new(6, 5, .4f),
            player.Read().GlobalPosition + direction * .9f + Vector3.UnitY * 2, MathF.Atan2(direction.X, direction.Z));
        try
        {
            await PhysicsFramesAsync(30);
            await ProcessFrameAsync();
            var motion = player.Read().Rac1ContactProcessedMotion;
            Require(motion.HasValue && Math.Sqrt(motion.Value.X * motion.Value.X + motion.Value.Y * motion.Value.Y) < 0.02,
                $"blocked run did not produce bounded contact carry: {motion}");
            Driver.SetAction(MovementDiagnosticDriver.Crouch, true);
            await PhysicsFramesAsync(2);
            wall.Dispose();
            await PhysicsFramesAsync(2);
            await ProcessFrameAsync();
            Vector3 start = player.Read().GlobalPosition;
            await PhysicsFramesAsync(30);
            await ProcessFrameAsync();
            float distance = HorizontalDistance(start, player.Read().GlobalPosition);
            Require(distance < 0.3f, $"crouch retained phantom run momentum after wall removal: {distance:F4}");
            Log($"[movement-smoke] synthetic-wall crouch release distance={distance:F4}");
        }
        finally
        {
            wall.Dispose();
            ClearMovementSmokeInput();
        }
    }

}
