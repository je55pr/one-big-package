using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// Staged Metropolis host gate: retail imported routes and rendered hulls, the
/// normal in-world taxi input handler, flight tick, collision and dismount.
/// Placement into a taxi's trigger/boarding area is test staging, NOT ordinary
/// player traversal. Does not modify campaign progress or source data.
/// </summary>
public partial class OBPGame
{
    private async Task WaitForTaxiSmokeStateAsync(
        Func<bool> satisfied, int maximumFrames, string failure)
    {
        for (int frames = 0; frames < maximumFrames; frames += 5)
        {
            if (satisfied()) return;
            await PhysicsFramesAsync(5);
        }
        Require(satisfied(), failure);
    }

    private async Task RequireTaxiFacesMovingRouteAsync(Node3D taxiRoot,
        string context, int maximumFrames)
    {
        // Native seek can pause near tightly sampled waypoints and yaw
        // takes time to settle after departure or path reversal. Sample
        // the live 5-frame tangent until the cab demonstrably faces its
        // route, rather than requiring motion on a particular test frame.
        float best = -1f;
        for (int frames = 0; frames < maximumFrames; frames += 5)
        {
            Vector3 before = taxiRoot.GlobalPosition;
            await PhysicsFramesAsync(5);
            Vector3 travel = taxiRoot.GlobalPosition - before;
            travel.Y = 0;
            if (travel.LengthSquared() < .01f) continue;
            Vector3 nose = -taxiRoot.GlobalTransform.Basis.X;
            nose.Y = 0;
            float alignment = travel.Normalized().Dot(nose.Normalized());
            best = Math.Max(best, alignment);
            if (alignment > .7f)
            {
                GD.Print($"[rac1-taxi-smoke] {context} forward alignment={alignment:F3}");
                return;
            }
        }

        Require(false, $"{context}: taxi did not face its route; best alignment={best:F3}");
    }

