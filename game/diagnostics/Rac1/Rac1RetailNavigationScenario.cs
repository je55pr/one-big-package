using System.Numerics;
using OBP.RAC1.Presentation;

namespace OBP.Game.Diagnostics;

/// <summary>Real paused-world controller navigation, preserving native elapsed transition ticks and all gameplay state.</summary>
public sealed class Rac1RetailNavigationScenario(DiagnosticScenarioDefinition definition, string world,
    IDiagnosticRetailPauseFixture fixture, Action<string> log) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = definition;
    public async Task RunAsync(DiagnosticRunContext run)
    {
        var before = fixture.Read();
        Require(before.PlayerPresent && before.Rac1World && before.AssetsReady, "pause review needs a live R&C1 world and native UI assets.");
        using (fixture.BeginInputReview())
        {
            Require(fixture.Open(), "ordinary gameplay failed to open retail pause.");
            var state = fixture.Read();
            Require(state.TreePaused && state.RetailEnabled, "ordinary gameplay failed to open retail pause.");
            await run.WaitAsync(DiagnosticFramePhase.Process, 10);
            state = fixture.Read();
            Require(state.Panels.Length == 6 && state.Panels.All(panel => panel.Visible && panel.Bounds.Width >= state.ViewportWidth * .25f && panel.SurfaceCount == 1) &&
                state.FocusId == state.Panels[0].Id, "root panels or opening focus invalid: " + state);
            string[] rootIds = state.Panels.Select(panel => panel.Id).ToArray();
            Require(state.Headless || !state.ControllerWasLastInput || state.PointerMode == "Hidden", "controller modality lost hidden pointer: " + state.PointerMode);
            try
            {
                fixture.InjectAction("ui_down", pressed: true, admitted: false);
                await run.WaitAsync(DiagnosticFramePhase.Process, 2);
            }
            finally { fixture.InjectAction("ui_down", pressed: false, admitted: false); }
            await run.WaitAsync(DiagnosticFramePhase.Process, 2);
            Require(fixture.Read().FocusId == rootIds[0], "unsolicited physical-equivalent input escaped the review guard.");
            if (!state.Headless) PauseScenarioChecks.VerifyPixels(fixture.CapturePanels("rac1-ui-" + world.ToLowerInvariant(), "root-review.png"), requireFullBounds: true);
            await Tap(run, "ui_down");
            Require(fixture.Read().FocusId == rootIds[1], "controller Down did not select Gadgets.");
            await Tap(run, "ui_up");
            Require(fixture.Read().FocusId == rootIds[0], "controller Up did not restore Weapons.");
            await Tap(run, "ui_accept");
            await Page(run, Rac1RetailMenuPage.Weapons);
            await Tap(run, "ui_cancel");
            await Page(run, Rac1RetailMenuPage.Root);
            for (int i = 0; i < 3; i++) await Tap(run, "ui_down");
            Require(fixture.Read().FocusText == "Items", "root D-pad did not select Items.");
            await Tap(run, "ui_accept");
            await Page(run, Rac1RetailMenuPage.Items);
            state = fixture.Read();
            Require(state.ItemCells.Length == 9 && state.FocusId == state.ItemCells[0], "native Items 3x3 or focus missing.");
            string middleCell = state.ItemCells[4];
            await Tap(run, "ui_right");
            await Tap(run, "ui_down");
            Require(fixture.Read().FocusId == middleCell, "Items controller moved to wrong grid cell.");
            await Tap(run, "ui_accept");
            await Tap(run, "ui_cancel");
            await Page(run, Rac1RetailMenuPage.Root);
            state = fixture.Read();
            Require(Vector3.Distance(state.PlayerPosition, before.PlayerPosition) <= .001f && state.HeldItem == before.HeldItem &&
                state.QuickSelect.SequenceEqual(before.QuickSelect) && state.Ammo.SequenceEqual(before.Ammo), "menu navigation mutated paused gameplay.");
            await Tap(run, "ui_cancel");
            await run.WaitAsync(DiagnosticFramePhase.Process, 3);
            state = fixture.Read();
            Require(!state.PauseOpen && !state.TreePaused && state.PointerMode == before.PointerMode, "Circle did not restore gameplay/input mode.");
            Require(fixture.Open() && fixture.Read().RetailEnabled, "pause failed to reopen after normal Circle exit.");
            await run.WaitAsync(DiagnosticFramePhase.Process, 4);
            state = fixture.Read();
            Require(state.Page == Rac1RetailMenuPage.Root && state.FocusText == "Weapons", "reopening retained stale page/focus.");
            log($"[rac1-ui-world-review] {world} PASS: projected panels, controller focus, Cross/Circle, Items, saved state, mouse mode, reopen");
        }
        await run.WaitAsync(DiagnosticFramePhase.Process, 3);
        var restored = fixture.Read();
        Require(!restored.TreePaused && !restored.PauseOpen, "world not restored after pause review.");
    }
    private async Task Page(DiagnosticRunContext run, Rac1RetailMenuPage page)
    {
        // Original native transitions use 12 elapsed 60-Hz ticks. Do not substitute a fixed render-frame delay.
        for (int i = 0; i < 600 && fixture.Read().Page != page; i++) await run.WaitAsync(DiagnosticFramePhase.Process);
        Require(fixture.Read().Page == page, $"page transition to {page} did not complete.");
        await run.WaitAsync(DiagnosticFramePhase.Process, 3);
    }
    private async Task Tap(DiagnosticRunContext run, string action)
    {
        try
        {
            fixture.InjectAction(action, pressed: true, admitted: true);
            await run.WaitAsync(DiagnosticFramePhase.Process, action is "ui_up" or "ui_down" or "ui_left" or "ui_right" ? 1 : 3);
        }
        finally { fixture.InjectAction(action, pressed: false, admitted: true); }
        await run.WaitAsync(DiagnosticFramePhase.Process, 3);
    }
    private void Require(bool condition, string detail) => PauseScenarioChecks.Require(condition, world + " " + detail);
}

internal static class PauseScenarioChecks
{
    internal static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    internal static void VerifyPixels(DiagnosticPanelCapture capture, bool requireFullBounds)
    {
        foreach (var panel in capture.Panels)
        {
            var bounds = panel.Bounds;
            Require(panel.SampleX >= 0 && panel.SampleY >= 0 && panel.SampleX < capture.Width && panel.SampleY < capture.Height,
                "Native panel " + panel.Id + " projected outside the viewport.");
            Require(!requireFullBounds || bounds.X >= 0 && bounds.Y >= 0 && bounds.X + bounds.Width <= capture.Width && bounds.Y + bounds.Height <= capture.Height,
                "Source panel extends beyond visible screen.");
            Require(float.IsFinite(panel.R) && float.IsFinite(panel.G) && float.IsFinite(panel.B) && panel.R < .26f && panel.G < .26f && panel.B < .30f,
                "Native panel " + panel.Id + " lost opaque backing.");
        }
    }
}
