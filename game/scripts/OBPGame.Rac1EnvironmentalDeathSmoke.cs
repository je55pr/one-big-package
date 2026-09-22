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
        bool leftFloor = false;
        try
        {
            SetAnalogueSmokeInput(0f, 1f);
            for (int frame = 0; frame < 900 && !_rac1Nanotech.Probe().IsDead; frame++)
            {
                await PhysicsFramesAsync(1);
                leftFloor |= !_player.IsOnFloor();
            }
        }
        finally
        {
            ClearMovementSmokeInput();
        }

        var dead = _rac1Nanotech.Probe();
        if (!dead.IsDead)
            throw new TimeoutException(
                "Ordinary full-forward Veldin run never reached the environmental-death boundary.");
        if (!leftFloor)
            throw new InvalidOperationException(
                "Veldin environmental death occurred without leaving imported collision.");

        float horizontalTravel = HorizontalDistance(runStart, _player.GlobalPosition);
        if (horizontalTravel < 8f)
            throw new InvalidOperationException(
                $"Veldin death route travelled only {horizontalTravel:R}; " +
                "expected an ordinary run across reachable imported collision before the fall.");
        if (_player.GlobalPosition.Y >= environment.DeathHeight)
            throw new InvalidOperationException(
                $"Veldin death triggered above the recovered plane: " +
                $"y={_player.GlobalPosition.Y:R}, death={environment.DeathHeight:R}.");
        double deathSeparation = MeasureRac1ContactSeparation();
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
            $"y={_player.GlobalPosition.Y:0.000} separation={deathSeparation:0.000} " +
            "state=0x77 sequence=11 frame=0 Nanotech=0");
        // Drive the same ordinary R-key input boundary used by local play. The
        // smoke must not call the respawn consequence handler directly.
        await TapPhysicalKeyAsync(Key.R);
        await Rac1SmokeWaitAsync(
            () => !_rac1Nanotech.Probe().IsDead && _player.IsOnFloor(),
            240,
            "authored Veldin respawn from ordinary input");
        await Rac1SmokeWaitAsync(
            () => CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.StandingSequenceId &&
                  PlayerAvatarPresentationIsSynchronized(),
            120,
            "respawn standing player-avatar presentation");

        var respawn = _rac1Nanotech.Probe();
        if (respawn.Nanotech != 4 || !_player.Rac1GameplayAlive)
            throw new InvalidOperationException(
                "Veldin respawn did not restore four Nanotech/alive state.");
        if (_player.GlobalPosition.DistanceTo(authoredRespawnPosition) > 0.2f)
            throw new InvalidOperationException(
                $"Veldin respawn missed authored start: " +
                $"{_player.GlobalPosition} vs {authoredRespawnPosition}.");

        GD.Print(
            "[rac1-smoke] authored Veldin checkpoint-session respawn PASS; Nanotech=4");
    }
}
