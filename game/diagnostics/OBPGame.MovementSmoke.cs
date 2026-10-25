using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OneBigPackage;

public partial class OBPGame
{
    private async System.Threading.Tasks.Task RunMovementSmokeAsync(ObpDestination destination)
    {
        PlayerHost player = _player
            ?? throw new InvalidOperationException("movement smoke requires a spawned player");

        try
        {
            Engine.MaxFps = 60;
            ClearMovementSmokeInput();
            await WaitForGroundedAsync(player, 360);
            player.ResetRac1CollisionDiagnostics();

            Require(_sceneResult?.CollisionBodies > 0, "world has no Godot collision body");
            Require(player.IsOnFloor(), "player did not settle on Godot collision");
            Require(
                player.MovementControllerLabel == "rac1-retail-derived-common-base",
                "unexpected ordinary movement controller");

            if (player.HasNativeLocomotion)
            {
                Require(
                    player.FloorSnapLength <= 0.000001f,
                    "R&C1 ordinary movement still has host floor snap enabled");
                Require(
                    !player.FloorConstantSpeed,
                    "R&C1 still delegates slope speed projection to Godot");
                Require(
                    Math.Abs(player.FloorMaxAngle -
                        (float)Rac1OrdinaryGroundContactMotion.OrdinarySupportMaxAngleRadians) < 0.000001f,
                    "R&C1 ordinary support angle is not using the recovered retail gate");
                Require(
                    _playerAvatarView is not null &&
                    GodotObject.IsInstanceValid(_playerAvatarView) &&
                    _playerAvatarAnimationController is Rac1PlayerAnimationPresentationController &&
                    _playerAvatarAnimationSink is not null,
                    "R&C1 did not attach its source-aware native player avatar");
                await WaitForPlayerAvatarSequenceAsync(
                    Rac1RatchetSequenceSelection.StandingSequenceId,
                    60,
                    "grounded standing");
            }
            else
            {
                Require(
                    _playerAvatarView is null &&
                    _playerAvatarAnimationController is null &&
                    _playerAvatarAnimationSink is null,
                    "Explicit diagnostic player unexpectedly attached a native player avatar");
            }

            if (destination.Game == ObpSourceGame.Rac1)
                await RequireRac1SmallStepSmokeAsync(player);

            if (destination.Game == ObpSourceGame.Rac1)
                await RequireRac1SlopeJumpSmokeAsync(player);

            SetAnalogueSmokeInput(0f, 0.60f);
            await PhysicsFramesAsync(48);
            float walkSpeed = HorizontalSpeed(player);
            double walkMagnitude = player.Rac1AnalogueMagnitude;
            double walkTarget = player.Rac1TargetPlanarStep;
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
            double cardinalYaw = player.Rac1MovementTargetYaw;
            Require(runSpeed > walkSpeed + 1.0f, "full stick did not progress from walk toward run");
            Require(player.Rac1TargetPlanarStep > walkTarget, "full stick target did not exceed walk target");

            SetAnalogueSmokeInput(0.65f, 0.80f);
            await PhysicsFramesAsync(24);
            double diagonalYaw = player.Rac1MovementTargetYaw;
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
                player.DevelopmentControls.ResetToSpawn();
                await WaitForGroundedAsync(player, 180);
                SetAnalogueSmokeInput(0f, 1f);
                await PhysicsFramesAsync(48);
                Require(player.IsOnFloor() && HorizontalSpeed(player) > 5.5f,
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
            player.DevelopmentControls.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            Input.ActionPress(RawGamepadInput.Crouch);
            await PhysicsFramesAsync(4);
            Require(
                player.Rac1LocomotionState.ToString() is "Crouched" or "CrouchTurning",
                $"crouch action did not reach the common controller; state={player.Rac1LocomotionState}; " +
                $"phase={player.Rac1MovementPhase}; grounded={player.IsOnFloor()}; alive={player.Rac1GameplayAlive}");
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
            Input.ActionRelease(RawGamepadInput.Crouch);
            if (destination.Game == ObpSourceGame.Rac1)
                await WaitForPlayerAvatarSequenceAsync(0, 30, "crouch release presentation");
            await PhysicsFramesAsync(20);

            if (destination.Game == ObpSourceGame.Rac1)
            {
                await RequireOrdinaryStopRestartAsync(player);
                await RequireRunToCrouchStopAsync(player);
                await RequireBlockedRunCrouchCarryAsync(player);
            }

            player.DevelopmentControls.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            float shortJump = await MeasureJumpAsync(player, holdFrames: 2, applyPartialAirControl: false);

            player.DevelopmentControls.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            float longJump = await MeasureJumpAsync(player, holdFrames: 16, applyPartialAirControl: false);
            Require(longJump > shortJump + 0.20f, "held jump did not exceed tap jump apex");

            player.DevelopmentControls.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            float partialAirTravel = await MeasureAirControlAsync(player);
            Require(partialAirTravel > 0.10f, "partial-stick air control produced no planar travel");

            player.DevelopmentControls.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            SendPhysicalKey(Key.W, true);
            await PhysicsFramesAsync(30);
            float keyboardSpeed = HorizontalSpeed(player);
            SendPhysicalKey(Key.W, false);
            Require(keyboardSpeed > 0.1f, "WASD keyboard fallback produced no movement");
            await PhysicsFramesAsync(3);
            Vector3 beforeFly = player.GlobalPosition;
            await TapPhysicalKeyAsync(Key.F);
            Require(player.DevelopmentControls.FlyEnabled, "F did not enable development fly");
            SendPhysicalKey(Key.W, true);
            await PhysicsFramesAsync(18);
            SendPhysicalKey(Key.W, false);
            float flyTravel = HorizontalDistance(beforeFly, player.GlobalPosition);
            Require(flyTravel > 0.5f, "development fly did not move with keyboard input");
            await TapPhysicalKeyAsync(Key.F);
            Require(!player.DevelopmentControls.FlyEnabled, "F did not disable development fly");

            Vector3 spawn = player.DevelopmentControls.SpawnPosition;
            if (destination.Game == ObpSourceGame.Rac1)
                player.DevelopmentControls.ResetToSpawn();
            else
                await TapPhysicalKeyAsync(Key.R);
            await WaitForGroundedAsync(player, 180);
            float respawnError = HorizontalDistance(spawn, player.GlobalPosition);
            Require(respawnError < 1.0f, "development respawn did not return to the host spawn");
            Require(player.IsOnFloor(), "respawned player did not re-enter Godot floor contact");
            if (destination.Game == ObpSourceGame.Rac1)
            {
                Require(
                    player.Rac1MaxObservedSlideCollisions < player.MaxSlides,
                    $"R&C1 ordinary movement saturated Godot MaxSlides={player.MaxSlides}; " +
                    $"observed={player.Rac1MaxObservedSlideCollisions}");
            }

            if (destination.Game == ObpSourceGame.Rac1)
            {
                await RequireRac1AirborneSmokeAsync(player);
                await RequireRac1LandingRegressionSmokeAsync(player);
                await RequireRac1TraversalContactSmokeAsync(player);
                await RequireRac1SwingshotSmokeAsync(player);
            }

            GD.Print(
                $"[movement-smoke] PASS {destination.DestinationId} " +
                $"walk={walkSpeed:0.000} run={runSpeed:0.000} movingJump={movingJump:0.000} shortJump={shortJump:0.000} " +
                $"longJump={longJump:0.000} partialAir={partialAirTravel:0.000} keyboard={keyboardSpeed:0.000} " +
                $"fly={flyTravel:0.000} respawnError={respawnError:0.000} " +
                $"maxSlideContacts={player.Rac1MaxObservedSlideCollisions}/{player.MaxSlides}");
            ApplicationLifecycle.RequestQuit(this, "movement-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[movement-smoke] FAIL {destination.DestinationId}: {ex.Message}");
            ApplicationLifecycle.RequestQuit(this, "movement-smoke-fail", 1);
        }
        finally
        {
            ClearMovementSmokeInput();
            SendPhysicalKey(Key.W, false);
            SendPhysicalKey(Key.Space, false);
        }
    }

    private async System.Threading.Tasks.Task<float> MeasureJumpAsync(
        PlayerHost player,
        int holdFrames,
        bool applyPartialAirControl,
        bool movingAtLaunch = false)
    {
        float baseline = player.GlobalPosition.Y;
        float apex = baseline;
        bool sawAir = false;

        Input.ActionPress(RawGamepadInput.Jump);
        await PhysicsFramesAsync(holdFrames);
        Input.ActionRelease(RawGamepadInput.Jump);

        for (int frame = 0; frame < 240; frame++)
        {
            await PhysicsFramesAsync(1);
            if (!player.IsOnFloor())
            {
                if (!sawAir)
                {
                    sawAir = true;
                    if (_activeDestination?.Game == ObpSourceGame.Rac1)
                    {
                        await WaitForPlayerAvatarSequenceAsync(
                            Rac1RatchetSequenceSelection.SelectJumpSequence(movingAtLaunch),
                            30,
                            movingAtLaunch ? "moving jump" : "stationary jump");
                    }
                }
                if (applyPartialAirControl && frame == 0)
                    SetAnalogueSmokeInput(0.58f, 0f);
            }

            apex = Math.Max(apex, player.GlobalPosition.Y);
            if (sawAir && player.IsOnFloor())
            {
                if (movingAtLaunch && _activeDestination?.Game == ObpSourceGame.Rac1)
                {
                    // Retain movement across touchdown: clearing it here hid
                    // the permanent loss of the native locomotion clock.
                    for (int update = 0; update < 5; update++)
                    {
                        await PhysicsFramesAsync(1);
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        var controller = _playerAvatarAnimationController as Rac1PlayerAnimationPresentationController;
                        Require(controller?.OrdinaryLocomotionSample is not null &&
                            controller.Current.SampledPose is not null && PlayerAvatarPresentationIsSynchronized(),
                            "moving landing did not reconnect physics-sampled locomotion");
                        int sequence = controller!.OrdinaryLocomotionSample!.Value.Sequence;
                        // This traversal can leave support again immediately.
                        // Native ordinary edge updates retain the assigned rate;
                        // only supported helper calls consume the current scalar.
                        if (player.Rac1AnimationHelperAdmitted)
                        {
                            float expected = Rac1OrdinaryLocomotionAnimation.PlaybackMultiplier(
                                sequence, player.Rac1AnimationScalarAtSelection);
                            Require(Math.Abs(controller.OrdinaryLocomotionMultiplier!.Value - expected) < .00001f,
                                $"post-jump locomotion lost its recovered speed multiplier: update={update}, " +
                                $"actual={controller.OrdinaryLocomotionMultiplier}, expected={expected}, " +
                                $"sequence={sequence}, scalar={player.Rac1AnimationScalarAtSelection}");
                        }
                    }
                    GD.Print("[movement-smoke] moving landing retained the native locomotion clock and speed multiplier");
                }
                ClearMovementSmokeInput();
                return apex - baseline;
            }
        }

        throw new InvalidOperationException("jump did not land within smoke timeout");
    }

    private async System.Threading.Tasks.Task<float> MeasureAirControlAsync(PlayerHost player)
    {
        Vector3 start = player.GlobalPosition;
        Input.ActionPress(RawGamepadInput.Jump);
        await PhysicsFramesAsync(10);
        Input.ActionRelease(RawGamepadInput.Jump);

        bool sawAir = false;
        for (int frame = 0; frame < 240; frame++)
        {
            await PhysicsFramesAsync(1);
            if (!player.IsOnFloor())
            {
                if (!sawAir)
                {
                    sawAir = true;
                    start = player.GlobalPosition;
                    SetAnalogueSmokeInput(0.58f, 0f);
                }
            }
            else if (sawAir)
            {
                float travel = HorizontalDistance(start, player.GlobalPosition);
                ClearMovementSmokeInput();
                return travel;
            }
        }

        throw new InvalidOperationException("partial-air-control jump did not land within smoke timeout");
    }

    private async System.Threading.Tasks.Task WaitForGroundedAsync(PlayerHost player, int maxFrames)
    {
        int supportedUpdates = 0;
        for (int frame = 0; frame < maxFrames; frame++)
        {
            await PhysicsFramesAsync(1);
            // PhysicsFrame is emitted before node physics callbacks. Inspect
            // the completed update and require settled controller support;
            // the floor bit can still describe the pre-teleport collision.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            supportedUpdates = player.IsOnFloor() &&
                player.Rac1MovementPhase == Rac1RatchetMovementPhase.Grounded
                    ? supportedUpdates + 1 : 0;
            if (supportedUpdates >= 3)
                return;
        }

        throw new InvalidOperationException("player did not reach Godot floor contact within smoke timeout");
    }

    private async System.Threading.Tasks.Task WaitForAnimationStateAsync(
        PlayerHost player,
        PlayerAnimationState expected,
        int maxFrames,
        string label)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            await PhysicsFramesAsync(1);
            if (player.AnimationState == expected)
                return;
        }

