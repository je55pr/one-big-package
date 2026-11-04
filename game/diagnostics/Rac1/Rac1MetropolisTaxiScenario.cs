using System.Numerics;
using OBP.Core;
using OBP.Fusion;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

/// <summary>Staged imported routes/hulls and ordinary input; native off-centre walk-in remains unproven.</summary>
public sealed class Rac1MetropolisTaxiScenario(MovementDiagnosticContext context, IRac1TaxiDiagnosticFixture taxi) : MovementScenarioSupport(context)
{
    private async Task WaitAsync(Func<bool> satisfied, int maximumFrames, string failure)
    {
        for (int frames = 0; frames < maximumFrames; frames += 5)
        {
            if (satisfied()) return;
            await PhysicsFramesAsync(5);
        }
        Require(satisfied(), failure);
    }
    private async Task FacesRouteAsync(ActorInstanceKey actor, string label, int maximumFrames)
    {
        float best = -1;
        for (int frames = 0; frames < maximumFrames; frames += 5)
        {
            Vector3 before = taxi.ReadCab(actor).Position; await PhysicsFramesAsync(5);
            var cab = taxi.ReadCab(actor); Vector3 travel = cab.Position - before; travel.Y = 0;
            if (travel.LengthSquared() < .01f) continue;
            Vector3 nose = cab.Nose; nose.Y = 0;
            float alignment = Vector3.Dot(Vector3.Normalize(travel), Vector3.Normalize(nose)); best = Math.Max(best, alignment);
            if (alignment > .7f) { Log($"[rac1-taxi-smoke] {label} forward alignment={alignment:F3}"); return; }
        }
        Require(false, $"{label}: taxi did not face its route; best alignment={best:F3}");
    }
    public async Task RunAsync()
    {
        using var presentation = taxi.PreservePresentation(); using var frameRate = World.LimitFrameRate(60);
        try
        {
            // Only the staged diagnostic clock is accelerated; native seek and ordinary gameplay are unchanged.
            taxi.SetTimeScale(12); var state = taxi.Read();
            Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 3 } && state.SessionPresent && state.Nodes == 5 && state.Hulls == 5,
                "Metropolis taxi smoke needs all five retail imported taxi hulls");
            var stops = taxi.ReadStops();
            Require(state.Hovers == 5 && stops.All(stop => stop.HoverPhases), "retail taxi hover oscillators and PVar phases not imported");
            await CheckOrdinarySpawnAsync();
            CheckDocks(stops);
            await RideIncomingAsync(stops.Single(stop => stop.Actor.Target.InstanceIndex == 919));
            await RideShuttleAsync(stops.Single(stop => stop.Actor.Target.InstanceIndex == 962));
            Log("[rac1-metropolis-taxi-smoke] PASS: retail i919 cuboid 10 -> native walk-to -> abort -> retry -> outbound -> grounded dismount; i962 safe direct board -> interrupted flight -> reset/reboard -> round trip; ordinary world input, physical motion and post-ride control recovery");
        }
        finally { ClearMovementSmokeInput(); }
    }
    private async Task CheckOrdinarySpawnAsync()
    {
        await PhysicsFramesAsync(5); taxi.SetTimeScale(1);
        Vector3 worldSpawn = Player.Read().GlobalPosition;
        Require(!taxi.Read().BoardableAtPlayer && string.IsNullOrEmpty(taxi.Read().HudLine), "ordinary Kerwan spawn falsely offers a taxi");
        Driver.SetKey(DiagnosticPhysicalKey.T, true); Driver.SetKey(DiagnosticPhysicalKey.T, false); await PhysicsFramesAsync(2);
        Require(!taxi.Read().Riding && taxi.Read().PendingStop is null, "ordinary out-of-range taxi input boarded from world spawn");
        Driver.SetAction(MovementDiagnosticDriver.Right, true); await PhysicsFramesAsync(28); Driver.SetAction(MovementDiagnosticDriver.Right, false);
        float ordinaryMotion = HorizontalDistance(Player.Read().GlobalPosition, worldSpawn);
        Require(ordinaryMotion > .08f && Math.Abs(Player.Read().GlobalPosition.Y - worldSpawn.Y) < 3 && !taxi.Read().ProcessDisabled && !taxi.Read().Riding,
            $"ordinary grounded Kerwan traversal failed: moved {ordinaryMotion:F3}u");
        Log($"[rac1-taxi-smoke] ordinary world spawn: raw input moved {ordinaryMotion:F3}u; out-of-range T rejected"); taxi.SetTimeScale(12);
    }

    private void CheckDocks(System.Collections.Immutable.ImmutableArray<Rac1TaxiStopDiagnostic> stops)
    {
        foreach (var stop in stops.Where(stop => stop.ShuttleDock))
        {
            Require(stop.OptionalCuboidSelector == -1, "retail class-1012 accidentally enabled optional cuboid admission");
            foreach (var dock in stop.DockEnds)
            {
                var landing = taxi.ProbeLanding(dock.Waypoint);
                Log($"[rac1-taxi-smoke] i{stop.Actor.Target.InstanceIndex} native side {dock.Side} permanent-ground={landing.Supported}, waypoint={dock.Waypoint}, ground={(landing.Supported ? landing.Position.ToString() : "none")}");
            }
        }
    }

    private async Task RideIncomingAsync(Rac1TaxiStopDiagnostic incoming)
    {
        var sourceGround = taxi.ProbeLanding(incoming.Position);
        Log($"[rac1-taxi-smoke] i919 permanent source ground={sourceGround.Supported}, point={(sourceGround.Supported ? sourceGround.Position.ToString() : "none")}; native off-centre walk-in remains unproven");
        Require(incoming.ApproachCentre is not null && incoming.ApproachPoints == 200, "missing native cuboid/approach for i919");
        taxi.SuspendAtApproach(incoming.Actor);
        await WaitAsync(() => taxi.ReadCab(incoming.Actor).Phase == Rac1MetropolisTaxiSession.TrafficPhase.Docked, 2400,
            "class-816 taxi failed to approach the retail docking endpoint");
        taxi.ResumeProcess(); taxi.StageAtSource(incoming.Actor, reverseGroundingLift: true);
        Require(taxi.SourceDistanceSquared(incoming.Actor) < .001, "class-816 smoke source staging drifted off native boarding dock");
        Require(taxi.RequestBoarding(), "class-816 ordinary taxi input handler rejected a docked cab"); var state = taxi.Read();
        Require(state.PendingStop == 919 && state.HeroTargetState == 0x65 && state.Walking && !state.Riding,
            "class-816 boarded immediately instead of beginning native walk-to");
        taxi.Abort(); state = taxi.Read();
        Require(state.PendingStop is null && state.HeroTargetState is null && !state.Walking && !state.Riding && !state.ProcessDisabled,
            "interrupted walk-to left class-816 controls captured");
        Require(taxi.RequestBoarding(), "class-816 could not re-request a cancelled source walk-to");
        await WaitAsync(() => taxi.Read().Riding, 240, "class-816 walk-to did not settle and release before departure"); state = taxi.Read();
        Require(state.PendingStop is null && !state.Walking && taxi.ReadCab(incoming.Actor).CollisionLayer == 0 && state.ProcessDisabled,
            "class-816 completed walk-to without attaching player safely");
        await FacesRouteAsync(incoming.Actor, "i919 outbound", 90);
        await WaitAsync(() => !taxi.Read().Riding && !taxi.Read().Flying, 2400, "class-816 route failed to arrive");
        Require(!taxi.Read().ProcessDisabled && taxi.ReadCab(incoming.Actor) is { CollisionLayer: 1, Phase: Rac1MetropolisTaxiSession.TrafficPhase.ParkedAtDestination },
            "class-816 route failed to dismount or restore physics");
        Require(incoming.OutboundEnd is { } endpoint && Vector3.Distance(Player.Read().GlobalPosition, endpoint) < 2, "class-816 failed to land at its permanently parked i921 platform");
        taxi.SetTimeScale(1); Vector3 recoveredLanding = Player.Read().GlobalPosition;
        Driver.SetAction(MovementDiagnosticDriver.Right, true); await PhysicsFramesAsync(18); Driver.SetAction(MovementDiagnosticDriver.Right, false);
        float postRideMotion = HorizontalDistance(Player.Read().GlobalPosition, recoveredLanding); state = taxi.Read();
        Require(postRideMotion > .05f && !state.ProcessDisabled && !state.Riding && !state.CameraWorldOwned,
            $"ordinary movement not restored after supported landing: {postRideMotion:F3}u");
        Log($"[rac1-taxi-smoke] i921 grounded dismount: ordinary player movement resumed ({postRideMotion:F3}u)"); taxi.SetTimeScale(12);
        await PhysicsFramesAsync(90);
    }

    private async Task RideShuttleAsync(Rac1TaxiStopDiagnostic shuttle)
    {
        // The unsupported far dock retains the existing bounded host return, without claiming native landing geometry.
        taxi.StageAtSource(shuttle.Actor, reverseGroundingLift: false);
        Require(shuttle.SourceHudMessage == 0x0bcb && taxi.Read().HudLine == taxi.ReadRetailText(0x0bcb),
            "class-1012 retail HUD message is not selected from PVar+0x98 at source dock");
        Require(shuttle.HudRequest is { Kind: Rac1TaxiNativeHudElementLease.HudCommandKind.Request, SlotId: 0x0c, Key: 0, InitializeAddress: 0x00226960 },
            "class-1012 retail HUD element request contract was lost");
        Require(taxi.RequestBoarding(), "class-1012 source boarding input rejected"); var state = taxi.Read();
        Require(state.Riding && !state.Walking && state.PendingStop is null, "class-1012 unsupported source dock attempted an unsafe walk");
        Require(state.VisualVisible, "class-1012 hidden passenger when boarded");
        Vector3 cameraRelativeAtBoard = state.CameraRelative; bool recoveredCamera = state.CameraTopLevel;
        await FacesRouteAsync(shuttle.Actor, "i962 outbound", 90); state = taxi.Read();
        Require(state.Riding, "class-1012 shuttle did not enter flight");
        Require(state.RiderFrame && state.Flying, "class-1012 lost cab-relative passenger pose during flight");
        Require(state.CabBasisOrthonormal, "taxi flight lost its rotation basis");
        Require(state.ExpectedPassengerPosition is { } expected && Vector3.Distance(Player.Read().GlobalPosition, expected) < .05f,
            "Ratchet stopped following the cab's yaw/pitch/roll passenger frame");
        Require(!recoveredCamera || Vector3.Distance(state.CameraRelative, cameraRelativeAtBoard) < .1f,
            "world-space recovered camera stayed at the boarding dock instead of following Ratchet");
        await WaitAsync(() => taxi.Read().Returning, 240, "class-1012 never switched to its return route");
        await FacesRouteAsync(shuttle.Actor, "i962 return", 45); taxi.Abort(); state = taxi.Read();
        Require(!state.Riding && !state.Flying && !state.RiderFrame && !state.CameraWorldOwned && !state.ProcessDisabled && taxi.ReadCab(shuttle.Actor).CollisionLayer == 1,
            "mid-flight abort stranded Ratchet, camera or moving hull");
        taxi.StageAtSource(shuttle.Actor, reverseGroundingLift: false);
        Require(taxi.RequestBoarding(), "class-1012 could not relaunch after flight interruption");
        Require(taxi.Read().Riding && !taxi.Read().Walking, "class-1012 relaunch abandoned the bounded source-dock fallback");
        await WaitAsync(() => !taxi.Read().Riding && !taxi.Read().Flying, 3200, "class-1012 return route failed to arrive"); state = taxi.Read();
        Require(!state.ProcessDisabled && !state.CameraWorldOwned && !state.RiderFrame && taxi.ReadCab(shuttle.Actor).CollisionLayer == 1,
            "class-1012 return route failed to disembark or restore camera/physics");
    }

}
