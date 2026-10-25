using OBP.Core;
using Godot;

namespace OBP.Game;

public partial class OBPGame
{
    private int _fusionSmokeStage;
    private bool _fusionSmokeReloaded;
    private int _fusionSmokeSnapshots;

    /// <summary>
    /// Drive the ordinary retail-derived PlayerHost controller, never teleport
    /// Ratchet. Used only by --test-scene fusion-lab --capture-frame.
    /// </summary>
    private void StartFusionLabTraversalSmoke(PlayerHost player)
    {
        if (_args.CaptureFrame is null || _args.TestScene != "fusion-lab" || _fusionSmokeReloaded)
            return;

        _fusionSmokeStage = 0;
        _fusionSmokeSnapshots = 0;
        player.ScriptedInputProvider = _ =>
        {
            float z = player.Position.Z;
            if (_fusionSmokeStage == 0 && z > -5f)
            {
                _fusionSmokeStage = 1;
                GD.Print($"[fusion-smoke] entered hub at z={z:0.00}");
            }
            if (_fusionSmokeStage == 1 && z > 14f)
            {
                _fusionSmokeStage = 2;
                GD.Print($"[fusion-smoke] entered test chamber at z={z:0.00}; returning");
            }
            if (_fusionSmokeStage == 2 && z < -13f)
            {
                _fusionSmokeStage = 3;
                GD.Print($"[fusion-smoke] returned to entrance at z={z:0.00}; staging reload");
                CallDeferred(nameof(CompleteFusionLabTraversalSmoke));
            }

            // Backwards remains a real movement input. PlayerHost and the
            // authored static collision decide where the player can walk.
            return (_fusionSmokeStage switch
            {
                0 or 1 => new Vector2(0, -1),
                2 => new Vector2(0, 1),
                _ => Vector2.Zero,
            }, false);
        };
    }

    private void TickFusionLabTraversalSmoke()
    {
        if (_args.TestScene != "fusion-lab" || _args.CaptureFrame is null ||
            _fusionSmokeReloaded || _world?.Game != WorldContentIdentity.FusionLab || _player is null)
            return;
        if (_frame % 120 != 0) return;

        _fusionSmokeSnapshots++;
        GD.Print($"[fusion-smoke] stage={_fusionSmokeStage} x={_player.Position.X:0.00} z={_player.Position.Z:0.00} elapsed={_fusionSmokeSnapshots * 2}s");
    }

    private void CompleteFusionLabTraversalSmoke()
    {
        if (_fusionSmokeReloaded || _fusionSmokeStage != 3) return;
        _fusionSmokeReloaded = true;
        ShowDestinationSelector();
        if (EnterFusionLab() && _world?.Game == WorldContentIdentity.FusionLab && _player is not null)
            GD.Print("[fusion-smoke] PASS: entrance -> hub -> chamber -> entrance, unload and reload via normal world host");
        else
            GD.PrintErr("[fusion-smoke] FAIL: traversal succeeded, but re-entry after teardown failed");
    }
}
