using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>
    /// Real Godot fixture using the original Aridia #548 Constructorbot
    /// mesh, collision, authored placement and playable player actor.
    /// The scene's target is deliberately repositioned: this is a STAGED
    /// integration test, not proof of an unassisted retail encounter.
    /// </summary>
    private async Task RunRac1AridiaConstructorbotSmokeAsync()
    {
        try
        {
            Require(_world is { Game: "rac1", LevelId: Rac1AridiaEnemies.LevelId } &&
                _player is not null, "Aridia Constructorbot smoke requires retail level 2");
            Engine.MaxFps = 60;
            Vector3 originalPlayerSpawn = _player!.GlobalPosition;
            ClearMovementSmokeInput();
            Require(_rac1Enemies.Count == 137, "Aridia hostile census changed");
            const int botId = 548; // retail mode0, state7, sequence0
            var bot = _rac1Enemies[botId];
            Require(bot.Node.Source.NativeClassId == Rac1AridiaEnemies.Constructorbot,
                "Smoke actor is not authored class612");
            var native = _rac1MobyRuntime.Require(bot.Node.Source);
            Require(native.State.NativeState == Rac1AridiaConstructorbotNative.RestState,
                "mode0 bot did not initialise to source state7");
            Require(_rac1ConstructorbotNativeWaypoints.TryGetValue(botId, out var waypoint),
                "mode0 bot has no original native waypoint");
            int native548 = Rac1AridiaConstructorbotNative.ReadWaypointRecordIndex(
                native.PVar.Span, _rac1AridiaClipVolumes.Count);
            Require(native548 == 22, "mode0 bot does not have retail waypoint record22");
            Require(waypoint.DistanceTo(FromEffectNative(_rac1AridiaClipVolumes[22].Centre)) < .001f,
                "mode0 bot waypoint is not original gameplay record22");

            await PhysicsFramesAsync(10);
            Vector3 position = bot.Body.GlobalPosition;
            Vector3 forward = EnemyForward(bot.Node);
            forward.Y = 0;
            Require(forward.LengthSquared() > .5f, "bot's authored facing is invalid");
            forward = forward.Normalized();

            // This is a controlled, near-waypoint input fixture. None of the
            // six Sandmouse-gated class580 actors is explicitly woken.
            _player!.ApplyDevelopmentReset(position + forward * 3 + Vector3.Up * .05f);
            await Rac1AridiaSmokeWaitNativeFramesAsync(() =>
                _rac1MobyRuntime.Require(bot.Node.Source).State.NativeState ==
                Rac1AridiaConstructorbotNative.JointContactState, 180,
                "source state7 to joint-contact state11");
            Require(bot.Animation.Sequence == 8 ||
                _rac1MobyRuntime.Require(bot.Node.Source).State.NativeState ==
                Rac1AridiaConstructorbotNative.ContactRecoveryState,
                "source state11 failed to select native sequence8");
            Require(_rac1AridiaEnemies.Health(botId) == 3,
                "source contact entry unexpectedly damaged the Constructorbot");

            Vector3 joint = EnemySocket(bot);
            GD.Print($"[aridia-bot-smoke] staged contact probe joint={joint} " +
                $"player={_player.GlobalPosition} forward={EnemyForward(bot.Node)} " +
                $"sequence={bot.Animation.Sequence} health={_rac1Nanotech.Probe().Nanotech}");
            int nanotechBeforeContact = _rac1Nanotech.Probe().Nanotech;
            bool contacted = false;
            for (int tick = 0; tick < 15 && !contacted; tick++)
            {
                // Staged collision probe uses a real player capsule next
                // to the ORIGINAL joint0 animation socket. Keep the actor
                // airborne for the fixture, so gravity doesn't move it
                // below this high-mounted source hitbox.
                Vector3 socket = EnemySocket(bot);
                Vector3 attackForward = EnemyForward(bot.Node);
                _player.StageDevelopmentWaterPosition(socket +
                    attackForward * .45f - Vector3.Up * .3f);
                await PhysicsFramesAsync(1);
                contacted = _rac1Nanotech.Probe().Nanotech < nanotechBeforeContact;
            }
            Require(contacted, "native state11 joint ray did not damage staged player capsule");
            GD.Print($"[aridia-bot-smoke] native state11 joint ray -> real Ratchet " +
                $"nanotech {nanotechBeforeContact}->{_rac1Nanotech.Probe().Nanotech} PASS");
            GD.Print($"[aridia-bot-smoke] real class612 i{botId} state7->11 " +
                $"at authored spawn, original model/contact body and player staging PASS; " +
                $"nativeFrame={_rac1MobyRuntime.Require(bot.Node.Source).State.NativeState}");
            // Also prove the native STATE8 branch, not just the more
            // numerous state9 Constructorbots. #551 is the one retail
            // mode2 bot: activation from record14 must select source clip4.
            const int particleBotId = 551;
            var particleBot = _rac1Enemies[particleBotId];
            var particleNative = _rac1MobyRuntime.Require(particleBot.Node.Source);
            Require(particleNative.State.NativeState == 8,
                "retail mode2 Constructorbot activated before source proximity");
            Require(Rac1AridiaConstructorbotNative.ReadWaypointRecordIndex(
                particleNative.PVar.Span, _rac1AridiaClipVolumes.Count) == 14,
                "retail mode2 Constructorbot does not use native record14");
            _player.StageDevelopmentWaterPosition(FromEffectNative(_rac1AridiaClipVolumes[14].Centre));
            await Rac1AridiaSmokeWaitNativeFramesAsync(() => particleNative.State.NativeState == 2, 80,
                "native class612 instance551 state8->2 source distance trigger");
            Require(!particleBot.Animation.SupportsSequence(4) || particleBot.Animation.Sequence == 4,
                "mode2 bot did not choose source entry clip4");
            GD.Print("[aridia-bot-smoke] real class612 i551 native record14 " +
                "distance admission state8->2 clip4 PASS");
            Vector3 originalMotionPosition = particleBot.Node.Root.GlobalPosition;
            Vector3 actualWaypoint = FromEffectNative(_rac1AridiaClipVolumes[14].Centre);
            float originalPlanarDistance = new Vector2(
                originalMotionPosition.X - actualWaypoint.X,
                originalMotionPosition.Z - actualWaypoint.Z).Length();
            await Rac1AridiaSmokeWaitNativeFramesAsync(() => particleNative.State.NativeState is >= 3 and <= 5,
                280, "source state2 clip4 completion and state3 horizontal waypoint motion");
            Vector3 movedPosition = particleBot.Node.Root.GlobalPosition;
            float movedPlanarDistance = new Vector2(
                movedPosition.X - actualWaypoint.X,
                movedPosition.Z - actualWaypoint.Z).Length();
            Require(movedPlanarDistance < originalPlanarDistance - .015f,
                $"Constructorbot state3 did not move toward native waypoint14: " +
                $"before={originalPlanarDistance:F3} after={movedPlanarDistance:F3}");
            GD.Print($"[aridia-bot-smoke] class612 i551 native movement toward record14 " +
                $"before={originalPlanarDistance:F3} after={movedPlanarDistance:F3} " +
                $"state={particleNative.State.NativeState} PASS");

            // This fixture still stages the PLAYER near authored record14,
            // but no longer forces the Constructorbot to native state5 or
            // manually moves its body onto terrain. Source state3 must now
            // descend to real imported collision geometry and state4 must
            // complete its animation before the walker is admitted.
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => particleNative.State.NativeState == 5, 280,
                "native class612 state3 descent -> state4 clip5 -> state5",
                () =>
                {
                    Vector3 current = particleBot.Node.Root.GlobalPosition;
                    return $"native={particleNative.State.NativeState} " +
                        $"clip={particleBot.Animation.Sequence} " +
                        $"position={current} waypoint={actualWaypoint} " +
                        $"floor={ProbeRac1ConstructorbotStateThreeFloor(particleBot, current)} " +
                        $"verticalStep={_rac1ConstructorbotStateThreeVerticalSteps.GetValueOrDefault(particleBotId)} " +
                        $"player={_player!.GlobalPosition}";
                });
            Require(particleBot.Animation.Sequence == 6 ||
                !particleBot.Animation.SupportsSequence(6),
                "native state5 did not select source clip6");
            GD.Print($"[aridia-bot-smoke] real class612 i551 " +
                $"state3 floor descent ->state4 clip5 ->state5 clip6 " +
                $"bodyY={particleBot.Body.GlobalPosition.Y:F3} PASS");
            float walkerStart = new Vector2(
                particleBot.Node.Root.GlobalPosition.X - actualWaypoint.X,
                particleBot.Node.Root.GlobalPosition.Z - actualWaypoint.Z).Length();
            await PhysicsFramesAsync(36);
            float walkerEnd = new Vector2(
                particleBot.Node.Root.GlobalPosition.X - actualWaypoint.X,
                particleBot.Node.Root.GlobalPosition.Z - actualWaypoint.Z).Length();
            if (walkerStart > .1f)
                Require(walkerEnd < walkerStart - .025f,
                    $"state5 source walker failed to approach native waypoint: " +
                    $"before={walkerStart:F3} after={walkerEnd:F3}");
            else
                Require(walkerEnd < .25f,
                    $"state5 source walker drifted off a waypoint already reached " +
                    $"by native state3: after={walkerEnd:F3}");
            GD.Print($"[aridia-bot-smoke] class612 i551 natural state5 " +
                $"walker waypoint before={walkerStart:F3} after={walkerEnd:F3} " +
                $"state={particleNative.State.NativeState} PASS");

            // Re-stage the SAME retail body one unit toward its authored
            // arrival position along actual level geometry. Unlike a
            // waypoint-at-zero check, this gives state5 a nonzero travel
            // vector and exercises its first AND ahead-of-actor probes.
            Vector3 back = originalMotionPosition - actualWaypoint;
            back.Y = 0;
            Require(back.LengthSquared() > .25f, "missing authored arrival direction");
            Vector3 offWaypoint = actualWaypoint + back.Normalized() * 1.1f;
            particleBot.Body.GlobalPosition = offWaypoint;
            particleBot.Node.Root.GlobalPosition = offWaypoint;
            Require(TryGroundRac1Enemy(particleBot, upwardReach: .5f, downwardReach: 40f),
                "cannot stage walker lookahead onto imported ground");
            particleBot.Node.Root.GlobalPosition = particleBot.Body.GlobalPosition;
            _rac1MobyRuntime.SetNativeState(particleNative, 5);
            float walkedStart = new Vector2(
                particleBot.Body.GlobalPosition.X - actualWaypoint.X,
                particleBot.Body.GlobalPosition.Z - actualWaypoint.Z).Length();
            await PhysicsFramesAsync(85);
            float walkedEnd = new Vector2(
                particleBot.Body.GlobalPosition.X - actualWaypoint.X,
                particleBot.Body.GlobalPosition.Z - actualWaypoint.Z).Length();
            Require(walkedEnd < walkedStart - .025f,
                $"source walker with lookahead did not move across real terrain: " +
                $"before={walkedStart:F3} after={walkedEnd:F3}");
            int nativeLiftsApplied =
                _rac1ConstructorbotNativeLiftCounts.GetValueOrDefault(particleBotId);
            Require(nativeLiftsApplied > 0,
                "original class612 walker never applied source 30%-floor lift " +
                "during the real waypoint traversal");
            GD.Print($"[aridia-bot-smoke] class612 i551 staged state5 " +
                $"first+lookahead ground probes before={walkedStart:F3} " +
                $"after={walkedEnd:F3} source-lift-applied={nativeLiftsApplied} PASS");

            // Deliberate native swept-segment branch fixture: source
            // walker_move_v skips its segment for <=0.625 distance.
            // Exercise a real >0.625 Godot scene ray with the original
            // class612 body, without fabricating a collision hit.
            Vector3 beforeSweepBody = particleBot.Body.GlobalPosition;
            int priorSweeps = _rac1ConstructorbotSweepProbeCounts.GetValueOrDefault(particleBotId);
            MoveRac1AridiaWalkerContact(particleBot, back.Normalized() * .8f);
            Require(_rac1ConstructorbotSweepProbeCounts.GetValueOrDefault(particleBotId) ==
                    priorSweeps + 1,
                "retail class612 state5 sweep did not use original 0x280 radius gate");
            particleBot.Body.GlobalPosition = beforeSweepBody;
            particleBot.Node.Root.GlobalPosition = beforeSweepBody;
            GD.Print("[aridia-bot-smoke] real class612 i551 native radius0.625 " +
                "swept-segment Godot ray branch PASS");

            // Probe a real overlap on the imported floor without moving
            // the retail enemy itself: lower the tentative Moby origin so
            // its +1-raised source sphere centre intersects level terrain.
            // The returned correction must come from an actual Godot
            // collide_shape contact pair, not invented upward grounding.
            var sphereStarting = beforeSweepBody - Vector3.Up * .8f;
            var sphereResult = ProbeRac1AridiaWalkerSpherePushouts(
                particleBot, sphereStarting);
            Require(sphereResult.Contacts > 0,
                "retail class612 host sphere query did not observe real terrain overlap");
            Require(FromEffectNative(sphereResult.Position).Y > sphereStarting.Y + .01f,
                "source six-attempt sphere host adapter failed to push out of real ground");
            Require(sphereResult.Contacts <=
                    Rac1AridiaConstructorbotNative.WalkerSpherePushoutAttempts,
                "source walker sphere pushout exceeded six attempts");
            GD.Print($"[aridia-bot-smoke] class612 i551 source six-attempt " +
                $"sphere overlap contacts={sphereResult.Contacts} " +
                $"lift={FromEffectNative(sphereResult.Position).Y - sphereStarting.Y:F3} PASS");

            // Real imported terrain must actually intersect the DIFFERENT
            // post-motion walker_move_v ray at position+1 to position-0.05.
            // Deliberately LOWER only its candidate, not the real enemy,
            // to expose a nonzero 30%-lift example on original scene data.
            Vector3 partialLiftCandidate = beforeSweepBody - Vector3.Up * .4f;
            float? observedLift = ProbeRac1AridiaWalkerGroundLift(
                particleBot, partialLiftCandidate);
            Require(observedLift is > .01f && observedLift < .3f,
                $"class612 original post-move floor segment did not observe " +
                $"a bounded partial lift (lift={observedLift})");
            float? unsupportedLift = ProbeRac1AridiaWalkerGroundLift(
                particleBot, beforeSweepBody + Vector3.Up * 30f);
            Require(!unsupportedLift.HasValue,
                "original walker post-move segment invented airborne floor contact");
            Require(particleBot.Body.GlobalPosition.DistanceTo(beforeSweepBody) < .001f,
                "non-mutating source floor ray unexpectedly relocated class612 body");
            GD.Print($"[aridia-bot-smoke] class612 i551 real source vertical " +
                $"segment positive 30%-lift={observedLift.GetValueOrDefault():F3} " +
                $"airborne miss PASS");

            // Negative scene fixture: same real collision body, but suspended
            // well above any terrain. Native walker_probe must return no
            // support; the host may turn, but cannot move or finish state5.
            Vector3 supportedLocation = particleBot.Body.GlobalPosition;
            Vector3 airborneLocation = supportedLocation + Vector3.Up * 30f;
            particleBot.Body.GlobalPosition = airborneLocation;
            particleBot.Node.Root.GlobalPosition = airborneLocation;
            _rac1MobyRuntime.SetNativeState(particleNative, 5);
            await PhysicsFramesAsync(8);
            Require(particleBot.Body.GlobalPosition.DistanceTo(airborneLocation) < .002f,
                "Constructorbot walker moved through unsupported air");
            Require(particleNative.State.NativeState == 5,
                "Constructorbot walker falsely completed while floor probe was missing");
            GD.Print("[aridia-bot-smoke] real class612 i551 missing-floor " +
                "walker blocked without phantom state7 PASS");
            particleBot.Body.GlobalPosition = supportedLocation;
            particleBot.Node.Root.GlobalPosition = supportedLocation;

            // Third fixture: mode3 class612 #549 has PVar+0x18c=-1;
            // native L2:0x2d7fac checks its PVar+0x188 record12's centre
            // and PVar+0x184 full-XYZ radius20, NOT an inferred spawn.
            const int distanceBotId = 549;
            var distanceBot = _rac1Enemies[distanceBotId];
            var distanceNative = _rac1MobyRuntime.Require(distanceBot.Node.Source);
            Require(Rac1AridiaConstructorbotNative.ReadActivationVolume(distanceNative.PVar.Span) == -1,
                "source proximity bot incorrectly uses a clip admission volume");
            int distanceNode = Rac1AridiaConstructorbotNative.ReadWaypointRecordIndex(
                distanceNative.PVar.Span, _rac1AridiaClipVolumes.Count);
            Require(distanceNode == 12, "proximity bot's source node was not record12");
            Require(distanceNative.State.NativeState == 9,
                "proximity bot was woken before entering its authored radius");
            _player.StageDevelopmentWaterPosition(
                FromEffectNative(_rac1AridiaClipVolumes[distanceNode].Centre));
            await Rac1AridiaSmokeWaitNativeFramesAsync(() =>
                distanceNative.State.NativeState == 2, 80,
                "source class612 instance549 waypoint record12 range admission state9->2");
            GD.Print("[aridia-bot-smoke] real class612 i549 native record12 " +
                "distance admission state9->2 PASS");

            // A second retail actor, #558, starts in native state9 and
            // uniquely references authentic clip volume 21 at PVar+0x18c.
            // No fabricated route-node fallback is used for this actor.
            const int volumeBotId = 558;
            var volumeBot = _rac1Enemies[volumeBotId];
            var sourceVolume = _rac1AridiaClipVolumes[21];
            Require(Rac1AridiaConstructorbotNative.ReadActivationVolume(
                _rac1MobyRuntime.Require(volumeBot.Node.Source).PVar.Span) == 21,
                "volume bot does not reference native volume21");
            Require(_rac1MobyRuntime.Require(volumeBot.Node.Source).State.NativeState == 9,
                "volume Constructorbot was activated before Ratchet entered its volume");
            Require(!_rac1AridiaClipVolumes[21].Contains(ToEffectNative(_player.GlobalPosition)),
                "initial contact fixture accidentally overlapped the volume");
            _player.StageDevelopmentWaterPosition(FromEffectNative(sourceVolume.Centre));
            await Rac1AridiaSmokeWaitNativeFramesAsync(() =>
                _rac1MobyRuntime.Require(volumeBot.Node.Source).State.NativeState == 2, 80,
                "retail class612 instance558 clip-volume21 activation state9->2");
            Require(!volumeBot.Animation.SupportsSequence(16) || volumeBot.Animation.Sequence == 16,
                "source clip16 was not selected after volume activation");
            GD.Print($"[aridia-bot-smoke] real class612 i558 clip-volume21 " +
                $"state9->2 source clip={volumeBot.Animation.Sequence} admission PASS");
            // Original state7 creates a return destination only after
            // source clip0 completes. Use a retail actor and the saved
            // original player spawn to keep Ratchet outside this bot's
            // source engagement radius. The enemy itself is NOT given
            // a fabricated return point.
            float roamingEngagementRadius =
                Rac1AridiaConstructorbotNative.ReadEngagementRadius(particleNative.PVar.Span);
            Vector3 outward = originalPlayerSpawn - actualWaypoint;
            outward.Y = 0;
            Require(outward.LengthSquared() > 1f,
                "initial player spawn must distinguish the source waypoint");
            // Keep the player OUTSIDE native combat admission but INSIDE
            // Aridia's intentional 24-unit animation/AI update radius.
            // Moving all the way to spawn freezes the retail clip0 clock.
            Require(roamingEngagementRadius + 2f < AridiaHostUpdateRadius - 1f,
                "there is no safe source roam fixture within the host update radius");
            _player.StageDevelopmentWaterPosition(
                actualWaypoint + outward.Normalized() * (roamingEngagementRadius + 2f));
            Require(_player.GlobalPosition.DistanceTo(actualWaypoint) >
                    roamingEngagementRadius,
                "staged player must remain beyond native engagement radius");
            _rac1MobyRuntime.SetNativeState(particleNative,
                Rac1AridiaConstructorbotNative.RestState);
            SelectRac1EnemySequence(particleBot, 0, 1);
            _rac1ConstructorbotRoamingTargets.Remove(particleBotId);
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => _rac1ConstructorbotRoamingTargets.ContainsKey(particleBotId),
                240, "source state7 completed idle animation selects dynamic state6 target");
            Require(_rac1ConstructorbotRoamingTargets.TryGetValue(particleBotId, out var roam),
                "retail Constructorbot did not store its dynamic roaming target");
            Require(MathF.Abs(roam.X - actualWaypoint.X) <= 2.001f &&
                    MathF.Abs(roam.Z - actualWaypoint.Z) <= 2.001f &&
                    MathF.Abs(roam.Y - actualWaypoint.Y) < .001f,
                "native state6 destination is not anchored to original waypoint record14");
            GD.Print($"[aridia-bot-smoke] original class612 i551 " +
                $"state7 clip0->state6 roaming target={roam} anchor={actualWaypoint} PASS");
            Vector3 originalRoamPosition = particleBot.Body.GlobalPosition;
            float roamDistanceBefore = new Vector2(
                originalRoamPosition.X - roam.X, originalRoamPosition.Z - roam.Z).Length();
            await PhysicsFramesAsync(45);
            float roamDistanceAfter = new Vector2(
                particleBot.Body.GlobalPosition.X - roam.X,
                particleBot.Body.GlobalPosition.Z - roam.Z).Length();
            Require(roamDistanceAfter < roamDistanceBefore - .005f,
                $"native state6 did not move on actual terrain toward its dynamic target: " +
                $"before={roamDistanceBefore:F3} after={roamDistanceAfter:F3}");
            Require(particleNative.State.NativeState ==
                    Rac1AridiaConstructorbotNative.RoamingState,
                "state6 changed state despite clear walker return bits and distant player");
            GD.Print($"[aridia-bot-smoke] class612 i551 45-frame roam " +
                $"distance={roamDistanceBefore:F3}->{roamDistanceAfter:F3} " +
                $"state={particleNative.State.NativeState} PASS");

            // Return Ratchet to the ORIGINAL record14 engagement range.
            // A clear state6 walker result must admit state5 rather than
            // reselect state7, and the old random target must be released.
            _player.StageDevelopmentWaterPosition(actualWaypoint);
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => particleNative.State.NativeState == 5, 100,
                "native state6 engages Ratchet and returns to source state5");
            Require(!_rac1ConstructorbotRoamingTargets.ContainsKey(particleBotId),
                "state6->5 retained an obsolete dynamic roaming destination");
            Require(particleBot.Animation.Sequence == 6 ||
                    !particleBot.Animation.SupportsSequence(6),
                "source state6->5 transition did not preserve walker clip6");
            GD.Print("[aridia-bot-smoke] class612 i551 roaming state6->5 " +
                "player-near-anchor and target cleanup PASS");

            // Move Ratchet back outside the original engagement radius,
            // still within the 24-unit host update radius. The bot must
            // finish approaching its actual waypoint and resume idle,
            // then generate another independent native-style target.
            _player.StageDevelopmentWaterPosition(
                actualWaypoint + outward.Normalized() * (roamingEngagementRadius + 2f));
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => particleNative.State.NativeState ==
                    Rac1AridiaConstructorbotNative.RestState, 260,
                "native state5 reaches planar waypoint and returns to state7");
            float planarReturnDistance = new Vector2(
                particleBot.Body.GlobalPosition.X - actualWaypoint.X,
                particleBot.Body.GlobalPosition.Z - actualWaypoint.Z).Length();
            Require(planarReturnDistance < Rac1AridiaConstructorbotNative.WalkerStopDistance,
                "native state5 returned before reaching original waypoint in XY");
            Require(!_rac1ConstructorbotRoamingTargets.ContainsKey(particleBotId),
                "return to state7 retained the previous roaming target");
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => particleNative.State.NativeState ==
                    Rac1AridiaConstructorbotNative.RoamingState &&
                    _rac1ConstructorbotRoamingTargets.ContainsKey(particleBotId),
                260, "state7 idle completion starts another state6 roaming cycle");
            Require(_rac1ConstructorbotRoamingTargets.TryGetValue(
                    particleBotId, out var nextRoam),
                "restarted roaming cycle has no dynamic target");
            Require(MathF.Abs(nextRoam.X - actualWaypoint.X) <= 2.001f &&
                    MathF.Abs(nextRoam.Z - actualWaypoint.Z) <= 2.001f &&
                    MathF.Abs(nextRoam.Y - actualWaypoint.Y) < .001f,
                "restarted roaming target was not sourced from the original waypoint");
            GD.Print("[aridia-bot-smoke] class612 i551 state6->5->7->6 " +
                "repeat roaming cycle PASS");

            // Let the newly generated random patrol reach a NATIVE solver
            // completion on real imported ground. Unlike the earlier staged
            // Ratchet engagement, this leg never sets the enemy's state,
            // body, or roaming target after state6 admission.
            float secondLegStart = new Vector2(
                particleBot.Body.GlobalPosition.X - nextRoam.X,
                particleBot.Body.GlobalPosition.Z - nextRoam.Z).Length();
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => particleNative.State.NativeState ==
                    Rac1AridiaConstructorbotNative.RestState, 540,
                "native state6 naturally returns to state7 from arrival/obstruction");
            Require(_rac1ConstructorbotRoamExitFlags.TryGetValue(
                    particleBotId, out int exitFlags),
                "natural state6->7 lacked a recorded source walker result");
            Require((exitFlags & Rac1AridiaConstructorbotNative.WalkerHeadingOrContactMask) != 0,
                $"state6->7 admitted with unsupported walker flags {exitFlags:X2}");
            Require(!_rac1ConstructorbotRoamingTargets.ContainsKey(particleBotId),
                "natural state6->7 retained its dynamic destination");
            float secondLegEnd = new Vector2(
                particleBot.Body.GlobalPosition.X - nextRoam.X,
                particleBot.Body.GlobalPosition.Z - nextRoam.Z).Length();
            if ((exitFlags & Rac1AridiaConstructorbotNative.WalkerNearTargetFlag) != 0)
                Require(secondLegEnd < Rac1AridiaConstructorbotNative.WalkerStopDistance,
                    $"state6 natural arrival bit4 did not reach the patrol destination: " +
                    $"distance={secondLegEnd:F3}");
            else
                Require((exitFlags & Rac1AridiaConstructorbotNative.WalkerBlockedFlag) != 0,
                    "state6 natural departure without arrival was not a collision/ledge block");
            GD.Print($"[aridia-bot-smoke] class612 i551 natural state6->7 " +
                $"distance={secondLegStart:F3}->{secondLegEnd:F3} " +
                $"flags=0x{exitFlags:X2} PASS");

            // Separate real-geometry unsupported-floor branch. Allow state7
            // to create its next source-random destination, then stage this
            // same retail body's origin 8 units above the actual ground.
            // The host must observe a genuine missing-floor probe, which is
            // walker 0x22 and hence state6->7, not forge proximity arrival.
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => particleNative.State.NativeState ==
                    Rac1AridiaConstructorbotNative.RoamingState &&
                    _rac1ConstructorbotRoamingTargets.ContainsKey(particleBotId),
                260, "next native state7 animation creates blocked-case roaming target");
            Require(!_rac1ConstructorbotRoamExitFlags.ContainsKey(particleBotId),
                "new roaming destination did not clear the previous departure flags");
            Vector3 supportedBody = particleBot.Body.GlobalPosition;
            Vector3 unsupportedBody = supportedBody + Vector3.Up * 8f;
            Require(unsupportedBody.DistanceTo(_player.GlobalPosition) <
                    AridiaHostUpdateRadius - 1f,
                "blocked roaming fixture would freeze source updates");
            particleBot.Body.GlobalPosition = unsupportedBody;
            particleBot.Node.Root.GlobalPosition = unsupportedBody;
            await Rac1AridiaSmokeWaitNativeFramesAsync(
                () => particleNative.State.NativeState ==
                    Rac1AridiaConstructorbotNative.RestState,
                16, "state6 sees missing imported floor and returns to state7");
            Require(particleBot.Body.GlobalPosition.DistanceTo(unsupportedBody) < .002f,
                "blocked state6 moved the real body through unsupported air");
            Require(_rac1ConstructorbotRoamExitFlags.TryGetValue(
                    particleBotId, out int blockedFlags) &&
                    (blockedFlags & Rac1AridiaConstructorbotNative.WalkerBlockedFlag) != 0,
                "missing-floor state6->7 did not report native walker bit2");
            Require((blockedFlags & Rac1AridiaConstructorbotNative.WalkerNearTargetFlag) == 0,
                "unsupported-air branch incorrectly claimed source arrival bit4");
            Require(!_rac1ConstructorbotRoamingTargets.ContainsKey(particleBotId),
                "missing-floor state6->7 retained its obsolete roaming target");
            particleBot.Body.GlobalPosition = supportedBody;
            particleBot.Node.Root.GlobalPosition = supportedBody;
            GD.Print($"[aridia-bot-smoke] class612 i551 unsupported-floor " +
                $"state6->7 flags=0x{blockedFlags:X2} target cleanup PASS");

            VerifyAridiaSharkTwoCycleAuthoredHome();
            ApplicationLifecycle.RequestQuit(this, "rac1-aridia-constructorbot-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[aridia-bot-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-aridia-constructorbot-smoke-fail", 4);
        }
    }

    /// <summary>
    /// Staged regression for PR #169's reviewed repeated-recycling bug.
    /// Use real imported Godot shark/nest host nodes but an isolated
    /// engine-neutral group fixture so no ordinary player activation,
    /// nest timer or death animation is silently claimed as exercised.
    /// The host release method must use the immutable registration pose
    /// even when the live shark root is at two opposite death positions.
    /// </summary>
    private void VerifyAridiaSharkTwoCycleAuthoredHome()
    {
        var shark = _rac1Enemies.Values.First(
            e => e.Node.Source.NativeClassId == Rac1AridiaEnemies.Sandshark);
        var nest = _rac1Enemies.Values.First(
            e => e.Node.Source.NativeClassId == Rac1AridiaEnemies.Nest);
        int sharkId = shark.Node.Source.InstanceIndex;
        int nestId = nest.Node.Source.InstanceIndex;
        var originalSession = _rac1AridiaEnemies;
        var originalRoot = shark.Node.Root.GlobalPosition;
        var originalBody = shark.Body.GlobalPosition;
        bool originalVisible = shark.Node.Root.Visible;
        bool originalBuried = shark.Buried;
        bool originalPlacement = shark.NeedsGroundPlacement;
        uint originalLayer = shark.Body.CollisionLayer;
        uint originalMask = shark.Body.CollisionMask;
        int originalDeathTicks = shark.DeathTicks;
        int originalSequence = shark.Animation.Sequence;
        var native = _rac1MobyRuntime.Require(shark.Node.Source);
        int originalNativeState = native.State.NativeState;
        try
        {
            var fixture = new Rac1AridiaEnemySession();
            fixture.Register(nestId, Rac1AridiaEnemies.Nest, groupId: 7);
            fixture.Register(sharkId, Rac1AridiaEnemies.Sandshark, groupId: 7);
            fixture.SetAuthoredGroupRoster(new Dictionary<int, int[]>
            {
                [7] = [sharkId]
            });
            _rac1AridiaEnemies = fixture;
            // Keep the nest offset well clear of the real shark home.
            // The two simulated death points face opposite directions
            // relative to this nest. Neither is the imported home.
            Vector3 nestPosition = shark.AuthoredHomePosition + Vector3.Right * 3f;
            var expected = ToEffectNative(shark.AuthoredHomePosition) -
                ToEffectNative(nestPosition);
            float sourceYaw = MathF.Atan2(expected.Y, expected.X);
            float? firstYaw = null;
            foreach (float z in new[] { 5f, -5f })
            {
                Vector3 deathPosition = nestPosition + Vector3.Back * z;
                shark.Node.Root.GlobalPosition = deathPosition;
                shark.Body.GlobalPosition = deathPosition;
                Require(fixture.Damage(sharkId, 1) &&
                    fixture.RetireToNestPool(sharkId) &&
                    fixture.FindWaitingSharkForNest(nestId) == sharkId,
                    "staged shark death was not admitted to its nest pool");
                ActivateNearbyRac1AridiaShark(nestId, nestPosition);
                var launched = fixture.Step(sharkId, new(
                    ToEffectNative(deathPosition), ToEffectNative(deathPosition),
                    PlayerAlive: false, LineOfSight: false));
                Require(launched.Phase == Rac1AridiaEnemyPhase.Launched &&
                    launched.Sequence == Rac1AridiaEnemies.NestReleasedSharkSequence &&
                    MathF.Abs(launched.Yaw - sourceYaw) < .001f &&
                    launched.Motion.X > 0 &&
                    _rac1MobyRuntime.Require(shark.Node.Source).State.NativeState ==
                    Rac1AridiaEnemies.NestReleasedSharkState,
                    "staged recycled shark aimed toward its mutable death location");
                if (firstYaw.HasValue)
                    Require(MathF.Abs(firstYaw.Value - launched.Yaw) < .001f,
                        "second shark relaunch did not preserve first authored heading");
                firstYaw = launched.Yaw;
            }
            Require(shark.AuthoredHomePosition.DistanceTo(originalRoot) < .001f,
                "Aridia source home changed during staged recycle");
            GD.Print($"[aridia-bot-smoke] class580 i{sharkId} staged two-cycle " +
                "authored-home relaunch heading PASS");
        }
        finally
        {
            _rac1AridiaEnemies = originalSession;
            shark.Node.Root.GlobalPosition = originalRoot;
            shark.Body.GlobalPosition = originalBody;
            shark.Node.Root.Visible = originalVisible;
            shark.Buried = originalBuried;
            shark.NeedsGroundPlacement = originalPlacement;
            shark.DeathTicks = originalDeathTicks;
            shark.Body.CollisionLayer = originalLayer;
            shark.Body.CollisionMask = originalMask;
            _rac1MobyRuntime.SetNativeState(native, originalNativeState);
            if (shark.Animation.Sequence != originalSequence &&
                shark.Animation.SupportsSequence(originalSequence))
                SelectRac1EnemySequence(shark, originalSequence, 1);
        }
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
