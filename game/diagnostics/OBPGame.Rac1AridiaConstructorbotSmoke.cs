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
            await Rac1SmokeWaitAsync(() =>
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
            await Rac1SmokeWaitAsync(() => particleNative.State.NativeState == 2, 80,
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
            await Rac1SmokeWaitAsync(() => particleNative.State.NativeState is >= 3 and <= 5,
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
            await Rac1SmokeWaitAsync(() => particleNative.State.NativeState == 5,
                280, "native class612 state3 source descent -> state4 clip5 -> state5");
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
            GD.Print($"[aridia-bot-smoke] class612 i551 staged state5 " +
                $"first+lookahead ground probes before={walkedStart:F3} " +
                $"after={walkedEnd:F3} PASS");

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
            await Rac1SmokeWaitAsync(() =>
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
            await Rac1SmokeWaitAsync(() =>
                _rac1MobyRuntime.Require(volumeBot.Node.Source).State.NativeState == 2, 80,
                "retail class612 instance558 clip-volume21 activation state9->2");
            Require(!volumeBot.Animation.SupportsSequence(16) || volumeBot.Animation.Sequence == 16,
                "source clip16 was not selected after volume activation");
            GD.Print($"[aridia-bot-smoke] real class612 i558 clip-volume21 " +
                $"state9->2 source clip={volumeBot.Animation.Sequence} admission PASS");
            ApplicationLifecycle.RequestQuit(this, "rac1-aridia-constructorbot-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[aridia-bot-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-aridia-constructorbot-smoke-fail", 4);
        }
    }
}
