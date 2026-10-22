using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OneBigPackage;

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

    private async Task RunRac1MetropolisTaxiSmokeAsync()
    {
        double previousTimeScale = Engine.TimeScale;
        try
        {
            // The recovered 4.5 u/s² seek is intentionally much slower
            // than the old 60-waypoints/s host preview. Accelerate only the
            // staged smoke's game clock, not ordinary retail-route gameplay.
            Engine.TimeScale = 12;
            Require(_world is { Game: "rac1", LevelId: 3 } &&
                _rac1MetropolisTaxis is { } && _player is not null &&
                _rac1TaxiNodes.Count == 5 && _rac1TaxiCollisionBodies.Count == 5,
                "Metropolis taxi smoke needs all five retail imported taxi hulls");
            var taxis = _rac1MetropolisTaxis!;
            var player = _player!;
            Engine.MaxFps = 60;
            await PhysicsFramesAsync(5);

            var incoming = taxis.Stops.Single(s => s.InstanceIndex == 919);
            Require(incoming.ApproachTrigger is not null &&
                incoming.ApproachPath is { Count: 200 }, "missing native cuboid/approach for i919");
            // Stage a static hero inside retail cuboid 10 to exercise admission.
            player.ProcessMode = Node.ProcessModeEnum.Disabled;
            player.GlobalPosition = FromTaxiPoint(incoming.ApproachTrigger!.Center);
            await WaitForTaxiSmokeStateAsync(
                () => taxis.PhaseOf(919) == Rac1MetropolisTaxiSession.TrafficPhase.Docked,
                2400, "class-816 taxi failed to approach the retail docking endpoint");

            player.ProcessMode = Node.ProcessModeEnum.Inherit;
            player.ApplyRac1TaxiArrival(new RuntimeSpawn(
                incoming.Position.X, incoming.Position.Y, incoming.Position.Z,
                player.Rac1CurrentYaw));
            Require(TryHandleRac1MetropolisTaxiInput(new InputEventKey
            { Keycode = Key.T, Pressed = true }),
                "class-816 ordinary taxi input handler rejected a docked cab");
            Require(taxis.IsRiding &&
                _rac1TaxiCollisionBodies[919].CollisionLayer == 0 &&
                player.ProcessMode == Node.ProcessModeEnum.Disabled,
                "class-816 boarding did not attach player/disable moving hull collision");
            await WaitForTaxiSmokeStateAsync(
                () => !taxis.IsRiding && _rac1FlyingTaxi is null, 2400,
                "class-816 route failed to arrive");
            Require(player.ProcessMode != Node.ProcessModeEnum.Disabled &&
                _rac1TaxiCollisionBodies[919].CollisionLayer == 1 &&
                taxis.PhaseOf(919) == Rac1MetropolisTaxiSession.TrafficPhase.ParkedAtDestination,
                "class-816 route failed to dismount or restore physics");
            // Let the ordinary post-ride admission cooldown expire before
            // staging a separate taxi encounter.
            await PhysicsFramesAsync(90);

            // Class 1012's far endpoint has no supported landing. The bounded
            // host return leg is deliberate pending native camera/hero state.
            var shuttle = taxis.Stops.Single(s => s.InstanceIndex == 962);
            player.ApplyRac1TaxiArrival(new RuntimeSpawn(
                shuttle.Position.X, shuttle.Position.Y, shuttle.Position.Z,
                player.Rac1CurrentYaw));
            Require(TryHandleRac1MetropolisTaxiInput(new InputEventKey
            { Keycode = Key.T, Pressed = true }), "class-1012 boarding rejected");
            await PhysicsFramesAsync(25);
            Require(taxis.IsRiding, "class-1012 shuttle did not enter flight");
            AbortRac1MetropolisTaxiRide();
            Require(!taxis.IsRiding && _rac1FlyingTaxi is null &&
                player.ProcessMode != Node.ProcessModeEnum.Disabled &&
                _rac1TaxiCollisionBodies[962].CollisionLayer == 1,
                "mid-flight abort stranded Ratchet or left a disabled hull");

            player.ApplyRac1TaxiArrival(new RuntimeSpawn(
                shuttle.Position.X, shuttle.Position.Y, shuttle.Position.Z,
                player.Rac1CurrentYaw));
            Require(TryHandleRac1MetropolisTaxiInput(new InputEventKey
            { Keycode = Key.T, Pressed = true }),
                "class-1012 could not relaunch after flight interruption");
            await WaitForTaxiSmokeStateAsync(
                () => !taxis.IsRiding && _rac1FlyingTaxi is null, 3200,
                "class-1012 return route failed to arrive");
            Require(player.ProcessMode != Node.ProcessModeEnum.Disabled &&
                _rac1TaxiCollisionBodies[962].CollisionLayer == 1,
                "class-1012 return route failed to disembark and restore physics");

            GD.Print("[rac1-metropolis-taxi-smoke] PASS: retail i919 cuboid 10 -> " +
                "docked boarding -> outbound -> grounded dismount; " +
                "i962 boarding -> interrupted flight -> reset/reboard -> round trip");
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
