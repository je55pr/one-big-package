using Godot;
using OBP.Godot;

namespace OneBigPackage;

public partial class OBPGame
{
    public void RunRac1StartupVisibilitySmokeFromBootstrap()
    {
        _ = RunRac1StartupVisibilitySmokeAsync();
    }

    private async System.Threading.Tasks.Task RunRac1StartupVisibilitySmokeAsync()
    {
        try
        {
            // Sample the first actual draw before entering Veldin. This is where the
            // old implicit smoke fixture could leak its cube into production startup.
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            AssertNoStartupPlaceholderVisible("pre-world first draw");

            OpenDestinationFromBootstrap("rac1:LEVEL0");
            AssertRac1StartupGameplayReady();

            // Sample several consecutive presented frames so a one-frame fallback
            // or deferred QueueFree race fails deterministically.
            for (int i = 0; i < 8; i++)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                AssertNoStartupPlaceholderVisible($"gameplay draw {i + 1}");
                AssertRac1StartupGameplayReady();
            }

            GD.Print("[rac1-startup-visibility-smoke] PASS: hidden startup -> valid Veldin world/player/camera with no placeholder frames");
            ApplicationLifecycle.RequestQuit(this, "rac1-startup-visibility-smoke-complete", 0);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[rac1-startup-visibility-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-startup-visibility-smoke-failed", 1);
        }
    }

    private void AssertNoStartupPlaceholderVisible(string sample)
    {
        foreach (string name in new[] { "SmokeCube", "Floor" })
        {
            if (_worldRoot.GetNodeOrNull<MeshInstance3D>(name) is { } mesh && mesh.IsVisibleInTree())
                throw new System.InvalidOperationException(
                    $"{sample}: bootstrap placeholder WorldRoot/{name} is visible");
        }

        if (_player?.VisualRoot.FallbackVisible == true)
            throw new System.InvalidOperationException(
                $"{sample}: debug player fallback visual is visible");
    }

    private void AssertRac1StartupGameplayReady()
    {
        if (_mode != Mode.World || _world is not { Game: "rac1", LevelId: 0 })
            throw new System.InvalidOperationException("R&C1 Veldin world is not ready");
        if (_sceneResult is null)
            throw new System.InvalidOperationException("R&C1 Veldin scene result is missing");
        if (_player is null || !IsInstanceValid(_player))
            throw new System.InvalidOperationException("R&C1 Veldin player is missing");
        if (_playerAvatarView is null || !IsInstanceValid(_playerAvatarView))
            throw new System.InvalidOperationException("native Ratchet presentation is missing");
        if (!ReferenceEquals(_activeCamera, _player.Camera) || !_player.Camera.Current)
            throw new System.InvalidOperationException("player camera is not the active gameplay camera");
    }
}