    private async Task RunRac1MetropolisTaxiSmokeAsync()
    {
        double previousTimeScale = Engine.TimeScale;
        try
        {
            // The recovered 4.5 u/s� seek is intentionally much slower
            // than the old 60-waypoints/s host preview. Accelerate only the
            // staged smoke's game clock, not ordinary retail-route gameplay.
            Engine.TimeScale = 12;
            Require(_world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 3 } &&
                _rac1MetropolisTaxis is { } && _player is not null &&
                _rac1TaxiNodes.Count == 5 && _rac1TaxiCollisionBodies.Count == 5,
                "Metropolis taxi smoke needs all five retail imported taxi hulls");
            var taxis = _rac1MetropolisTaxis!;
            var player = _player!;
            Require(_rac1TaxiHovers.Count == 5 &&
                taxis.Stops.All(s => s.NativeHoverPhases.HasValue),
                "retail taxi hover oscillators and PVar phases not imported");
            Engine.MaxFps = 60;
            await PhysicsFramesAsync(5);

            // Exercise ordinary world input at the actual Kerwan spawn,
            // BEFORE any smoke-only taxi teleport or disabled player process.
            // This checks controller/contact movement and a real rejected
            // out-of-range taxi key, not just direct TryHandle calls.
            Engine.TimeScale = 1;
            Vector3 worldSpawn = player.GlobalPosition;
            Require(taxis.FindBoardable(ToTaxiPoint(worldSpawn)) is null &&
                string.IsNullOrEmpty(GetRac1MetropolisTaxiHudLine()),
                "ordinary Kerwan spawn falsely offers a taxi");
            Input.ParseInputEvent(new InputEventKey
            { Keycode = Key.T, PhysicalKeycode = Key.T, Pressed = true });
            Input.ParseInputEvent(new InputEventKey
            { Keycode = Key.T, PhysicalKeycode = Key.T, Pressed = false });
            await PhysicsFramesAsync(2);
            Require(!taxis.IsRiding && _rac1TaxiPendingStop is null,
                "ordinary out-of-range taxi input boarded from world spawn");
            try
            {
                Input.ActionPress(RawGamepadInput.MoveRight);
                await PhysicsFramesAsync(28);
            }
            finally
            {
                Input.ActionRelease(RawGamepadInput.MoveRight);
            }
            float ordinaryMotion = new Vector2(
                player.GlobalPosition.X - worldSpawn.X,
                player.GlobalPosition.Z - worldSpawn.Z).Length();
            Require(ordinaryMotion > 0.08f &&
                Math.Abs(player.GlobalPosition.Y - worldSpawn.Y) < 3f &&
                player.ProcessMode != Node.ProcessModeEnum.Disabled &&
                !taxis.IsRiding,
                $"ordinary grounded Kerwan traversal failed: moved {ordinaryMotion:F3}u");
            GD.Print($"[rac1-taxi-smoke] ordinary world spawn: " +
                $"raw input moved {ordinaryMotion:F3}u; out-of-range T rejected");
            Engine.TimeScale = 12;

            // Inspect the imported *permanent* ground under both real
            // class-1012 dock ends. The probe excludes mobile taxi hulls
            // but preserves i921's permanently parked landing platform.
            // This is a diagnostic of imported geometry, not an assertion
            // that the other native dock is currently playable.
            foreach (var shuttleStop in taxis.Stops.Where(s => s.NativeShuttleDock is not null))
            {
                var dock = shuttleStop.NativeShuttleDock!;
                Require(dock.OptionalCuboidSelector == -1,
                    "retail class-1012 accidentally enabled optional cuboid admission");
                foreach (var (side, waypoint) in new[]
                {
                    (Side: 0, Waypoint: dock.SideZeroPath[0]),
                    (Side: 1, Waypoint: dock.SideOnePath[0]),
                })
                {
                    Vector3 point = FromTaxiPoint(waypoint);
                    bool supported = TryResolveRac1TaxiLanding(player, point, out var grounded);
                    GD.Print($"[rac1-taxi-smoke] i{shuttleStop.InstanceIndex} native side {side} " +
                        $"permanent-ground={supported}, waypoint={point}, " +
                        $"ground={(supported ? grounded.ToString() : "none")}");
                }
            }

            var incoming = taxis.Stops.Single(s => s.InstanceIndex == 919);
            bool sourceGround = TryResolveRac1TaxiLanding(player,
                FromTaxiPoint(incoming.Position), out var sourceGroundPoint);
            GD.Print($"[rac1-taxi-smoke] i919 permanent source ground={sourceGround}, " +
                $"point={(sourceGround ? sourceGroundPoint.ToString() : "none")}; " +
                "native off-centre walk-in remains unproven");
            Require(incoming.ApproachTrigger is not null &&
                incoming.ApproachPath is { Count: 200 }, "missing native cuboid/approach for i919");
            // Stage a static hero inside retail cuboid 10 to exercise admission.
            player.ProcessMode = Node.ProcessModeEnum.Disabled;
            player.GlobalPosition = FromTaxiPoint(incoming.ApproachTrigger!.Center);
            await WaitForTaxiSmokeStateAsync(
                () => taxis.PhaseOf(919) == Rac1MetropolisTaxiSession.TrafficPhase.Docked,
                2400, "class-816 taxi failed to approach the retail docking endpoint");

            player.ProcessMode = Node.ProcessModeEnum.Inherit;
            // Precisely stage the retail dock for the 0x65 handoff.
            // ApplyRac1TaxiArrival's reusable restart adapter adds 3 host
            // collision-clearance units, so reverse only that host lift here.
            // This staging is NOT evidence of an off-centre walkable deck.
            player.ApplyRac1TaxiArrival(new RuntimeSpawn(
                incoming.Position.X,
                incoming.Position.Y - RuntimeSpawnSceneAdapter.GroundingLift,
                incoming.Position.Z, player.Rac1CurrentYaw));
            Require(ToTaxiPoint(player.GlobalPosition)
                .DistanceSquared(incoming.Position) < 0.001,
                "class-816 smoke source staging drifted off native boarding dock");
            Require(TryHandleRac1MetropolisTaxiInput(new InputEventKey
            { Keycode = Key.T, Pressed = true }),
                "class-816 ordinary taxi input handler rejected a docked cab");
            Require(_rac1TaxiPendingStop?.InstanceIndex == 919 &&
                _rac1TaxiHeroTarget?.NativeState == 0x65 &&
                player.Rac1TaxiWalking && !taxis.IsRiding,
                "class-816 boarded immediately instead of beginning native walk-to");
            AbortRac1MetropolisTaxiRide();
            Require(_rac1TaxiPendingStop is null && _rac1TaxiHeroTarget is null &&
                !player.Rac1TaxiWalking && !taxis.IsRiding &&
                player.ProcessMode != Node.ProcessModeEnum.Disabled,
                "interrupted walk-to left class-816 controls captured");
            Require(TryHandleRac1MetropolisTaxiInput(new InputEventKey
            { Keycode = Key.T, Pressed = true }),
                "class-816 could not re-request a cancelled source walk-to");
            await WaitForTaxiSmokeStateAsync(
                () => taxis.IsRiding, 240,
                "class-816 walk-to did not settle and release before departure");
            Require(_rac1TaxiPendingStop is null && !player.Rac1TaxiWalking &&
                _rac1TaxiCollisionBodies[919].CollisionLayer == 0 &&
                player.ProcessMode == Node.ProcessModeEnum.Disabled,
                "class-816 completed walk-to without attaching player safely");
            await RequireTaxiFacesMovingRouteAsync(
                _rac1FlyingTaxi!.Root, "i919 outbound", 90);
            await WaitForTaxiSmokeStateAsync(
                () => !taxis.IsRiding && _rac1FlyingTaxi is null, 2400,
                "class-816 route failed to arrive");
            Require(player.ProcessMode != Node.ProcessModeEnum.Disabled &&
                _rac1TaxiCollisionBodies[919].CollisionLayer == 1 &&
                taxis.PhaseOf(919) == Rac1MetropolisTaxiSession.TrafficPhase.ParkedAtDestination,
                "class-816 route failed to dismount or restore physics");
            Require(player.GlobalPosition.DistanceTo(
                FromTaxiPoint(incoming.OutboundPath[^1])) < 2f,
                "class-816 failed to land at its permanently parked i921 platform");
            // Genuine controller/contact movement after the supported i921
            // dismount, without setting any taxi position or suppressing physics.
            Engine.TimeScale = 1;
            Vector3 recoveredLanding = player.GlobalPosition;
            try
            {
                Input.ActionPress(RawGamepadInput.MoveRight);
                await PhysicsFramesAsync(18);
            }
            finally
            {
                Input.ActionRelease(RawGamepadInput.MoveRight);
            }
            float postRideMotion = new Vector2(
                player.GlobalPosition.X - recoveredLanding.X,
                player.GlobalPosition.Z - recoveredLanding.Z).Length();
            Require(postRideMotion > 0.05f &&
                player.ProcessMode != Node.ProcessModeEnum.Disabled &&
                !taxis.IsRiding && !_rac1TaxiCameraWorldOwned,
                $"ordinary movement not restored after supported landing: {postRideMotion:F3}u");
            GD.Print($"[rac1-taxi-smoke] i921 grounded dismount: " +
                $"ordinary player movement resumed ({postRideMotion:F3}u)");
            Engine.TimeScale = 12;
            // Let the ordinary post-ride admission cooldown expire before
            // staging a separate taxi encounter.
            await PhysicsFramesAsync(90);

            // Class 1012's far endpoint has no supported landing. The bounded
            // host return leg is deliberate pending native camera/hero state.
            var shuttle = taxis.Stops.Single(s => s.InstanceIndex == 962);
            player.ApplyRac1TaxiArrival(new RuntimeSpawn(
                shuttle.Position.X, shuttle.Position.Y, shuttle.Position.Z,
                player.Rac1CurrentYaw));
            Require(shuttle.NativeHudSelectors?.MessageForCurrentSide(1) == 0x0bcb &&
                GetRac1MetropolisTaxiHudLine() ==
                    Rac1RetailUiAssets.Text(0x0bcb, "Taxi: T / Triangle to ride"),
                "class-1012 retail HUD message is not selected from PVar+0x98 at source dock");
            // The native camera_request name masks HUD element slot 0x0c, not
            // a Godot camera-cut. The HUD serial stays unallocated
            // until a real HUD controller supplies its request serial.
            var nativeHudElement = shuttle.NativeHudElementLease?.FocusCommand();
            Require(nativeHudElement is
            {
                Kind: Rac1TaxiNativeHudElementLease.HudCommandKind.Request,
                SlotId: 0x0c,
                Key: 0,
                InitializeAddress: 0x00226960
            }, "class-1012 retail HUD element request contract was lost");
            // Exercise the ordinary taxi input handler deterministically.
            // Synthetic full-viewport input dispatch races Godot's unfocused
            // test window on unsupported 1012 boarding collision; actual
            // T/Triangle controller boarding was separately playtested.
            // Placement here remains staged, not proof of walkable geometry.
            Require(TryHandleRac1MetropolisTaxiInput(new InputEventKey
            { Keycode = Key.T, Pressed = true }),
                "class-1012 source boarding input rejected");
            // 1012's source dock is currently above unsupported ground.
            // It retains the safe direct-boarding host fallback until the
            // two-ended native landing/attachment geometry is recovered.
            Require(taxis.IsRiding && !player.Rac1TaxiWalking &&
                _rac1TaxiPendingStop is null,
                "class-1012 unsupported source dock attempted an unsafe walk");
            Require(player.VisualRoot.Visible,
                "class-1012 hidden passenger when boarded");
            Vector3 cameraRelativeAtBoard = player.Camera.GlobalPosition - player.GlobalPosition;
            bool recoveredCamera = player.Camera.TopLevel;
            await RequireTaxiFacesMovingRouteAsync(
                _rac1FlyingTaxi!.Root, "i962 outbound", 90);
            Require(taxis.IsRiding, "class-1012 shuttle did not enter flight");
            Require(_rac1TaxiRiderFrame is not null &&
                _rac1FlyingTaxi is not null,
                "class-1012 lost cab-relative passenger pose during flight");
            var carryingTaxi = _rac1FlyingTaxi!;
            var cabPose = carryingTaxi.Root.GlobalPosition;
            var cabBasis = ToTaxiCabBasis(carryingTaxi.Root.GlobalTransform.Basis);
            Require(cabBasis.IsOrthonormal(), "taxi flight lost its rotation basis");
            var expected = _rac1TaxiRiderFrame!.PositionAt(
                new Rac1TaxiPoint(cabPose.X, cabPose.Y, cabPose.Z), cabBasis);
            Require(player.GlobalPosition.DistanceTo(new Vector3(
                (float)expected.X, (float)expected.Y, (float)expected.Z)) < 0.05f,
                "Ratchet stopped following the cab's yaw/pitch/roll passenger frame");
            Require(!recoveredCamera ||
                (player.Camera.GlobalPosition - player.GlobalPosition)
                    .DistanceTo(cameraRelativeAtBoard) < 0.1f,
                "world-space recovered camera stayed at the boarding dock instead of following Ratchet");
            // Wait for the actual model transition to the homeward leg;
            // verify reverse-facing rather than guessing from frame count.
            await WaitForTaxiSmokeStateAsync(() => taxis.IsReturning, 240,
                "class-1012 never switched to its return route");
            await RequireTaxiFacesMovingRouteAsync(
                carryingTaxi.Root, "i962 return", 45);
            AbortRac1MetropolisTaxiRide();
            Require(!taxis.IsRiding && _rac1FlyingTaxi is null &&
                _rac1TaxiRiderFrame is null && !_rac1TaxiCameraWorldOwned &&
                player.ProcessMode != Node.ProcessModeEnum.Disabled &&
                _rac1TaxiCollisionBodies[962].CollisionLayer == 1,
                "mid-flight abort stranded Ratchet, camera or moving hull");

            player.ApplyRac1TaxiArrival(new RuntimeSpawn(
                shuttle.Position.X, shuttle.Position.Y, shuttle.Position.Z,
                player.Rac1CurrentYaw));
            Require(TryHandleRac1MetropolisTaxiInput(new InputEventKey
            { Keycode = Key.T, Pressed = true }),
                "class-1012 could not relaunch after flight interruption");
            Require(taxis.IsRiding && !player.Rac1TaxiWalking,
                "class-1012 relaunch abandoned the bounded source-dock fallback");
            await WaitForTaxiSmokeStateAsync(
                () => !taxis.IsRiding && _rac1FlyingTaxi is null, 3200,
                "class-1012 return route failed to arrive");
            Require(player.ProcessMode != Node.ProcessModeEnum.Disabled &&
                !_rac1TaxiCameraWorldOwned && _rac1TaxiRiderFrame is null &&
                _rac1TaxiCollisionBodies[962].CollisionLayer == 1,
                "class-1012 return route failed to disembark or restore camera/physics");

            GD.Print("[rac1-metropolis-taxi-smoke] PASS: retail i919 cuboid 10 -> " +
                "native walk-to -> abort -> retry -> outbound -> grounded dismount; " +
                "i962 safe direct board -> interrupted flight -> reset/reboard -> round trip; " +
                "ordinary world input, physical motion and post-ride control recovery");
            ApplicationLifecycle.RequestQuit(this, "rac1-metropolis-taxi-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[rac1-metropolis-taxi-smoke] FAIL: {ex}");
            AbortRac1MetropolisTaxiRide();
            ApplicationLifecycle.RequestQuit(this, "rac1-metropolis-taxi-smoke-fail", 3);
        }
        finally
        {
            Engine.TimeScale = previousTimeScale;
        }
    }
}
