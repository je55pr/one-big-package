using OBP.Core;

namespace OBP.Game.Diagnostics;

public sealed class OrdinaryUiScenario(DiagnosticScenarioDefinition definition, string? startupCase,
    IDiagnosticMenuFixture menus, IDiagnosticPlayerContinuity player, IDiagnosticProfileEvidence profile,
    IDiagnosticSourceRepairFixture repair, Action<string> log) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = definition;
    public async Task RunAsync(DiagnosticRunContext run)
    {
        await run.WaitAsync(DiagnosticFramePhase.Process, 8);
        var state = menus.ReadNavigation();
        Require(!state.DiagnosticSourceAccess, "Ordinary navigation must retain the complete trilogy requirement.");
        Require(!state.SourceManagerPresent, "Ready startup opened setup.");
        await CheckStartupAsync(run);
        await run.WaitAsync(DiagnosticFramePhase.Process, 3);
        state = menus.ReadNavigation();
        Require(state.SelectorActive, "Title did not open World Select.");
        VerifySurface(DiagnosticMenuSurface.WorldSelect);
        Require(state.EnabledButtonFocused, "World Select has no controller focus.");
        Require(menus.Back() && menus.ReadNavigation().TitleActive, "World Select Back did not return to the title.");
        Require(!menus.ReadNavigation().SourceManagerPresent, "World Select Back exposed setup.");
        menus.ShowWorldSelect();
        Require(menus.ReadNavigation().LabButtonPresent, "The playable Lab button is missing.");
        menus.PressLab();
        await run.WaitAsync(DiagnosticFramePhase.Process, 4);
        state = menus.ReadNavigation();
        Require(state.WorldLoaded && state.WorldActive, "The playable Lab did not load.");
        VerifySurface(DiagnosticMenuSurface.Overlay);
        VerifySurface(DiagnosticMenuSurface.World);
        Require(menus.OpenPause(), "The ordinary pause menu did not open.");
        try
        {
            await run.WaitAsync(DiagnosticFramePhase.Process);
            foreach (string section in new[] { "Weapons", "Gadgets", "Appearance", "Quick Select", "Settings" })
            {
                menus.ShowPauseSection(section);
                VerifySurface(DiagnosticMenuSurface.Pause);
            }
        }
        finally { Require(menus.ClosePause(), "The pause menu did not close."); }
        menus.ShowWorldSelect();
        var before = player.Capture();
        string? saved = profile.ReadDigest();
        Require(repair.Detach(ObpSourceGame.Rac3), "The repair test requires three configured games.");
        menus.ShowTitle();
        state = menus.ReadNavigation();
        Require(state.SourceManagerPresent && state.RepairActive && !state.TitlePresent, "Genuine source loss did not replace the title with repair.");
        var after = player.Capture();
        Require(before.Configuration == after.Configuration && before.LoadoutJson == after.LoadoutJson && before.Health == after.Health,
            "Repair reset the persistent player session.");
        Require(!profile.TrySave("ordinary UI repair check"), "Repair allowed a profile write.");
        if (saved is not null) Require(saved == profile.ReadDigest(), "Repair rewrote the saved game.");
        log("[ordinary-ui-smoke] PASS: title, resume, World Select/back, Lab, pause/focus and repair preserve ordinary UI and player state");
    }

    private async Task CheckStartupAsync(DiagnosticRunContext run)
    {
        Require(startupCase is "fresh" or "resume" or "invalid-save", "Choose an explicit ordinary UI case.");
        var state = menus.ReadNavigation();
        if (startupCase == "resume")
        {
            Require(state.WorldLoaded && state.SavedDestination == state.Destination, "The resume case did not auto-resume the saved destination.");
            menus.ShowWorldSelect(); return;
        }
        Require(!state.WorldLoaded && state.TitleActive && state.TitlePresent, "Fresh/recovery startup did not show the title.");
        if (startupCase == "invalid-save")
        {
            Require(state.ProfileWriteBlocked, "The invalid-save case did not block writes.");
            Require(state.SaveNotice is { } notice && notice.Contains("could not be opened", StringComparison.Ordinal) && notice.Contains("without saving", StringComparison.Ordinal),
                "Invalid-save recovery did not show the read-only notice.");
            Require(!profile.TrySave("ordinary UI invalid-save check"), "Invalid-save recovery allowed a write.");
        }
        else Require(!state.ProfileWriteBlocked, "Fresh startup unexpectedly blocked saving.");
        VerifySurface(DiagnosticMenuSurface.Title);
        menus.PressTitleStart();
        for (int frame = 0; frame < 120 && !menus.ReadNavigation().SelectorActive; frame++)
            await run.WaitAsync(DiagnosticFramePhase.Process);
    }
    private void VerifySurface(DiagnosticMenuSurface surface) => OrdinaryUiSurfacePolicy.Verify(menus.ReadVisibleText(surface));
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
}
