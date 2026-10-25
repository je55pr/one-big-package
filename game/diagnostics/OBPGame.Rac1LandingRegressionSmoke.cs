using Godot;
using OBP.RAC1.Player;

namespace OBP.Game;

public partial class OBPGame
{
    /// <summary>
    /// Explicit synthetic flat-ground regressions for quiet landings, native
    /// standing interpolation and the two distinct long-jump controllers.
    /// Distances are integration observations, not fitted retail coefficients.
    /// </summary>
    private async System.Threading.Tasks.Task RequireRac1LandingRegressionSmokeAsync(PlayerHost player)
    {
        Vector3 savedPosition = player.GlobalPosition;
        Vector3 savedRotation = player.Rotation;
        double savedYaw = player.Rac1CurrentYaw;
        var savedPack = player.Rac1ActiveBackpack;
        bool savedClank = player.Rac1ClankAvailable;
        int? savedPreview = player.Rac1DevelopmentPackPreview;
        bool savedSwingshotPreview = player.Rac1DevelopmentSwingshotPreview;
        var fixture = new Node3D
        {
            Name = "SyntheticRac1LandingRegressionFixture",
            Position = new Vector3(2000f, 64f, 2600f),
        };
        AddChild(fixture);
        try
        {
            ClearMovementSmokeInput();
            player.Rac1ActiveBackpack = null;
            player.Rac1ClankAvailable = false;
            if (player.Rac1DevelopmentSwingshotPreview)
                player.ToggleRac1DevelopmentSwingshot();
            AddSyntheticTraversalMesh(fixture, "SyntheticOrdinaryLandingFloor", 11,
                (new(-150f, 0f, -80f), new(150f, 0f, -80f), new(150f, 0f, 80f)),
                (new(-150f, 0f, -80f), new(150f, 0f, 80f), new(-150f, 0f, 80f)));
            await PhysicsFramesAsync(3);

            SelectLandingPack(null);
            await ResetLandingFixtureAsync(-40f);
            SetAnalogueSmokeInput(0f, .60f);
            await PhysicsFramesAsync(24);
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(12);
            ClearMovementSmokeInput();
            var ordinary = await ObserveLandingAsync("low-drift ordinary jump", requireNoRunFlash: true);
            await RequireStandingInterpolationAsync("low-drift ordinary jump");
            GD.Print($"[movement-smoke] synthetic quiet ordinary landing/standing passed; coast={ordinary.Coast:F4}");

            SelectLandingPack(null);
            await ResetLandingFixtureAsync(-20f);
            Input.ActionPress(RawGamepadInput.Crouch);
            SetSyntheticNativeHeading(player, Math.PI / 2d);
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(3);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(player.Rac1TraversalAction == 0x0b && CurrentPlayerAvatarSourceSequence() is 28 or 29 or 31,
                "synthetic directional crouch/Cross did not enter the flip family");
            ClearMovementSmokeInput();
            var flip = await ObserveLandingAsync("neutral side-flip landing", requireNoRunFlash: true);
            await RequireStandingInterpolationAsync("neutral side-flip landing");
            GD.Print($"[movement-smoke] synthetic neutral side-flip landing/standing passed; coast={flip.Coast:F4}");

            SelectLandingPack(null);
            await ResetLandingFixtureAsync(0f);
            Input.ActionPress(RawGamepadInput.Crouch);
            SetSyntheticNativeHeading(player, Math.PI / 2d);
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(3);
            Require(player.Rac1TraversalAction == 0x0b,
                "synthetic held-crouch side-flip did not enter native action0b");
            ReleaseMovementActions();
            Input.ActionRelease(RawGamepadInput.Jump);
            var heldFlip = await ObserveLandingAsync("held-crouch stationary side-flip landing",
                requireNoRunFlash: true, expectCrouch: true);
            Require(heldFlip.Coast < .15f,
                $"stationary side-flip inherited directional flight as grounded crouch carry: coast={heldFlip.Coast:F4}");
            Require(CurrentPlayerAvatarSourceSequence() == 13 &&
                _playerAvatarAnimationController?.Current.SourceStateKey == "4" &&
                (_playerAvatarAnimationController as Rac1PlayerAnimationPresentationController)?.CrouchSample is not null,
                "held-crouch side-flip did not settle in sampled native crouch action4/sequence13");
            Input.ActionRelease(RawGamepadInput.Crouch);
            for (int tick = 0; tick < 90; tick++)
            {
                await PhysicsFramesAsync(1);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (CurrentPlayerAvatarSourceSequence() == 0 &&
                    (_playerAvatarAnimationController as Rac1PlayerAnimationPresentationController)?.CrouchSample is null)
                    break;
            }
            await RequireStandingInterpolationAsync("held-crouch side-flip release");
            GD.Print($"[movement-smoke] synthetic held-crouch side-flip/action4/release passed; coast={heldFlip.Coast:F4}");

            foreach (int pack in new[] { 2, 3 })
            {
                SelectLandingPack(pack);
                await ResetLandingFixtureAsync(pack == 2 ? 20f : 40f);
                for (int tick = 0; tick < 48; tick++)
                {
                    SetSyntheticNativeHeading(player, 0d);
                    await PhysicsFramesAsync(1);
                }
                Input.ActionPress(RawGamepadInput.Crouch);
                for (int tick = 0; tick < 8; tick++)
                {
                    SetSyntheticNativeHeading(player, 0d);
                    await PhysicsFramesAsync(1);
                }
                Input.ActionPress(RawGamepadInput.Jump);
                await PhysicsFramesAsync(2);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                int action = pack == 2 ? 0x0a : 0x10;
                int sequence = pack == 2 ? 18 : 38;
                Require(player.Rac1TraversalAction == action && CurrentPlayerAvatarSourceSequence() == sequence,
                    $"synthetic pack{pack} run/crouch/Cross did not enter long jump: " +
                    $"action={player.Rac1TraversalAction}, sequence={CurrentPlayerAvatarSourceSequence()}");
                for (int tick = 0; tick < 30 && player.Rac1MovementPhase == Rac1RatchetMovementPhase.JumpAnticipation; tick++)
                {
                    SetSyntheticNativeHeading(player, 0d);
                    await PhysicsFramesAsync(1);
                }
                Require(player.Rac1MovementPhase == Rac1RatchetMovementPhase.Rising,
                    $"synthetic pack{pack} long jump did not launch");
                ulong launchPhysicsFrame = Engine.GetPhysicsFrames();
                Input.ActionRelease(RawGamepadInput.Crouch);
                int fastFlightSamples = 0;
                int consecutiveFastSamples = 0;
                int longestFastRun = 0;
                float maximumFlightSpeed = 0f;
                for (int tick = 0; tick < 12; tick++)
                {
                    SetSyntheticNativeHeading(player, 0d);
                    await PhysicsFramesAsync(1);
                    float speed = HorizontalSpeed(player);
                    maximumFlightSpeed = Math.Max(maximumFlightSpeed, speed);
                    if (player.Rac1TraversalAction == action && speed > 6.5f)
                    {
                        fastFlightSamples++;
                        consecutiveFastSamples++;
                        longestFastRun = Math.Max(longestFastRun, consecutiveFastSamples);
                    }
                    else
                        consecutiveFastSamples = 0;
                }
                ClearMovementSmokeInput();
                var landing = await ObserveLandingAsync($"pack{pack} neutral long-jump landing",
                    requireNoRunFlash: true, launchPhysicsFrame: launchPhysicsFrame);
                if (pack == 2)
                    Require(longestFastRun >= 4,
                        $"Heli-Pack long jump never sustained its recovered accelerated flight request: " +
                        $"samples={fastFlightSamples}, consecutive={longestFastRun}, max={maximumFlightSpeed:F4}");
                else
                    Require(landing.Coast < 2f,
                        $"Thruster-Pack long-jump landing carried excessive unsupported momentum: coast={landing.Coast:F4}");
                await RequireStandingInterpolationAsync($"pack{pack} long-jump landing");
                GD.Print($"[movement-smoke] synthetic pack{pack} long-jump/neutral-landing passed; " +
                    $"action={action:x}, maxFlightSpeed={maximumFlightSpeed:F4}, " +
                    $"flightTicks={landing.FlightTicks}, coast={landing.Coast:F4}");
            }
        }
        finally
        {
            ClearMovementSmokeInput();
            if (GodotObject.IsInstanceValid(fixture) && !fixture.IsQueuedForDeletion())
                fixture.QueueFree();
            player.Rac1ActiveBackpack = savedPack;
            player.Rac1ClankAvailable = savedClank;
            SelectLandingPack(savedPreview);
            if (player.Rac1DevelopmentSwingshotPreview != savedSwingshotPreview)
                player.ToggleRac1DevelopmentSwingshot();
            player.Rotation = savedRotation;
            player.Rac1CurrentYaw = savedYaw;
            player.ApplyDevelopmentReset(savedPosition);
            await PhysicsFramesAsync(3);
        }

        void SelectLandingPack(int? pack)
        {
            for (int cycle = 0; player.Rac1DevelopmentPackPreview != pack && cycle < 3; cycle++)
                player.CycleRac1DevelopmentPack();
            Require(player.Rac1DevelopmentPackPreview == pack, "could not select synthetic landing pack preview");
        }

        async System.Threading.Tasks.Task ResetLandingFixtureAsync(float lane)
        {
            ClearMovementSmokeInput();
            player.Rac1CurrentYaw = 0d;
            player.ApplyDevelopmentReset(fixture.GlobalPosition + new Vector3(10f, .08f, lane));
            await PhysicsFramesAsync(4);
            await WaitForGroundedAsync(player, 90);
        }

        async System.Threading.Tasks.Task<(float Coast, ulong FlightTicks)> ObserveLandingAsync(
            string label, bool requireNoRunFlash, ulong? launchPhysicsFrame = null, bool expectCrouch = false)
        {
            bool sawFlight = player.GlobalPosition.Y > fixture.GlobalPosition.Y + .15f;
            Vector3? touchdown = null;
            ulong firstFrame = launchPhysicsFrame ?? Engine.GetPhysicsFrames();
            ulong flightTicks = 0;
            float greatestCoast = 0f;
            int quietUpdates = 0;
            for (int tick = 0; tick < 360; tick++)
            {
                await PhysicsFramesAsync(1);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                sawFlight |= player.GlobalPosition.Y > fixture.GlobalPosition.Y + .15f;
                if (touchdown is null && sawFlight && player.IsOnFloor())
                {
                    touchdown = player.GlobalPosition;
                    flightTicks = Engine.GetPhysicsFrames() - firstFrame;
                }
                if (touchdown is not { } point)
                    continue;
                greatestCoast = Math.Max(greatestCoast, HorizontalDistance(point, player.GlobalPosition));
                if (requireNoRunFlash)
                    Require(CurrentPlayerAvatarSourceSequence() != 4,
                        $"synthetic {label} flashed source run sequence4 after neutral touchdown: " +
                        $"speed={HorizontalSpeed(player):F4}, phase={player.Rac1MovementPhase}");
                bool quiet = player.IsOnFloor() && player.Rac1MovementPhase == Rac1RatchetMovementPhase.Grounded &&
                    player.Rac1TraversalAction is null && HorizontalSpeed(player) < .15f &&
                    CurrentPlayerAvatarSourceSequence() == (expectCrouch ? 13 : 0);
                quietUpdates = quiet ? quietUpdates + 1 : 0;
                if (quietUpdates >= 5)
                    return (greatestCoast, flightTicks);
            }
            var presentation = _playerAvatarAnimationController as Rac1PlayerAnimationPresentationController;
            throw new InvalidOperationException($"synthetic {label} did not settle after touchdown: " +
                $"touchdown={touchdown}, speed={HorizontalSpeed(player):F4}, " +
                $"phase={player.Rac1MovementPhase}, action={player.Rac1TraversalAction}, " +
                $"sequence={CurrentPlayerAvatarSourceSequence()}, coast={greatestCoast:F4}, " +
                $"standingSample={presentation?.OrdinaryStandingSample}, " +
                $"semantic={presentation?.Current.SemanticState}, frame={_playerAvatarView?.CurrentFrame}");
        }

        async System.Threading.Tasks.Task RequireStandingInterpolationAsync(string label)
        {
            var controller = _playerAvatarAnimationController as Rac1PlayerAnimationPresentationController;
            static bool HasNativeStandingOwner(Rac1PlayerAnimationPresentationController? current) =>
                current?.OrdinaryStandingSample is not null || current?.OrdinaryStopSample is { Sequence: 0 };
            // Native Heli landing action 3 retains its stop clock after
            // that clock enters sequence 0. Both owners sample the same native
            // standing pose on physics updates; their ownership is distinct.
            Require(HasNativeStandingOwner(controller) && controller!.Current.SampledPose is not null,
                $"synthetic {label} did not retain the native sampled standing clock");
            var previous = controller!.Current.SampledPose!;
            bool advanced = false;
            bool changedBetweenKeyframes = false;
            for (int tick = 0; tick < 60; tick++)
            {
                await PhysicsFramesAsync(1);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(CurrentPlayerAvatarSourceSequence() == 0 && HasNativeStandingOwner(controller) &&
                    controller.Current.SampledPose is not null && PlayerAvatarPresentationIsSynchronized(),
                    $"synthetic {label} lost synchronized source standing after landing");
                var current = controller.Current.SampledPose!;
                bool changed = !current.LocalPositions.AsSpan().SequenceEqual(previous.LocalPositions);
                advanced |= changed;
                changedBetweenKeyframes |= changed && current.FrameIndex == previous.FrameIndex;
                previous = current;
            }
            Require(advanced && changedBetweenKeyframes,
                $"synthetic {label} standing froze or reverted to discrete keyframes: " +
                $"advanced={advanced}, betweenFrames={changedBetweenKeyframes}");
        }
    }
}
