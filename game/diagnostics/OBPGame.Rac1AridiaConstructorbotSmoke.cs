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
            Require(_rac1ConstructorbotAuthoredCenters.TryGetValue(botId, out var waypoint),
                "mode0 bot has no authored waypoint fallback");

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
            ApplicationLifecycle.RequestQuit(this, "rac1-aridia-constructorbot-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[aridia-bot-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-aridia-constructorbot-smoke-fail", 4);
        }
    }
}