        throw new InvalidOperationException(
            $"{label} did not reach animation state {expected}; current={player.AnimationState}");
    }

    private async System.Threading.Tasks.Task WaitForPlayerAvatarSequenceAsync(
        int expectedSequence,
        int maxFrames,
        string label)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (CurrentPlayerAvatarSourceSequence() == expectedSequence &&
                PlayerAvatarPresentationIsSynchronized())
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"{label} did not reach synchronized player-avatar sequence {expectedSequence}; " +
            $"current={CurrentPlayerAvatarSourceSequence()?.ToString() ?? "none"}; " +
            $"synchronized={PlayerAvatarPresentationIsSynchronized()}; " +
            $"avatarFrame={_playerAvatarView?.CurrentFrame}/{_playerAvatarView?.FrameCount}; " +
            $"avatarFps={_playerAvatarView?.FramesPerSecond}; " +
            $"loop={_playerAvatarAnimationController?.Current.Loop}; " +
            $"semantic={_player?.AnimationState}; " +
            $"phase={_player?.Rac1MovementPhase}; yawMode={_player?.Rac1YawMode}; " +
            $"targetStep={_player?.Rac1TargetPlanarStep}; " +
            $"grounded={_player?.IsOnFloor()}; recoveredSupport={_player?.Rac1RecoveredSupportGrounded}");
    }

    private async System.Threading.Tasks.Task RequireWalkingAvatarLoopAsync(int frames)
    {
        int previousFrame = _playerAvatarView?.CurrentFrame ?? -1;
        bool advanced = false;
        bool wrapped = false;
        for (int frame = 0; frame < frames; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            int currentFrame = _playerAvatarView?.CurrentFrame ?? -1;
            Require(
                currentFrame >= 0 &&
                CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.LocomotionStartSequenceId &&
                _playerAvatarAnimationController?.Current.Loop == true &&
                PlayerAvatarPresentationIsSynchronized(),
                $"steady low-stick walk lost synchronized looping sequence 3; frame={currentFrame}; " +
                $"sequence={CurrentPlayerAvatarSourceSequence()}; semantic={_player?.AnimationState}");
            if (_activeDestination?.Game == ObpSourceGame.Rac1)
            {
                Require(_playerAvatarAnimationController is Rac1PlayerAnimationPresentationController
                { OrdinaryLocomotionSample: not null, OrdinaryLocomotionMultiplier: > 2.35f and < 2.36f } &&
                    _playerAvatarAnimationSink?.CurrentAnimationPresentation.SampledPose is not null,
                    "steady low-stick walk is not using the physics-sampled native locomotion clock/rate");
            }
            advanced |= currentFrame > previousFrame;
            wrapped |= currentFrame < previousFrame;
            previousFrame = currentFrame;
        }
        Require(advanced && wrapped,
            $"steady walking animation did not advance and wrap; advanced={advanced}; " +
            $"wrapped={wrapped}; frame={previousFrame}");
        GD.Print("[movement-smoke] steady walking sequence 3 advanced and wrapped");
    }

    private async System.Threading.Tasks.Task PhysicsFramesAsync(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
    private async System.Threading.Tasks.Task TapPhysicalKeyAsync(Key key)
    {
        SendPhysicalKey(key, true);
        await PhysicsFramesAsync(2);
        SendPhysicalKey(key, false);
        await PhysicsFramesAsync(2);
    }

    private async System.Threading.Tasks.Task RequireOrdinaryStopRestartAsync(PlayerHost player)
    {
        foreach (float strength in new[] { .6f, 1f })
        {
            ClearMovementSmokeInput();
            player.DevelopmentControls.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            SetAnalogueSmokeInput(0, strength);
            await PhysicsFramesAsync(48);
            SetAnalogueSmokeInput(0, 0);
            bool sawStop = false;
            for (int update = 0; update < 12; update++)
            {
                await PhysicsFramesAsync(1);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                int? sequence = CurrentPlayerAvatarSourceSequence();
                sawStop |= sequence is 5 or 6 or 20;
                if (strength == 1 && sawStop)
                    break;
            }
            var controller = (Rac1PlayerAnimationPresentationController)_playerAvatarAnimationController!;
            if (strength == 1)
            {
                Require(sawStop && controller.OrdinaryStopSample is not null &&
                    controller.Current.SampledPose is not null && PlayerAvatarPresentationIsSynchronized(),
                    "run release did not enter the sampled native stop family");
                // Reverse direction while the stop blend is still active.
                SetAnalogueSmokeInput(0, -1);
                await PhysicsFramesAsync(3);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(controller.OrdinaryLocomotionSample is not null &&
                    controller.OrdinaryStopSample is null && CurrentPlayerAvatarSourceSequence() == 3 &&
                    PlayerAvatarPresentationIsSynchronized(), "quick reverse restart did not re-enter native locomotion");
                SetAnalogueSmokeInput(0, 0);
            }
            await PhysicsFramesAsync(100);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(CurrentPlayerAvatarSourceSequence() == 0 && PlayerAvatarPresentationIsSynchronized(),
                "ordinary release did not settle to synchronized standing");
            if (strength < 1)
                Require(!sawStop, "low-speed walk release unexpectedly used the running stop family");
            GD.Print($"[movement-smoke] ordinary stop/restart passed strength={strength:F1}, stopFamily={sawStop}");
        }
    }

    private async System.Threading.Tasks.Task RequireRunToCrouchStopAsync(PlayerHost player)
    {
        foreach (bool keepDirection in new[] { true, false })
        {
            ClearMovementSmokeInput();
            player.DevelopmentControls.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            SetAnalogueSmokeInput(0f, 1f);
            await PhysicsFramesAsync(48);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float startSpeed = HorizontalSpeed(player);
            Require(startSpeed > 5.5f && player.IsOnFloor(), "run-to-crouch trial did not start at supported full speed");
            var contactMotion = player.Rac1ContactProcessedMotion;
            Require(contactMotion.HasValue, "run-to-crouch trial did not record static-contact motion");
            double entryPlanar = Math.Sqrt(contactMotion!.Value.X * contactMotion.Value.X +
                contactMotion.Value.Y * contactMotion.Value.Y);
            Vector3 start = player.GlobalPosition;
            if (!keepDirection)
                SetAnalogueSmokeInput(0f, 0f);
            Input.ActionPress(RawGamepadInput.Crouch);
            int stoppedAt = 0;
            ulong startedPhysicsFrame = Engine.GetPhysicsFrames();
            float previousSpeed = startSpeed;
            for (int tick = 1; tick <= 65; tick++)
            {
                await PhysicsFramesAsync(1);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                float speed = HorizontalSpeed(player);
                Require(player.Rac1LocomotionState is Rac1RatchetLocomotionState.Crouched or Rac1RatchetLocomotionState.CrouchTurning,
                    $"run-to-crouch lost its state at tick {tick}; phase={player.Rac1MovementPhase}; floor={player.IsOnFloor()}; speed={speed:F4}");
                Require(speed <= previousSpeed + 0.01f,
                    $"run-to-crouch accelerated at tick {tick}: {previousSpeed:F4} -> {speed:F4}");
                if (player.Rac1ControllerPlanarStep < 1e-8d && stoppedAt == 0)
                    stoppedAt = checked((int)(Engine.GetPhysicsFrames() - startedPhysicsFrame));
                previousSpeed = speed;
            }
            float distance = HorizontalDistance(start, player.GlobalPosition);
            var entryCarry = Rac1ContactMotionProjection.EnterCrouch(contactMotion.Value);
            double entryCarryPlanar = Math.Sqrt(entryCarry.X * entryCarry.X + entryCarry.Y * entryCarry.Y);
            int expectedStop = (int)Math.Ceiling(entryCarryPlanar / Rac1RatchetMovementController.CrouchDecelerationPerTick);
            // Count actual physics updates. Awaiting a render frame between
            // observations can skip a physics tick under host scheduling load.
            Require(stoppedAt > 0 && Math.Abs(stoppedAt - expectedStop) <= 2,
                $"run-to-crouch stop timing changed: stop={stoppedAt}, expected={expectedStop}, distance={distance:F4}");
            // Distance is an observation, pending a matched retail trajectory.
            // Do not require OBP's current result as a retail-distance oracle.
            double standingHeight = _playerAvatar?.AnimationBounds.Height ?? double.NaN;
            GD.Print($"[movement-smoke] run-to-crouch heldDirection={keepDirection} startSpeed={startSpeed:F4} entryContactPlanar={entryPlanar:F6} stopTicks={stoppedAt} distance={distance:F4} standingHeights={distance / standingHeight:F4}");
            ClearMovementSmokeInput();
        }
    }

    private async System.Threading.Tasks.Task RequireBlockedRunCrouchCarryAsync(PlayerHost player)
    {
        // Synthetic Godot fixture: verifies the adapter consumes completed
        // collision feedback. This is not evidence for native wall collision.
        ClearMovementSmokeInput();
        player.DevelopmentControls.ResetToSpawn();
        await WaitForGroundedAsync(player, 180);
        SetAnalogueSmokeInput(0f, 1f);
        await PhysicsFramesAsync(48);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Vector3 direction = new Vector3(player.Velocity.X, 0, player.Velocity.Z).Normalized();
        var wall = new StaticBody3D { Name = "CrouchCarrySmokeWall" };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6, 5, 0.4f) } });
        AddChild(wall);
        wall.GlobalPosition = player.GlobalPosition + direction * 0.9f + Vector3.Up * 2;
        wall.Rotation = new Vector3(0, Mathf.Atan2(direction.X, direction.Z), 0);
        try
        {
            await PhysicsFramesAsync(30);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var motion = player.Rac1ContactProcessedMotion;
            Require(motion.HasValue && Math.Sqrt(motion.Value.X * motion.Value.X + motion.Value.Y * motion.Value.Y) < 0.02,
                $"blocked run did not produce bounded contact carry: {motion}");
            Input.ActionPress(RawGamepadInput.Crouch);
            await PhysicsFramesAsync(2);
            wall.QueueFree();
            await PhysicsFramesAsync(2);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Vector3 start = player.GlobalPosition;
            await PhysicsFramesAsync(30);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float distance = HorizontalDistance(start, player.GlobalPosition);
            Require(distance < 0.3f, $"crouch retained phantom run momentum after wall removal: {distance:F4}");
            GD.Print($"[movement-smoke] synthetic-wall crouch release distance={distance:F4}");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(wall) && !wall.IsQueuedForDeletion())
                wall.QueueFree();
            ClearMovementSmokeInput();
        }
    }

    private static void SendPhysicalKey(Key key, bool pressed)
    {
        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = key,
            PhysicalKeycode = key,
            Pressed = pressed,
            Echo = false,
        });
    }

    private static void SetAnalogueSmokeInput(float right, float forward)
    {
        ReleaseMovementActions();
        if (right > 0f) Input.ActionPress(RawGamepadInput.MoveRight, right);
        else if (right < 0f) Input.ActionPress(RawGamepadInput.MoveLeft, -right);

        if (forward > 0f) Input.ActionPress(RawGamepadInput.MoveForward, forward);
        else if (forward < 0f) Input.ActionPress(RawGamepadInput.MoveBack, -forward);
    }

    private static void SetRac1SmokeConditionedInput(double right, double forward)
    {
        // Invert the recovered per-axis deadzone. A unit circle supplied as
        // raw axes otherwise becomes a walk at diagonal headings, despite the
        // route driver requesting a full run.
        static float RawComponent(double conditioned) => Math.Abs(conditioned) < 1e-8d
            ? 0f : (float)Math.CopySign((48d + 76d * Math.Abs(conditioned)) / 128d, conditioned);
        SetAnalogueSmokeInput(RawComponent(right), RawComponent(forward));
    }

    private static void ClearMovementSmokeInput()
    {
        ReleaseMovementActions();
        Input.ActionRelease(RawGamepadInput.Jump);
        Input.ActionRelease(RawGamepadInput.Crouch);
        Input.ActionRelease(RawGamepadInput.Action);
        Input.ActionRelease(RawGamepadInput.Traversal);
    }

    private static void ReleaseMovementActions()
    {
        Input.ActionRelease(RawGamepadInput.MoveLeft);
        Input.ActionRelease(RawGamepadInput.MoveRight);
        Input.ActionRelease(RawGamepadInput.MoveForward);
        Input.ActionRelease(RawGamepadInput.MoveBack);
    }

    private static float HorizontalSpeed(PlayerHost player) =>
        new Vector2(player.Velocity.X, player.Velocity.Z).Length();

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        new Vector2(a.X - b.X, a.Z - b.Z).Length();

    private static double WrapAngle(double value)
    {
        while (value > Math.PI) value -= Math.Tau;
        while (value <= -Math.PI) value += Math.Tau;
        return value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
