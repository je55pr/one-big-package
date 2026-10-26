using System.Numerics;
using System.Buffers.Binary;
using OBP.Core;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

public sealed class Rac1LandingRegression(MovementDiagnosticContext context) : MovementScenarioSupport(context)
{
    /// <summary>
    /// Explicit synthetic flat-ground regressions for quiet landings, native
    /// standing interpolation and the two distinct long-jump controllers.
    /// Distances are integration observations, not fitted retail coefficients.
    /// </summary>
    public async Task RunAsync()
    {
        var player = Player;
        var saved = player.Read();
        var fixture = Driver.Own(Geometry.Create("SyntheticRac1LandingRegressionFixture", new Vector3(2000f, 64f, 2600f)));
        try
        {
            ClearMovementSmokeInput();
            player.SetBackpack(null);
            player.SetClank(false);
            if (player.Read().Rac1DevelopmentSwingshotPreview)
            { player.ToggleSwingshot(); }
            AddSyntheticTraversalMesh(fixture, "SyntheticOrdinaryLandingFloor", 11,
                (new(-150f, 0f, -80f), new(150f, 0f, -80f), new(150f, 0f, 80f)),
                (new(-150f, 0f, -80f), new(150f, 0f, 80f), new(-150f, 0f, 80f)));
            await PhysicsFramesAsync(3);

            SelectLandingPack(null);
            await ResetLandingFixtureAsync(-40f);
            SetAnalogueSmokeInput(0f, .60f);
            await PhysicsFramesAsync(24);
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            await PhysicsFramesAsync(12);
            ClearMovementSmokeInput();
            var ordinary = await ObserveLandingAsync("low-drift ordinary jump", requireNoRunFlash: true);
            await RequireStandingInterpolationAsync("low-drift ordinary jump");
            Log($"[movement-smoke] synthetic quiet ordinary landing/standing passed; coast={ordinary.Coast:F4}");

            SelectLandingPack(null);
            await ResetLandingFixtureAsync(-20f);
            Driver.SetAction(MovementDiagnosticDriver.Crouch, true);
            SetSyntheticNativeHeading(player, Math.PI / 2d);
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            await PhysicsFramesAsync(3);
            await ProcessFrameAsync();
            Require(player.Read().Rac1TraversalAction == 0x0b && CurrentPlayerAvatarSourceSequence() is 28 or 29 or 31,
                "synthetic directional crouch/Cross did not enter the flip family");
            ClearMovementSmokeInput();
            var flip = await ObserveLandingAsync("neutral side-flip landing", requireNoRunFlash: true);
            await RequireStandingInterpolationAsync("neutral side-flip landing");
            Log($"[movement-smoke] synthetic neutral side-flip landing/standing passed; coast={flip.Coast:F4}");

            SelectLandingPack(null);
            await ResetLandingFixtureAsync(0f);
            Driver.SetAction(MovementDiagnosticDriver.Crouch, true);
            SetSyntheticNativeHeading(player, Math.PI / 2d);
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            await PhysicsFramesAsync(3);
            Require(player.Read().Rac1TraversalAction == 0x0b,
                "synthetic held-crouch side-flip did not enter native action0b");
            ReleaseMovementActions();
            Driver.SetAction(MovementDiagnosticDriver.Jump, false);
            var heldFlip = await ObserveLandingAsync("held-crouch stationary side-flip landing",
                requireNoRunFlash: true, expectCrouch: true);
            Require(heldFlip.Coast < .15f,
                $"stationary side-flip inherited directional flight as grounded crouch carry: coast={heldFlip.Coast:F4}");
            Require(CurrentPlayerAvatarSourceSequence() == 13 &&
                Animation.Read().SourceStateKey == "4" &&
                Animation.Read().CrouchSamplePresent,
                "held-crouch side-flip did not settle in sampled native crouch action4/sequence13");
            Driver.SetAction(MovementDiagnosticDriver.Crouch, false);
            for (int tick = 0; tick < 90; tick++)
            {
                await PhysicsFramesAsync(1);
                await ProcessFrameAsync();
                if (CurrentPlayerAvatarSourceSequence() == 0 &&
                    !Animation.Read().CrouchSamplePresent)
                { break; }
            }
            await RequireStandingInterpolationAsync("held-crouch side-flip release");
            Log($"[movement-smoke] synthetic held-crouch side-flip/action4/release passed; coast={heldFlip.Coast:F4}");

            foreach (int pack in new[] { 2, 3 })
            {
                SelectLandingPack(pack);
                await ResetLandingFixtureAsync(pack == 2 ? 20f : 40f);
                for (int tick = 0; tick < 48; tick++)
                {
                    SetSyntheticNativeHeading(player, 0d);
                    await PhysicsFramesAsync(1);
                }
                Driver.SetAction(MovementDiagnosticDriver.Crouch, true);
                for (int tick = 0; tick < 8; tick++)
                {
                    SetSyntheticNativeHeading(player, 0d);
                    await PhysicsFramesAsync(1);
                }
                Driver.SetAction(MovementDiagnosticDriver.Jump, true);
                await PhysicsFramesAsync(2);
                await ProcessFrameAsync();
                int action = pack == 2 ? 0x0a : 0x10;
                int sequence = pack == 2 ? 18 : 38;
                Require(player.Read().Rac1TraversalAction == action && CurrentPlayerAvatarSourceSequence() == sequence,
                    $"synthetic pack{pack} run/crouch/Cross did not enter long jump: " +
                    $"action={player.Read().Rac1TraversalAction}, sequence={CurrentPlayerAvatarSourceSequence()}");
                for (int tick = 0; tick < 30 && player.Read().Rac1MovementPhase == Rac1RatchetMovementPhase.JumpAnticipation; tick++)
                {
                    SetSyntheticNativeHeading(player, 0d);
                    await PhysicsFramesAsync(1);
                }
                Require(player.Read().Rac1MovementPhase == Rac1RatchetMovementPhase.Rising,
                    $"synthetic pack{pack} long jump did not launch");
                ulong launchPhysicsFrame = Driver.PhysicsFrame;
                Driver.SetAction(MovementDiagnosticDriver.Crouch, false);
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
                    if (player.Read().Rac1TraversalAction == action && speed > 6.5f)
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
                {
                    Require(longestFastRun >= 4,
                    $"Heli-Pack long jump never sustained its recovered accelerated flight request: " +
                    $"samples={fastFlightSamples}, consecutive={longestFastRun}, max={maximumFlightSpeed:F4}");
                }
                else
                    Require(landing.Coast < 2f,
                        $"Thruster-Pack long-jump landing carried excessive unsupported momentum: coast={landing.Coast:F4}");
                await RequireStandingInterpolationAsync($"pack{pack} long-jump landing");
                Log($"[movement-smoke] synthetic pack{pack} long-jump/neutral-landing passed; " +
                    $"action={action:x}, maxFlightSpeed={maximumFlightSpeed:F4}, " +
                    $"flightTicks={landing.FlightTicks}, coast={landing.Coast:F4}");
            }
        }
        finally
        {
            await RestoreFixturePlayerAsync(fixture, saved);
        }

        void SelectLandingPack(int? pack)
        {
            for (int cycle = 0; player.Read().Rac1DevelopmentPackPreview != pack && cycle < 3; cycle++)
                player.CyclePack();
            Require(player.Read().Rac1DevelopmentPackPreview == pack, "could not select synthetic landing pack preview");
        }

        async Task ResetLandingFixtureAsync(float lane)
        {
            ClearMovementSmokeInput();
            player.SetYaw(0d);
            player.ResetAt(fixture.GlobalPosition + new Vector3(10f, .08f, lane));
            await PhysicsFramesAsync(4);
            await WaitForGroundedAsync(player, 90);
        }

        async Task<(float Coast, ulong FlightTicks)> ObserveLandingAsync(
            string label, bool requireNoRunFlash, ulong? launchPhysicsFrame = null, bool expectCrouch = false)
        {
            bool sawFlight = player.Read().GlobalPosition.Y > fixture.GlobalPosition.Y + .15f;
            Vector3? touchdown = null;
            ulong firstFrame = launchPhysicsFrame ?? Driver.PhysicsFrame;
            ulong flightTicks = 0;
            float greatestCoast = 0f;
            int quietUpdates = 0;
            for (int tick = 0; tick < 360; tick++)
            {
                await PhysicsFramesAsync(1);
                await ProcessFrameAsync();
                sawFlight |= player.Read().GlobalPosition.Y > fixture.GlobalPosition.Y + .15f;
                if (touchdown is null && sawFlight && player.Read().Grounded)
                {
                    touchdown = player.Read().GlobalPosition;
                    flightTicks = Driver.PhysicsFrame - firstFrame;
                }
                if (touchdown is not { } point)
                { continue; }
                greatestCoast = Math.Max(greatestCoast, HorizontalDistance(point, player.Read().GlobalPosition));
                if (requireNoRunFlash)
                {
                    Require(CurrentPlayerAvatarSourceSequence() != 4,
                    $"synthetic {label} flashed source run sequence4 after neutral touchdown: " +
                    $"speed={HorizontalSpeed(player):F4}, phase={player.Read().Rac1MovementPhase}");
                }
                bool quiet = player.Read().Grounded && player.Read().Rac1MovementPhase == Rac1RatchetMovementPhase.Grounded &&
                    player.Read().Rac1TraversalAction is null && HorizontalSpeed(player) < .15f &&
                    CurrentPlayerAvatarSourceSequence() == (expectCrouch ? 13 : 0);
                quietUpdates = quiet ? quietUpdates + 1 : 0;
                if (quietUpdates >= 5)
                { return (greatestCoast, flightTicks); }
            }
            var presentation = Animation.Read();
            throw new InvalidOperationException($"synthetic {label} did not settle after touchdown: " +
                $"touchdown={touchdown}, speed={HorizontalSpeed(player):F4}, " +
                $"phase={player.Read().Rac1MovementPhase}, action={player.Read().Rac1TraversalAction}, " +
                $"sequence={CurrentPlayerAvatarSourceSequence()}, coast={greatestCoast:F4}, " +
                $"standingSample={presentation?.OrdinaryStandingSample}, " +
                $"semantic={presentation?.SemanticState}, frame={Animation.Read().Frame}");
        }

        async Task RequireStandingInterpolationAsync(string label)
        {
            var controller = Animation.Read();
            static bool HasNativeStandingOwner(DiagnosticMovementAnimation? current) =>
                current?.OrdinaryStandingSample is not null || current?.OrdinaryStopSample is { Sequence: 0 };
            // Native Heli landing action 3 retains its stop clock after
            // that clock enters sequence 0. Both owners sample the same native
            // standing pose on physics updates; their ownership is distinct.
            Require(HasNativeStandingOwner(controller) && controller!.Pose is not null,
                $"synthetic {label} did not retain the native sampled standing clock");
            var previous = controller!.Pose!;
            bool advanced = false;
            bool changedBetweenKeyframes = false;
            for (int tick = 0; tick < 60; tick++)
            {
                await PhysicsFramesAsync(1);
                await ProcessFrameAsync();
                Require(CurrentPlayerAvatarSourceSequence() == 0 && HasNativeStandingOwner(controller = Animation.Read()) &&
                    controller.Pose is not null && PlayerAvatarPresentationIsSynchronized(),
                    $"synthetic {label} lost synchronized source standing after landing");
                var current = controller.Pose!;
                bool changed = !current.LocalPositions.AsSpan().SequenceEqual(previous.LocalPositions.AsSpan());
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
