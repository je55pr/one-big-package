using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

/// <summary>Original staged Aridia contact/support/roaming witnesses over real source actors.</summary>
public sealed class Rac1AridiaScenario(MovementDiagnosticContext context, IRac1EnemyDiagnosticFacts enemies,
    IRac1EnemyDiagnosticActions actors, IRac1AridiaDiagnosticFixture aridia, IRac1EncounterLifecycle lifecycle)
    : Rac1EncounterScenarioSupport(context, enemies, actors)
{
    /// <summary>
    /// Real Godot fixture using the original Aridia #548 Constructorbot
    /// mesh, collision, authored placement and playable player actor.
    /// The scene's target is deliberately repositioned: this is a STAGED
    /// integration test, not proof of an unassisted retail encounter.
    /// </summary>
    public async Task RunAsync()
    {
        try
        {
            Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: Rac1AridiaEnemies.LevelId }, "Aridia Constructorbot smoke requires retail level 2");
            using var frameRate = World.LimitFrameRate(60);
            Driver.Own(Actors.PreservePlayerPose());
            Vector3 originalPlayerSpawn = Player.Read().GlobalPosition;
            ClearMovementSmokeInput();
            Require(Enemies.ReadPopulation().Enemies == 137, "Aridia hostile census changed");
            const int botId = 548; // retail mode0, state7, sequence0
            var bot = Enemy(botId);
            Driver.Own(Actors.PreserveActorPose(bot.Key));
            Require(bot.Key.Target.NativeClassId == Rac1AridiaEnemies.Constructorbot,
                "Smoke actor is not authored class612");
            var native = bot.Key;
            Require(aridia.ReadConstructorbot(native).NativeState == Rac1AridiaConstructorbotNative.RestState,
                "mode0 bot did not initialise to source state7");
            Require(aridia.TryReadAuthoredWaypoint(Enemy(botId).Key, out var waypoint),
                "mode0 bot has no original native waypoint");
            int native548 = aridia.ReadConstructorbot(native).WaypointRecord;
            Require(native548 == 22, "mode0 bot does not have retail waypoint record22");
            Require(waypoint.DistanceTo(FromEffectNative(aridia.ReadClipCentreNative(22))) < .001f,
                "mode0 bot waypoint is not original gameplay record22");

            await PhysicsFramesAsync(10);
            Vector3 position = bot.Read().BodyPosition;
            Vector3 forward = bot.Read().Forward;
            forward.Y = 0;
            Require(forward.LengthSquared() > .5f, "bot's authored facing is invalid");
            forward = forward.Normalized();

            // This is a controlled, near-waypoint input fixture. None of the
            // six Sandmouse-gated class580 actors is explicitly woken.
            Player.ResetAt(position + forward * 3 + Vector3.UnitY * .05f);
            await Rac1AridiaSmokeWaitNativeFramesAsync(() =>
                aridia.ReadConstructorbot(bot.Key).NativeState ==
                Rac1AridiaConstructorbotNative.JointContactState, 180,
                "source state7 to joint-contact state11");
            Require(bot.Read().AnimationSequence == 8 ||
                aridia.ReadConstructorbot(bot.Key).NativeState ==
                Rac1AridiaConstructorbotNative.ContactRecoveryState,
                "source state11 failed to select native sequence8");
            Require(aridia.ReadConstructorbot(Enemy(botId).Key).Health == 3,
                "source contact entry unexpectedly damaged the Constructorbot");

            Vector3 joint = bot.Read().Socket;
            Log($"[aridia-bot-smoke] staged contact probe joint={joint} " +
                $"player={Player.Read().GlobalPosition} forward={bot.Read().Forward} " +
                $"sequence={bot.Read().AnimationSequence} health={lifecycle.ReadNanotech().Nanotech}");
            int nanotechBeforeContact = lifecycle.ReadNanotech().Nanotech;
            bool contacted = false;
            for (int tick = 0; tick < 15 && !contacted; tick++)
            {
                // Staged collision probe uses a real player capsule next
                // to the ORIGINAL joint0 animation socket. Keep the actor
                // airborne for the fixture, so gravity doesn't move it
                // below this high-mounted source hitbox.
                Vector3 socket = bot.Read().Socket;
                Vector3 attackForward = bot.Read().Forward;
                Player.StageWater(socket +
                    attackForward * .45f - Vector3.UnitY * .3f);
                await PhysicsFramesAsync(1);
                contacted = lifecycle.ReadNanotech().Nanotech < nanotechBeforeContact;
            }
            Require(contacted, "native state11 joint ray did not damage staged player capsule");
            Log($"[aridia-bot-smoke] native state11 joint ray -> real Ratchet " +
                $"nanotech {nanotechBeforeContact}->{lifecycle.ReadNanotech().Nanotech} PASS");
            Log($"[aridia-bot-smoke] real class612 i{botId} state7->11 " +
                $"at authored spawn, original model/contact body and player staging PASS; " +
                $"nativeFrame={aridia.ReadConstructorbot(bot.Key).NativeState}");

            // PR #190 P2: the real host moves with MoveAndCollide, which
            // does NOT update CharacterBody3D.IsOnFloor(). Keep Ratchet in
            // range while the imported bot's attack clip reaches recovery,
            // support the actual body against imported level geometry, and
            // demand a normal native state11 -> 13 -> 7 cycle without
            // directly writing any of those native states.
            Player.ResetAt(bot.Read().BodyPosition +
                forward * 3 + Vector3.UnitY * .05f);
            Require(Actors.TryGround(bot.Key, upwardReach: 1, downwardReach: 3),
                "retail Constructorbot cannot find real terrain for state13 regression");
            Actors.StageRoot(bot.Key, bot.Read().BodyPosition);
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => aridia.ReadConstructorbot(native).NativeState ==
                    Rac1AridiaConstructorbotNative.ContactRecoveryState,
                400, "real class612 i548 clip8 completes state11->13");
            Require(!bot.Read().GodotFloor,
                "state13 regression no longer exercises the MoveAndCollide floor-flag gap");
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => aridia.ReadConstructorbot(native).NativeState ==
                    Rac1AridiaConstructorbotNative.RestState,
                16, "real floor-supported class612 i548 state13->7");
            Require(bot.Read().AnimationSequence == 0,
                "state13 terrain contact did not select original rest clip0");
            Log("[aridia-bot-smoke] real class612 i548 clip8 " +
                "state11->13->7 from Godot terrain support, without MoveAndSlide PASS");

            // Also prove the native STATE8 branch, not just the more
            // numerous state9 Constructorbots. #551 is the one retail
            // mode2 bot: activation from record14 must select source clip4.
            const int particleBotId = 551;
            var particleBot = Enemy(particleBotId);
            Driver.Own(Actors.PreserveActorPose(particleBot.Key));
            var particleNative = particleBot.Key;
            Require(aridia.ReadConstructorbot(particleNative).NativeState == 8,
                "retail mode2 Constructorbot activated before source proximity");
            Require(aridia.ReadConstructorbot(particleNative).WaypointRecord == 14,
                "retail mode2 Constructorbot does not use native record14");
            Player.StageWater(FromEffectNative(aridia.ReadClipCentreNative(14)));
            await Rac1AridiaSmokeWaitNativeFramesAsync(() => aridia.ReadConstructorbot(particleNative).NativeState == 2, 80,
                "native class612 instance551 state8->2 source distance trigger");
            Require(!Enemies.SupportsSequence(particleBot.Key, 4) || particleBot.Read().AnimationSequence == 4,
                "mode2 bot did not choose source entry clip4");
            Log("[aridia-bot-smoke] real class612 i551 native record14 " +
                "distance admission state8->2 clip4 PASS");
            Vector3 originalMotionPosition = particleBot.Read().Position;
            Vector3 actualWaypoint = FromEffectNative(aridia.ReadClipCentreNative(14));
            float originalPlanarDistance = new Vector2(
                originalMotionPosition.X - actualWaypoint.X,
                originalMotionPosition.Z - actualWaypoint.Z).Length();
            await Rac1AridiaSmokeWaitNativeFramesAsync(() => aridia.ReadConstructorbot(particleNative).NativeState is >= 3 and <= 5,
                280, "source state2 clip4 completion and state3 horizontal waypoint motion");
            Vector3 movedPosition = particleBot.Read().Position;
            float movedPlanarDistance = new Vector2(
                movedPosition.X - actualWaypoint.X,
                movedPosition.Z - actualWaypoint.Z).Length();
            Require(movedPlanarDistance < originalPlanarDistance - .015f,
                $"Constructorbot state3 did not move toward native waypoint14: " +
                $"before={originalPlanarDistance:F3} after={movedPlanarDistance:F3}");
            Log($"[aridia-bot-smoke] class612 i551 native movement toward record14 " +
                $"before={originalPlanarDistance:F3} after={movedPlanarDistance:F3} " +
                $"state={aridia.ReadConstructorbot(particleNative).NativeState} PASS");

            // This fixture still stages the PLAYER near authored record14,
            // but no longer forces the Constructorbot to native state5 or
            // manually moves its body onto terrain. Source state3 must now
            // descend to real imported collision geometry and state4 must
            // complete its animation before the walker is admitted.
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => aridia.ReadConstructorbot(particleNative).NativeState == 5, 280,
                "native class612 state3 descent -> state4 clip5 -> state5",
                () =>
                {
                    Vector3 current = particleBot.Read().Position;
                    return $"native={aridia.ReadConstructorbot(particleNative).NativeState} " +
                        $"clip={particleBot.Read().AnimationSequence} " +
                        $"position={current} waypoint={actualWaypoint} " +
                        $"floor={aridia.ProbeStateThreeFloor(particleBot.Key, current)} " +
                        $"verticalStep={aridia.ReadConstructorbot(Enemy(particleBotId).Key).VerticalStep} " +
                        $"player={Player.Read().GlobalPosition}";
                });
            Require(particleBot.Read().AnimationSequence == 6 ||
                !Enemies.SupportsSequence(particleBot.Key, 6),
                "native state5 did not select source clip6");
            Log($"[aridia-bot-smoke] real class612 i551 " +
                $"state3 floor descent ->state4 clip5 ->state5 clip6 " +
                $"bodyY={particleBot.Read().BodyPosition.Y:F3} PASS");
            float walkerStart = new Vector2(
                particleBot.Read().Position.X - actualWaypoint.X,
                particleBot.Read().Position.Z - actualWaypoint.Z).Length();
            await PhysicsFramesAsync(36);
            float walkerEnd = new Vector2(
                particleBot.Read().Position.X - actualWaypoint.X,
                particleBot.Read().Position.Z - actualWaypoint.Z).Length();
            if (walkerStart > .1f)
                Require(walkerEnd < walkerStart - .025f,
                    $"state5 source walker failed to approach native waypoint: " +
                    $"before={walkerStart:F3} after={walkerEnd:F3}");
            else
                Require(walkerEnd < .25f,
                    $"state5 source walker drifted off a waypoint already reached " +
                    $"by native state3: after={walkerEnd:F3}");
            Log($"[aridia-bot-smoke] class612 i551 natural state5 " +
                $"walker waypoint before={walkerStart:F3} after={walkerEnd:F3} " +
                $"state={aridia.ReadConstructorbot(particleNative).NativeState} PASS");

            // Re-stage the SAME retail body one unit toward its authored
            // arrival position along actual level geometry. Unlike a
            // waypoint-at-zero check, this gives state5 a nonzero travel
            // vector and exercises its first AND ahead-of-actor probes.
            Vector3 back = originalMotionPosition - actualWaypoint;
            back.Y = 0;
            Require(back.LengthSquared() > .25f, "missing authored arrival direction");
            Vector3 offWaypoint = actualWaypoint + back.Normalized() * 1.1f;
            Actors.StageBody(particleBot.Key, offWaypoint);
            Actors.StageRoot(particleBot.Key, offWaypoint);
            Require(Actors.TryGround(particleBot.Key, upwardReach: .5f, downwardReach: 40f),
                "cannot stage walker lookahead onto imported ground");
            Actors.StageRoot(particleBot.Key, particleBot.Read().BodyPosition);
            aridia.StageNativeState(particleNative, 5);
            float walkedStart = new Vector2(
                particleBot.Read().BodyPosition.X - actualWaypoint.X,
                particleBot.Read().BodyPosition.Z - actualWaypoint.Z).Length();
            await PhysicsFramesAsync(85);
            float walkedEnd = new Vector2(
                particleBot.Read().BodyPosition.X - actualWaypoint.X,
                particleBot.Read().BodyPosition.Z - actualWaypoint.Z).Length();
            Require(walkedEnd < walkedStart - .025f,
                $"source walker with lookahead did not move across real terrain: " +
                $"before={walkedStart:F3} after={walkedEnd:F3}");
            int nativeLiftsApplied =
                aridia.ReadConstructorbot(Enemy(particleBotId).Key).LiftCount;
            Require(nativeLiftsApplied > 0,
                "original class612 walker never applied source 30%-floor lift " +
                "during the real waypoint traversal");
            Log($"[aridia-bot-smoke] class612 i551 staged state5 " +
                $"first+lookahead ground probes before={walkedStart:F3} " +
                $"after={walkedEnd:F3} source-lift-applied={nativeLiftsApplied} PASS");

            // Deliberate native swept-segment branch fixture: source
            // walker_move_v skips its segment for <=0.625 distance.
            // Exercise a real >0.625 Godot scene ray with the original
            // class612 body, without fabricating a collision hit.
            Vector3 beforeSweepBody = particleBot.Read().BodyPosition;
            int priorSweeps = aridia.ReadConstructorbot(Enemy(particleBotId).Key).SweepCount;
            aridia.MoveWalkerContact(particleBot.Key, back.Normalized() * .8f);
            Require(aridia.ReadConstructorbot(Enemy(particleBotId).Key).SweepCount ==
                    priorSweeps + 1,
                "retail class612 state5 sweep did not use original 0x280 radius gate");
            Actors.StageBody(particleBot.Key, beforeSweepBody);
            Actors.StageRoot(particleBot.Key, beforeSweepBody);
            Log("[aridia-bot-smoke] real class612 i551 native radius0.625 " +
                "swept-segment Godot ray branch PASS");

            // Probe a real overlap on the imported floor without moving
            // the retail enemy itself: lower the tentative Moby origin so
            // its +1-raised source sphere centre intersects level terrain.
            // The returned correction must come from an actual Godot
            // collide_shape contact pair, not invented upward grounding.
            var sphereStarting = beforeSweepBody - Vector3.UnitY * .8f;
            var sphereResult = aridia.ProbeSpherePushouts(particleBot.Key, sphereStarting);
            Require(sphereResult.Contacts > 0,
                "retail class612 host sphere query did not observe real terrain overlap");
            Require(FromEffectNative(sphereResult.Position).Y > sphereStarting.Y + .01f,
                "source six-attempt sphere host adapter failed to push out of real ground");
            Require(sphereResult.Contacts <=
                    Rac1AridiaConstructorbotNative.WalkerSpherePushoutAttempts,
                "source walker sphere pushout exceeded six attempts");
            Log($"[aridia-bot-smoke] class612 i551 source six-attempt " +
                $"sphere overlap contacts={sphereResult.Contacts} " +
                $"lift={FromEffectNative(sphereResult.Position).Y - sphereStarting.Y:F3} PASS");

            // Real imported terrain must actually intersect the DIFFERENT
            // post-motion walker_move_v ray at position+1 to position-0.05.
            // Deliberately LOWER only its candidate, not the real enemy,
            // to expose a nonzero 30%-lift example on original scene data.
            Vector3 partialLiftCandidate = beforeSweepBody - Vector3.UnitY * .4f;
            float? observedLift = aridia.ProbeGroundLift(particleBot.Key, partialLiftCandidate);
            Require(observedLift is > .01f && observedLift < .3f,
                $"class612 original post-move floor segment did not observe " +
                $"a bounded partial lift (lift={observedLift})");
            float? unsupportedLift = aridia.ProbeGroundLift(particleBot.Key, beforeSweepBody + Vector3.UnitY * 30f);
            Require(!unsupportedLift.HasValue,
                "original walker post-move segment invented airborne floor contact");
            Require(particleBot.Read().BodyPosition.DistanceTo(beforeSweepBody) < .001f,
                "non-mutating source floor ray unexpectedly relocated class612 body");
            Log($"[aridia-bot-smoke] class612 i551 real source vertical " +
                $"segment positive 30%-lift={observedLift.GetValueOrDefault():F3} " +
                $"airborne miss PASS");

            // Negative scene fixture: same real collision body, but suspended
            // well above any terrain. Native walker_probe must return no
            // support; the host may turn, but cannot move or finish state5.
            Vector3 supportedLocation = particleBot.Read().BodyPosition;
            Vector3 airborneLocation = supportedLocation + Vector3.UnitY * 30f;
            Actors.StageBody(particleBot.Key, airborneLocation);
            Actors.StageRoot(particleBot.Key, airborneLocation);
            aridia.StageNativeState(particleNative, 5);
            await PhysicsFramesAsync(8);
            Require(particleBot.Read().BodyPosition.DistanceTo(airborneLocation) < .002f,
                "Constructorbot walker moved through unsupported air");
            Require(aridia.ReadConstructorbot(particleNative).NativeState == 5,
                "Constructorbot walker falsely completed while floor probe was missing");
            Log("[aridia-bot-smoke] real class612 i551 missing-floor " +
                "walker blocked without phantom state7 PASS");
            Actors.StageBody(particleBot.Key, supportedLocation);
            Actors.StageRoot(particleBot.Key, supportedLocation);

            // Third fixture: mode3 class612 #549 has PVar+0x18c=-1;
            // native L2:0x2d7fac checks its PVar+0x188 record12's centre
            // and PVar+0x184 full-XYZ radius20, NOT an inferred spawn.
            const int distanceBotId = 549;
            var distanceBot = Enemy(distanceBotId);
            Driver.Own(Actors.PreserveActorPose(distanceBot.Key));
            var distanceNative = distanceBot.Key;
            Require(aridia.ReadConstructorbot(distanceNative).ActivationVolume == -1,
                "source proximity bot incorrectly uses a clip admission volume");
            int distanceNode = aridia.ReadConstructorbot(distanceNative).WaypointRecord;
            Require(distanceNode == 12, "proximity bot's source node was not record12");
            Require(aridia.ReadConstructorbot(distanceNative).NativeState == 9,
                "proximity bot was woken before entering its authored radius");
            Player.StageWater(
                FromEffectNative(aridia.ReadClipCentreNative(distanceNode)));
            await Rac1AridiaSmokeWaitNativeFramesAsync(() =>
                aridia.ReadConstructorbot(distanceNative).NativeState == 2, 80,
                "source class612 instance549 waypoint record12 range admission state9->2");
            Log("[aridia-bot-smoke] real class612 i549 native record12 " +
                "distance admission state9->2 PASS");

            // A second retail actor, #558, starts in native state9 and
            // uniquely references authentic clip volume 21 at PVar+0x18c.
            // No fabricated route-node fallback is used for this actor.
            const int volumeBotId = 558;
            var volumeBot = Enemy(volumeBotId);
            Driver.Own(Actors.PreserveActorPose(volumeBot.Key));
            var sourceVolume = aridia.ReadClipCentreNative(21);
            Require(aridia.ReadConstructorbot(volumeBot.Key).ActivationVolume == 21,
                "volume bot does not reference native volume21");
            Require(aridia.ReadConstructorbot(volumeBot.Key).NativeState == 9,
                "volume Constructorbot was activated before Ratchet entered its volume");
            Require(!aridia.ClipContainsNative(21, ToEffectNative(Player.Read().GlobalPosition)),
                "initial contact fixture accidentally overlapped the volume");
            Player.StageWater(FromEffectNative(sourceVolume));
            await Rac1AridiaSmokeWaitNativeFramesAsync(() =>
                aridia.ReadConstructorbot(volumeBot.Key).NativeState == 2, 80,
                "retail class612 instance558 clip-volume21 activation state9->2");
            Require(!Enemies.SupportsSequence(volumeBot.Key, 16) || volumeBot.Read().AnimationSequence == 16,
                "source clip16 was not selected after volume activation");
            Log($"[aridia-bot-smoke] real class612 i558 clip-volume21 " +
                $"state9->2 source clip={volumeBot.Read().AnimationSequence} admission PASS");
            // Original state7 creates a return destination only after
            // source clip0 completes. Use a retail actor and the saved
            // original player spawn to keep Ratchet outside this bot's
            // source engagement radius. The enemy itself is NOT given
            // a fabricated return point.
            float roamingEngagementRadius =
                aridia.ReadConstructorbot(particleNative).EngagementRadius;
            Vector3 outward = originalPlayerSpawn - actualWaypoint;
            outward.Y = 0;
            Require(outward.LengthSquared() > 1f,
                "initial player spawn must distinguish the source waypoint");
            // Keep the player OUTSIDE native combat admission but INSIDE
            // Aridia's intentional 24-unit animation/AI update radius.
            // Moving all the way to spawn freezes the retail clip0 clock.
            Require(roamingEngagementRadius + 2f < aridia.HostUpdateRadius - 1f,
                "there is no safe source roam fixture within the host update radius");
            Player.StageWater(
                actualWaypoint + outward.Normalized() * (roamingEngagementRadius + 2f));
            Require(Player.Read().GlobalPosition.DistanceTo(actualWaypoint) >
                    roamingEngagementRadius,
                "staged player must remain beyond native engagement radius");
            aridia.StageNativeState(particleNative,
                Rac1AridiaConstructorbotNative.RestState);
            aridia.SelectSequence(particleBot.Key, 0, 1);
            aridia.ClearRoamingTarget(Enemy(particleBotId).Key);
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => aridia.HasRoamingTarget(Enemy(particleBotId).Key),
                240, "source state7 completed idle animation selects dynamic state6 target");
            Require(aridia.TryReadRoamingTarget(Enemy(particleBotId).Key, out var roam),
                "retail Constructorbot did not store its dynamic roaming target");
            Require(MathF.Abs(roam.X - actualWaypoint.X) <= 2.001f &&
                    MathF.Abs(roam.Z - actualWaypoint.Z) <= 2.001f &&
                    MathF.Abs(roam.Y - actualWaypoint.Y) < .001f,
                "native state6 destination is not anchored to original waypoint record14");
            Log($"[aridia-bot-smoke] original class612 i551 " +
                $"state7 clip0->state6 roaming target={roam} anchor={actualWaypoint} PASS");
            Vector3 originalRoamPosition = particleBot.Read().BodyPosition;
            float roamDistanceBefore = new Vector2(
                originalRoamPosition.X - roam.X, originalRoamPosition.Z - roam.Z).Length();
            await PhysicsFramesAsync(45);
            float roamDistanceAfter = new Vector2(
                particleBot.Read().BodyPosition.X - roam.X,
                particleBot.Read().BodyPosition.Z - roam.Z).Length();
            Require(roamDistanceAfter < roamDistanceBefore - .005f,
                $"native state6 did not move on actual terrain toward its dynamic target: " +
                $"before={roamDistanceBefore:F3} after={roamDistanceAfter:F3}");
            Require(aridia.ReadConstructorbot(particleNative).NativeState ==
                    Rac1AridiaConstructorbotNative.RoamingState,
                "state6 changed state despite clear walker return bits and distant player");
            Log($"[aridia-bot-smoke] class612 i551 45-frame roam " +
                $"distance={roamDistanceBefore:F3}->{roamDistanceAfter:F3} " +
                $"state={aridia.ReadConstructorbot(particleNative).NativeState} PASS");

            // Return Ratchet to the ORIGINAL record14 engagement range.
            // A clear state6 walker result must admit state5 rather than
            // reselect state7, and the old random target must be released.
            Player.StageWater(actualWaypoint);
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => aridia.ReadConstructorbot(particleNative).NativeState == 5, 100,
                "native state6 engages Ratchet and returns to source state5");
            Require(!aridia.HasRoamingTarget(Enemy(particleBotId).Key),
                "state6->5 retained an obsolete dynamic roaming destination");
            Require(particleBot.Read().AnimationSequence == 6 ||
                    !Enemies.SupportsSequence(particleBot.Key, 6),
                "source state6->5 transition did not preserve walker clip6");
            Log("[aridia-bot-smoke] class612 i551 roaming state6->5 " +
                "player-near-anchor and target cleanup PASS");

            // Move Ratchet back outside the original engagement radius,
            // still within the 24-unit host update radius. The bot must
            // finish approaching its actual waypoint and resume idle,
            // then generate another independent native-style target.
            Player.StageWater(
                actualWaypoint + outward.Normalized() * (roamingEngagementRadius + 2f));
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => aridia.ReadConstructorbot(particleNative).NativeState ==
                    Rac1AridiaConstructorbotNative.RestState, 260,
                "native state5 reaches planar waypoint and returns to state7");
            float planarReturnDistance = new Vector2(
                particleBot.Read().BodyPosition.X - actualWaypoint.X,
                particleBot.Read().BodyPosition.Z - actualWaypoint.Z).Length();
            Require(planarReturnDistance < Rac1AridiaConstructorbotNative.WalkerStopDistance,
                "native state5 returned before reaching original waypoint in XY");
            Require(!aridia.HasRoamingTarget(Enemy(particleBotId).Key),
                "return to state7 retained the previous roaming target");
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => aridia.ReadConstructorbot(particleNative).NativeState ==
                    Rac1AridiaConstructorbotNative.RoamingState &&
                    aridia.HasRoamingTarget(Enemy(particleBotId).Key),
                260, "state7 idle completion starts another state6 roaming cycle");
            Require(aridia.TryReadRoamingTarget(Enemy(particleBotId).Key, out var nextRoam),
                "restarted roaming cycle has no dynamic target");
            Require(MathF.Abs(nextRoam.X - actualWaypoint.X) <= 2.001f &&
                    MathF.Abs(nextRoam.Z - actualWaypoint.Z) <= 2.001f &&
                    MathF.Abs(nextRoam.Y - actualWaypoint.Y) < .001f,
                "restarted roaming target was not sourced from the original waypoint");
            Log("[aridia-bot-smoke] class612 i551 state6->5->7->6 " +
                "repeat roaming cycle PASS");

            // Let the newly generated random patrol reach a NATIVE solver
            // completion on real imported ground. Unlike the earlier staged
            // Ratchet engagement, this leg never sets the enemy's state,
            // body, or roaming target after state6 admission.
            float secondLegStart = new Vector2(
                particleBot.Read().BodyPosition.X - nextRoam.X,
                particleBot.Read().BodyPosition.Z - nextRoam.Z).Length();
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => aridia.ReadConstructorbot(particleNative).NativeState ==
                    Rac1AridiaConstructorbotNative.RestState, 540,
                "native state6 naturally returns to state7 from arrival/obstruction");
            Require(aridia.TryReadRoamExitFlags(Enemy(particleBotId).Key, out int exitFlags),
                "natural state6->7 lacked a recorded source walker result");
            Require((exitFlags & Rac1AridiaConstructorbotNative.WalkerHeadingOrContactMask) != 0,
                $"state6->7 admitted with unsupported walker flags {exitFlags:X2}");
            Require(!aridia.HasRoamingTarget(Enemy(particleBotId).Key),
                "natural state6->7 retained its dynamic destination");
            float secondLegEnd = new Vector2(
                particleBot.Read().BodyPosition.X - nextRoam.X,
                particleBot.Read().BodyPosition.Z - nextRoam.Z).Length();
            if ((exitFlags & Rac1AridiaConstructorbotNative.WalkerNearTargetFlag) != 0)
                Require(secondLegEnd < Rac1AridiaConstructorbotNative.WalkerStopDistance,
                    $"state6 natural arrival bit4 did not reach the patrol destination: " +
                    $"distance={secondLegEnd:F3}");
            else
                Require((exitFlags & Rac1AridiaConstructorbotNative.WalkerBlockedFlag) != 0,
                    "state6 natural departure without arrival was not a collision/ledge block");
            Log($"[aridia-bot-smoke] class612 i551 natural state6->7 " +
                $"distance={secondLegStart:F3}->{secondLegEnd:F3} " +
                $"flags=0x{exitFlags:X2} PASS");

            // Separate real-geometry unsupported-floor branch. Allow state7
            // to create its next source-random destination, then stage this
            // same retail body's origin 8 units above the actual ground.
            // The host must observe a genuine missing-floor probe, which is
            // walker 0x22 and hence state6->7, not forge proximity arrival.
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => aridia.ReadConstructorbot(particleNative).NativeState ==
                    Rac1AridiaConstructorbotNative.RoamingState &&
                    aridia.HasRoamingTarget(Enemy(particleBotId).Key),
                260, "next native state7 animation creates blocked-case roaming target");
            Require(!aridia.HasRoamExitFlags(Enemy(particleBotId).Key),
                "new roaming destination did not clear the previous departure flags");
            Vector3 supportedBody = particleBot.Read().BodyPosition;
            Vector3 unsupportedBody = supportedBody + Vector3.UnitY * 8f;
            Require(unsupportedBody.DistanceTo(Player.Read().GlobalPosition) <
                    aridia.HostUpdateRadius - 1f,
                "blocked roaming fixture would freeze source updates");
            Actors.StageBody(particleBot.Key, unsupportedBody);
            Actors.StageRoot(particleBot.Key, unsupportedBody);
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => aridia.ReadConstructorbot(particleNative).NativeState ==
                    Rac1AridiaConstructorbotNative.RestState,
                16, "state6 sees missing imported floor and returns to state7");
            Require(particleBot.Read().BodyPosition.DistanceTo(unsupportedBody) < .002f,
                "blocked state6 moved the real body through unsupported air");
            Require(aridia.TryReadRoamExitFlags(Enemy(particleBotId).Key, out int blockedFlags) &&
                    (blockedFlags & Rac1AridiaConstructorbotNative.WalkerBlockedFlag) != 0,
                "missing-floor state6->7 did not report native walker bit2");
            Require((blockedFlags & Rac1AridiaConstructorbotNative.WalkerNearTargetFlag) == 0,
                "unsupported-air branch incorrectly claimed source arrival bit4");
            Require(!aridia.HasRoamingTarget(Enemy(particleBotId).Key),
                "missing-floor state6->7 retained its obsolete roaming target");
            Actors.StageBody(particleBot.Key, supportedBody);
            Actors.StageRoot(particleBot.Key, supportedBody);
            Log($"[aridia-bot-smoke] class612 i551 unsupported-floor " +
                $"state6->7 flags=0x{blockedFlags:X2} target cleanup PASS");

            await VerifyAridiaConstructorbotEdgeSupportAsync(particleBot);
            VerifyAridiaSharkTwoCycleAuthoredHome();

        }
        catch (Exception ex)
        {
            LogError($"[aridia-bot-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }


    /// <summary>
    /// PR #202/#204 real-Godot regression. A deliberately isolated platform
    /// tests the imported Constructorbot's actual capsule at a ray-missed
    /// edge. Compare the normal-ray and BodyTestMotion fallback elevations
    /// for BOTH walker support and state3 descent. The platform is synthetic;
    /// it tests Godot contact reporting, not retail terrain equivalence.
    /// </summary>
    private async Task VerifyAridiaConstructorbotEdgeSupportAsync(Rac1DiagnosticEnemy bot)
    {
        var centre = bot.Read().BodyPosition + Vector3.UnitY * 120f;
        var fixture = Driver.Own(Geometry.Create("AridiaProbeEdgeFixture", Vector3.Zero));
        var platform = Driver.Own(fixture.AddBox("AridiaProbeEdgePlatform", new(1.2f, .4f, 4f), centre - Vector3.UnitY * .2f, 0));
        try
        {
            await PhysicsFramesAsync(3);
            // The centre ray strikes the 1.2-unit platform. Its edge is
            // x+0.6; the query x+0.68 is beyond it, but the original
            // Constructorbot's hosted capsule still overlaps the top edge.
            var edge = centre + Vector3.UnitX * .68f;
            var missedHit = Actors.Ray(edge + Vector3.UnitY * .5f, edge - Vector3.UnitY * .5f, [bot.Key], true);
            Require(missedHit is null,
                "edge-support fixture centre ray did not miss the platform");

            Require(aridia.ProbeSupport(bot.Key, centre, out float centreFloor,
                    out float centreNormal) && centreNormal > .65f,
                "real Godot centre-ray walker support unavailable");
            Require(aridia.ProbeSupport(bot.Key, edge, out float edgeFloor,
                    out float edgeNormal) && edgeNormal > .65f,
                "real Godot ray-missed capsule walker support unavailable");
            Require(MathF.Abs(centreFloor - centre.Y) < .06f &&
                    MathF.Abs(edgeFloor - centreFloor) < .06f,
                $"walker sweep reported body travel instead of contact height: " +
                $"ray={centreFloor:F3}, edge={edgeFloor:F3}, ground={centre.Y:F3}");
            Require(!Rac1AridiaConstructorbotNative.WalkerFloorHeightRejectsStep(
                    edge.Y, edgeFloor,
                    Rac1AridiaConstructorbotNative.WalkerProbeDown,
                    Rac1AridiaConstructorbotNative.WalkerProbeUp),
                "walker edge contact produced a false source 0x22 obstruction");

            float? centreDescent = aridia.ProbeStateThreeFloor(bot.Key, centre + Vector3.UnitY * 2f);
            float? edgeDescent = aridia.ProbeStateThreeFloor(bot.Key, edge + Vector3.UnitY * 2f);
            Require(centreDescent.HasValue && edgeDescent.HasValue &&
                    MathF.Abs(centreDescent.Value - centre.Y) < .06f &&
                    MathF.Abs(edgeDescent.Value - centreDescent.Value) < .06f,
                $"state3 descent sweep reported elevated capsule origin: " +
                $"ray={centreDescent}, edge={edgeDescent}, ground={centre.Y:F3}");
            Log("[aridia-bot-smoke] class612 real Godot capsule " +
                $"ray-missed edge walker and state3 floor heights " +
                $"ray={centreFloor:F3} edge={edgeFloor:F3} descent={edgeDescent:F3} PASS");

            // PR #213 review P2: the two primary terrain rays must ignore
            // other LIVE hostile bodies, just as both fallback sweeps do.
            // Reposition a second original Constructorbot capsule, not a
            // decorative Area3D, above this verified real Godot platform.
            var other = Enemy(548);
            Require(!(other.Key == bot.Key) &&
                    other.Read().CollisionLayer != 0,
                "other-hostile terrain fixture requires a live enemy body");
            var otherPose = Driver.Own(Actors.PreserveActorPose(other.Key));
            try
            {
                // The platform top is centre.Y. The other bot's 1.7-unit
                // capsule top is ~centre.Y+.38, below the walker ray start
                // +.5 and above the genuine static floor.
                Actors.StageBody(other.Key, centre - Vector3.UnitY * 1.32f);
                Actors.StageRoot(other.Key, other.Read().BodyPosition);
                await PhysicsFramesAsync(2);
                var legacyHit = Actors.Ray(centre + Vector3.UnitY * .5f, centre - Vector3.UnitY * .5f, [bot.Key], true);
                Vector3 naiveContact = legacyHit?.Position ?? Vector3.Zero;
                Require(legacyHit?.Actor == other.Key &&
                        naiveContact.Y > centre.Y + .2f,
                    "other-hostile fixture did not reproduce an elevated enemy-capsule ray hit");

                Require(aridia.ProbeSupport(bot.Key, centre,
                        out float actorFilteredFloor, out float actorFilteredNormal) &&
                        actorFilteredNormal > .65f &&
                        MathF.Abs(actorFilteredFloor - centre.Y) < .06f,
                    $"walker terrain ray treated another enemy capsule as ground: " +
                    $"ground={centre.Y:F3}, observed={actorFilteredFloor:F3}");
                float? actorFilteredDescent = aridia.ProbeStateThreeFloor(bot.Key, centre + Vector3.UnitY * 2f);
                Require(actorFilteredDescent.HasValue &&
                        MathF.Abs(actorFilteredDescent.Value - centre.Y) < .06f,
                    $"state3 terrain ray treated another enemy capsule as ground: " +
                    $"ground={centre.Y:F3}, observed={actorFilteredDescent}");
                Log("[aridia-bot-smoke] class612 state3/walker ignore " +
                    $"other live hostile capsule: naiveY={naiveContact.Y:F3} " +
                    $"terrainY={actorFilteredFloor:F3} PASS");
            }
            finally
            {
                using var cleanup = BeginCleanup();
                CleanupStep(otherPose.Dispose);
                await CleanupPhysicsAsync(2);
            }
        }
        finally
        {
            using var cleanup = BeginCleanup();
            CleanupStep(platform.Dispose);
            await CleanupPhysicsAsync(2);
        }
    }

    private void VerifyAridiaSharkTwoCycleAuthoredHome()
    {
        var trial = Driver.Own(aridia.BeginSharkRecycleTrial());
        var start = trial.Start;
        int sharkId = start.Shark.Target.InstanceIndex;
        Vector3 nestPosition = start.AuthoredHomeScene + Vector3.UnitX * 3f;
        var expected = ToEffectNative(start.AuthoredHomeScene) - ToEffectNative(nestPosition);
        float sourceYaw = MathF.Atan2(expected.Y, expected.X);
        float? firstYaw = null;
        foreach (float z in new[] { 5f, -5f })
        {
            Vector3 deathPosition = nestPosition + Vector3.UnitZ * z;
            var retirement = trial.StageDeathAndRetire(deathPosition);
            Require(retirement.Damaged && retirement.Retired && retirement.WaitingShark == sharkId,
                "staged shark death was not admitted to its nest pool");
            trial.ActivateFromNest(nestPosition);
            var sample = trial.Step(ToEffectNative(deathPosition));
            var launched = sample.Native;
            Require(launched.Phase == Rac1AridiaEnemyPhase.Launched &&
                launched.Sequence == Rac1AridiaEnemies.NestReleasedSharkSequence &&
                MathF.Abs(launched.Yaw - sourceYaw) < .001f && launched.Motion.X > 0 &&
                sample.NativeState == Rac1AridiaEnemies.NestReleasedSharkState,
                "staged recycled shark aimed toward its mutable death location");
            if (firstYaw.HasValue) Require(MathF.Abs(firstYaw.Value - launched.Yaw) < .001f,
                "second shark relaunch did not preserve first authored heading");
            firstYaw = launched.Yaw;
        }
        Require(trial.ReadAuthoredHomeScene().DistanceTo(start.OriginalRootScene) < .001f,
            "Aridia source home changed during staged recycle");
        Log($"[aridia-bot-smoke] class580 i{sharkId} staged two-cycle authored-home relaunch heading PASS");
    }

    // The shared Rac1SmokeWaitAsync waits on ProcessFrame, which can advance
    // much faster than Godot physics/native enemy ticks in headless tests.
    // Arrival and animation transitions must consume a bounded PHYSICS
    // budget; a render-frame timeout is not an actual native stall.
    private async Task Rac1AridiaSmokeWaitNativeFramesAsync(
        Func<bool> condition, int maxPhysicsFrames, string label,
        Func<string>? timeoutEvidence = null)
    {
        for (int frame = 0; frame < maxPhysicsFrames; frame++)
        {
            if (condition()) return;
            await PhysicsFramesAsync(1);
        }
        if (condition()) return;
        throw new TimeoutException(
            $"Timed out waiting for Aridia native state: {label} after {maxPhysicsFrames} " +
            $"physics frames. {(timeoutEvidence is null ? "" : timeoutEvidence())}");
    }

}
