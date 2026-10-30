using OBP.RAC1.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    // Keep player callbacks bound to the scoped world, not persistent root state.
    private void ResetRac1WrenchHost()
    {
        // Avatar and cross-source teardown can happen after the R&C1 lease retires.
        if (_worldScenes?.Current?.Resources.Rac1Gameplay is { } gameplay) gameplay.Wrench.Reset();
        else
        {
            GodotRac1PlayerAudioPlayback.ResetWrench(_playerAudio);
            if (_player is not null) _player.Rac1WrenchThrown = false;
        }
    }

    private void OnRac1WrenchPhysicsUpdated(Rac1WrenchAttackSession.Frame frame)
        => _worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.OnPhysicsUpdated(frame);
}
