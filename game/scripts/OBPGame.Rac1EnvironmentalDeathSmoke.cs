using Godot;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;

namespace OneBigPackage;

public partial class OBPGame
{
    private async Task RunRac1NaturalVeldinFallRespawnSmokeAsync(Vector3 authoredRespawnPosition)
    {
        if (_world is not { Game: "rac1", LevelId: 0, Environment: { } environment } || _player is null)
            throw new InvalidOperationException("Natural Veldin fall smoke requires a live LEVEL0 player.");

        if (Math.Abs(environment.DeathHeight - 27f) > 0.0001f)
            throw new InvalidOperationException(
                $"Veldin death height drifted to {environment.DeathHeight:R}, expected 27.");

        double groundedSeparation = MeasureRac1ContactSeparation();
        if (groundedSeparation > Rac1RatchetNanotechSession.RetailVeldinDeathContactSeparationExclusive)
            throw new InvalidOperationException(
                $"Authored Veldin start was not in floor contact: separation={groundedSeparation:R}.");

        Vector3 runStart = _player.GlobalPosition;
        long deathGenerationBefore = _rac1EnvironmentalDeathGeneration;
        long automaticRestartGenerationBefore = _rac1AutomaticEnvironmentalRestartGeneration;
        bool leftFloor = false;
        try
        {
            SetAnalogueSmokeInput(0f, 1f);
            for (int frame = 0;
                 frame < 900 && _rac1EnvironmentalDeathGeneration == deathGenerationBefore;
                 frame++)
            {
                await PhysicsFramesAsync(1);
                leftFloor |= !_player.IsOnFloor();
            }
        }
        finally
        {
            ClearMovementSmokeInput();
        }

        if (_rac1EnvironmentalDeathGeneration != deathGenerationBefore + 1 ||
            _rac1LastEnvironmentalDeathBoundary is not { } dead)
            throw new TimeoutException(
                "Ordinary full-forward Veldin run never reached the environmental-death boundary.");
        if (!leftFloor)
            throw new InvalidOperationException(
                "Veldin environmental death occurred without leaving imported collision.");

        Vector3 deathPosition = _rac1LastEnvironmentalDeathPosition;
        double deathSeparation = _rac1LastEnvironmentalDeathContactSeparation;
        float horizontalTravel = HorizontalDistance(runStart, deathPosition);
        if (horizontalTravel < 8f)
            throw new InvalidOperationException(
                $"Veldin death route travelled only {horizontalTravel:R}; " +
                "expected an ordinary run across reachable imported collision before the fall.");
        if (deathPosition.Y >= environment.DeathHeight)
            throw new InvalidOperationException(
                $"Veldin death triggered above the recovered plane: " +
                $"y={deathPosition.Y:R}, death={environment.DeathHeight:R}.");
        if (!(deathSeparation > Rac1RatchetNanotechSession.RetailVeldinDeathContactSeparationExclusive))
            throw new InvalidOperationException(
                $"Veldin death triggered without recovered contact separation: {deathSeparation:R}.");

        if (dead.Nanotech != 0 ||
            dead.NativePlayerState != Rac1RatchetNanotechSession.RetailVeldinDeathNativeState ||
            dead.NativeSequence != Rac1RatchetNanotechSession.RetailVeldinDeathNativeSequence ||
            dead.NativeSequenceFrame != Rac1RatchetNanotechSession.RetailVeldinDeathNativeSequenceFrame)
            throw new InvalidOperationException(
                "Natural Veldin fall did not enter state 0x77 / sequence 11 frame 0.");

        if (CurrentPlayerAvatarSourceSequence() is
            Rac1RatchetSequenceSelection.EnvironmentalDeathStartSequenceId or
            Rac1RatchetSequenceSelection.EnvironmentalDeathTerminalSequenceId)
            throw new InvalidOperationException(
                "Godot player avatar invented unrecovered environmental-death playback.");
        if (!PlayerAvatarPresentationIsSynchronized())
            throw new InvalidOperationException(
                "Player-avatar presentation desynchronized at the death boundary.");

        GD.Print(
            $"[rac1-smoke] ordinary Veldin fall PASS; travel={horizontalTravel:0.000} " +
            $"y={deathPosition.Y:0.000} separation={deathSeparation:0.000} " +
            "state=0x77 sequence=11 frame=0 Nanotech=0");

        long deathGeneration = _rac1EnvironmentalDeathGeneration;
        await Rac1SmokeWaitAsync(
            () => _rac1AutomaticEnvironmentalRestartGeneration == deathGeneration &&
                  !_rac1Nanotech.Probe().IsDead &&
                  _player.IsOnFloor(),
            240,
            "automatic authored Veldin environmental restart");
        if (_rac1AutomaticEnvironmentalRestartGeneration <= automaticRestartGenerationBefore)
            throw new InvalidOperationException(
                "Veldin restart completed without the automatic environmental-restart path.");

        await Rac1SmokeWaitAsync(
            () => CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.StandingSequenceId &&
                  PlayerAvatarPresentationIsSynchronized() &&
                  _player.HasActiveRecoveredCamera,
            120,
            "automatic respawn standing player/camera presentation");

        var respawn = _rac1Nanotech.Probe();
        if (respawn.Nanotech != 4 || !_player.Rac1GameplayAlive)
            throw new InvalidOperationException(
                "Veldin automatic restart did not restore four Nanotech/alive state.");
        if (_player.GlobalPosition.DistanceTo(authoredRespawnPosition) > 0.2f)
            throw new InvalidOperationException(
                $"Veldin automatic restart missed authored start: " +
                $"{_player.GlobalPosition} vs {authoredRespawnPosition}.");

        var checkpoint = _rac1CampaignSession.LevelCheckpoint
            ?? throw new InvalidOperationException("Veldin automatic restart lost its checkpoint session.");
        if (Math.Abs(_player.Rac1CurrentYaw - checkpoint.AuthoredClass0.Yaw) > 0.0001d)
            throw new InvalidOperationException(
                $"Veldin automatic restart yaw {_player.Rac1CurrentYaw:R} " +
                $"did not restore authored {checkpoint.AuthoredClass0.Yaw:R}.");
        if (_player.Velocity.Length() > 0.05f ||
            Math.Abs(_player.Rac1YawVelocity) > 0.0001d ||
            _player.Rac1AnalogueMagnitude > 0.0001d)
            throw new InvalidOperationException(
                $"Veldin automatic restart did not clear motion: velocity={_player.Velocity}, " +
                $"yawVelocity={_player.Rac1YawVelocity:R}, analogue={_player.Rac1AnalogueMagnitude:R}.");

        GD.Print(
            "[rac1-smoke] automatic authored Veldin restart PASS; Nanotech=4; " +
            "no development respawn input");
    }
}
