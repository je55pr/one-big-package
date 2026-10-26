using System.Numerics;
using OBP.RAC1.Presentation;

namespace OBP.Game.Diagnostics;

public sealed class Rac1VeldinRetailPauseScenario(DiagnosticScenarioDefinition definition,
    IDiagnosticRetailPauseFixture fixture, Action<string> log) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = definition;
    public async Task RunAsync(DiagnosticRunContext run)
    {
        var before = fixture.Read();
        PauseScenarioChecks.Require(before.AssetsReady && before.PlayerPresent, "Veldin retail pause requires the attached R&C1 disc and live player.");
        PauseScenarioChecks.Require(fixture.Open(), "Ordinary Veldin gameplay did not open the source-backed pause menu.");
        try
        {
            var opened = fixture.Read();
            PauseScenarioChecks.Require(opened.RetailEnabled && opened.TreePaused, "Ordinary Veldin gameplay did not open the source-backed pause menu.");
            await run.WaitAsync(DiagnosticFramePhase.Process, 6);
            var state = fixture.Read();
            PauseScenarioChecks.Require(state.Panels.Length == 6 && state.Panels.All(panel => panel.RetailPauseRoot && panel.SurfaceCount == 1),
                "Real Veldin pause lost its six native projected and shaded panels.");
            PauseScenarioChecks.Require(state.FocusId == state.Panels[0].Id, "Real Veldin pause focused a stale menu item instead of Weapons.");
            if (!state.Headless) PauseScenarioChecks.VerifyPixels(fixture.CapturePanels("rac1-ui-veldin", "root.png"), requireFullBounds: false);
            PauseScenarioChecks.Require(Vector3.Distance(state.PlayerPosition, before.PlayerPosition) <= .001f, "Real gameplay advanced player position during pause.");
            log("[rac1-veldin-pause] PASS: six source-projected opaque panels over live Veldin; opening focus and paused world retained");
        }
        finally { fixture.Close(); }
        await run.WaitAsync(DiagnosticFramePhase.Process, 3);
        var restored = fixture.Read();
        PauseScenarioChecks.Require(!restored.TreePaused && !restored.PauseOpen, "Real Veldin gameplay did not resume after closing retail pause.");
    }
}
