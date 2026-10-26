using System.Numerics;
using System.Buffers.Binary;
using OBP.Core;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

public sealed class Rac1EnvironmentalDeathRegression(MovementDiagnosticContext context, IDiagnosticEnvironmentalDeath environment) : MovementScenarioSupport(context)
{
    public async Task RunAsync(Vector3 authoredRespawnPosition)
    {
        if (World.Read() is not { Source: ObpSourceGame.Rac1, LevelId: 0, DeathHeight: { } deathHeight })
        { throw new InvalidOperationException("Natural Veldin fall smoke requires a live LEVEL0 player."); }

        if (Math.Abs(deathHeight - 27f) > 0.0001f)
        {
            throw new InvalidOperationException(
            $"Veldin death height drifted to {deathHeight:R}, expected 27.");
        }

        double groundedSeparation = environment.Read().ContactSeparation;
        if (groundedSeparation > Rac1RatchetNanotechSession.RetailVeldinDeathContactSeparationExclusive)
        {
            throw new InvalidOperationException(
            $"Authored Veldin start was not in floor contact: separation={groundedSeparation:R}.");
        }

        Vector3 runStart = Player.Read().GlobalPosition;
        long deathGenerationBefore = environment.Read().DeathGeneration;
        long automaticRestartGenerationBefore = environment.Read().RestartGeneration;
        bool leftFloor = false;
        try
        {
            SetAnalogueSmokeInput(0f, 1f);
            for (int frame = 0;
                 frame < 900 && environment.Read().DeathGeneration == deathGenerationBefore;
                 frame++)
            {
                await PhysicsFramesAsync(1);
                leftFloor |= !Player.Read().Grounded;
            }
        }
        finally
        {
            ClearMovementSmokeInput();
        }

        if (environment.Read().DeathGeneration != deathGenerationBefore + 1 ||
            environment.Read().LastBoundary is not { } dead)
        {
            throw new TimeoutException(
            "Ordinary full-forward Veldin run never reached the environmental-death boundary.");
        }
        if (!leftFloor)
        {
            throw new InvalidOperationException(
            "Veldin environmental death occurred without leaving imported collision.");
        }

        Vector3 deathPosition = environment.Read().LastPosition;
        double deathSeparation = environment.Read().LastSeparation;
        float horizontalTravel = HorizontalDistance(runStart, deathPosition);
        if (horizontalTravel < 8f)
        {
            throw new InvalidOperationException(
            $"Veldin death route travelled only {horizontalTravel:R}; " +
            "expected an ordinary run across reachable imported collision before the fall.");
        }
        if (deathPosition.Y >= deathHeight)
        {
            throw new InvalidOperationException(
            $"Veldin death triggered above the recovered plane: " +
            $"y={deathPosition.Y:R}, death={deathHeight:R}.");
        }
        if (!(deathSeparation > Rac1RatchetNanotechSession.RetailVeldinDeathContactSeparationExclusive))
        {
            throw new InvalidOperationException(
            $"Veldin death triggered without recovered contact separation: {deathSeparation:R}.");
        }

        if (dead.Nanotech != 0 ||
            dead.NativePlayerState != Rac1RatchetNanotechSession.RetailVeldinDeathNativeState ||
            dead.NativeSequence != Rac1RatchetNanotechSession.RetailVeldinDeathNativeSequence ||
            dead.NativeSequenceFrame != Rac1RatchetNanotechSession.RetailVeldinDeathNativeSequenceFrame)
        {
            throw new InvalidOperationException(
            "Natural Veldin fall did not enter state 0x77 / sequence 11 frame 0.");
        }

        if (CurrentPlayerAvatarSourceSequence() is
            Rac1RatchetSequenceSelection.EnvironmentalDeathStartSequenceId or
            Rac1RatchetSequenceSelection.EnvironmentalDeathTerminalSequenceId)
        {
            throw new InvalidOperationException(
            "Godot player avatar invented unrecovered environmental-death playback.");
        }
        if (!PlayerAvatarPresentationIsSynchronized())
        {
            throw new InvalidOperationException(
            "Player-avatar presentation desynchronized at the death boundary.");
        }

        Log(
            $"[rac1-smoke] ordinary Veldin fall PASS; travel={horizontalTravel:0.000} " +
            $"y={deathPosition.Y:0.000} separation={deathSeparation:0.000} " +
            "state=0x77 sequence=11 frame=0 Nanotech=0");

        long deathGeneration = environment.Read().DeathGeneration;
        await Rac1SmokeWaitAsync(
            () => environment.Read().RestartGeneration == deathGeneration &&
                  !environment.Read().Nanotech.IsDead &&
                  Player.Read().Grounded,
            240,
            "automatic authored Veldin environmental restart");
        if (environment.Read().RestartGeneration <= automaticRestartGenerationBefore)
        {
            throw new InvalidOperationException(
            "Veldin restart completed without the automatic environmental-restart path.");
        }

        await Rac1SmokeWaitAsync(
            () => CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.StandingSequenceId &&
                  PlayerAvatarPresentationIsSynchronized() &&
                  Player.Read().HasActiveRecoveredCamera,
            120,
            "automatic respawn standing player/camera presentation");

        var respawn = environment.Read().Nanotech;
        if (respawn.Nanotech != 4 || !Player.Read().Rac1GameplayAlive)
        {
            throw new InvalidOperationException(
            "Veldin automatic restart did not restore four Nanotech/alive state.");
        }
        if (Player.Read().GlobalPosition.DistanceTo(authoredRespawnPosition) > 0.2f)
        {
            throw new InvalidOperationException(
            $"Veldin automatic restart missed authored start: " +
            $"{Player.Read().GlobalPosition} vs {authoredRespawnPosition}.");
        }

        var checkpoint = environment.Read().CheckpointYaw
            ?? throw new InvalidOperationException("Veldin automatic restart lost its checkpoint session.");
        if (Math.Abs(Player.Read().Rac1CurrentYaw - checkpoint) > 0.0001d)
        {
            throw new InvalidOperationException(
            $"Veldin automatic restart yaw {Player.Read().Rac1CurrentYaw:R} " +
            $"did not restore authored {checkpoint:R}.");
        }
        if (Player.Read().Velocity.Length() > 0.05f ||
            Math.Abs(Player.Read().Rac1YawVelocity) > 0.0001d ||
            Player.Read().Rac1AnalogueMagnitude > 0.0001d)
        {
            throw new InvalidOperationException(
            $"Veldin automatic restart did not clear motion: velocity={Player.Read().Velocity}, " +
            $"yawVelocity={Player.Read().Rac1YawVelocity:R}, analogue={Player.Read().Rac1AnalogueMagnitude:R}.");
        }

        Log(
            "[rac1-smoke] automatic authored Veldin restart PASS; Nanotech=4; " +
            "no development respawn input");
    }

}
