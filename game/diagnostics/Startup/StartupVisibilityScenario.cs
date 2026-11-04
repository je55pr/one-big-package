using OBP.Core;

namespace OBP.Game.Diagnostics;

public sealed class StartupVisibilityScenario(DiagnosticScenarioDefinition definition, IDiagnosticMenuFixture menus,
    IDiagnosticWorldObservations worlds, IDiagnosticNavigationActions navigation, Action<string> log) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = definition;
    public async Task RunAsync(DiagnosticRunContext run)
    {
        await run.WaitAsync(DiagnosticFramePhase.PostDraw);
        AssertNoPlaceholder("pre-world first draw");
        var entry = await navigation.NavigateAsync("rac1:LEVEL0", run.CancellationToken);
        if (!entry.Accepted) throw new InvalidOperationException(entry.Reason ?? "R&C1 Veldin world entry failed.");
        AssertReady();
        for (int i = 0; i < 8; i++)
        {
            await run.WaitAsync(DiagnosticFramePhase.PostDraw);
            AssertNoPlaceholder($"gameplay draw {i + 1}");
            AssertReady();
        }
        log("[rac1-startup-visibility-smoke] PASS: hidden startup -> valid Veldin world/player/camera with no placeholder frames");
    }
    private void AssertNoPlaceholder(string sample)
    {
        var state = menus.ReadStartupPresentation();
        if (state.VisiblePlaceholders.Length != 0)
            throw new InvalidOperationException($"{sample}: bootstrap placeholder WorldRoot/{state.VisiblePlaceholders[0]} is visible");
        if (state.FallbackVisible) throw new InvalidOperationException($"{sample}: debug player fallback visual is visible");
    }
    private void AssertReady()
    {
        var state = menus.ReadStartupPresentation();
        if (!menus.ReadNavigation().WorldActive || worlds.ReadWorld().Content?.SourceGame != ObpSourceGame.Rac1 || state.LevelId != 0)
            throw new InvalidOperationException("R&C1 Veldin world is not ready");
        if (!state.ScenePresent) throw new InvalidOperationException("R&C1 Veldin scene result is missing");
        if (!state.PlayerPresent) throw new InvalidOperationException("R&C1 Veldin player is missing");
        if (!state.NativeAvatarPresent) throw new InvalidOperationException("native Ratchet presentation is missing");
        if (!state.PlayerCameraActive) throw new InvalidOperationException("player camera is not the active gameplay camera");
    }
}
