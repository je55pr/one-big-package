using OBP.Core;

namespace OBP.Game.Diagnostics;

/// <summary>Ordinary readiness under test. Detaches a library entry, never changes disc files or grants diagnostic access.</summary>
public sealed class SourceSetupScenario(DiagnosticScenarioDefinition definition, IDiagnosticMenuFixture menus,
    IDiagnosticPlayerContinuity player, IDiagnosticProfileEvidence profile, IDiagnosticSourceRepairFixture repair,
    Action<string> log) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = definition;
    public async Task RunAsync(DiagnosticRunContext run)
    {
        await run.WaitAsync(DiagnosticFramePhase.Physics);
        var state = menus.ReadNavigation();
        Require(!state.DiagnosticSourceAccess && state.WorldLoaded, "The source-loss check requires a loaded ordinary world.");
        Require(profile.HasExplicitPath, "The source-loss check requires --profile.");
        var before = player.Capture();
        string saved = profile.ReadDigest() ?? throw new InvalidOperationException("The source-loss check requires an existing profile.");
        int level = repair.ReadCampaignTravel().Level;
        Require(repair.StageCampaignTravel(level == 0 ? 1 : 0), "Could not stage the source-campaign transfer.");
        Require(repair.Detach(ObpSourceGame.Rac2), "The source-loss check requires the complete trilogy at entry.");
        Require(!repair.RequireReady("source-loss check"), "A detached required source did not block play.");
        state = menus.ReadNavigation();
        Require(!state.WorldLoaded && state.RepairActive, "World resources were not retired for repair.");
        var campaign = repair.ReadCampaignTravel();
        Require(!campaign.TransitionActive && campaign.Level == level, "Repair retained or committed an interrupted source-campaign transfer.");
        Require(before == player.Capture(), "Source loss reset player-global state.");
        Require(!profile.TrySave("source-loss check") && saved == profile.ReadDigest(), "Repair flow rewrote the persistent profile.");
        log("[source-setup-check] PASS: required source detachment retires the world, retains player state/profile and requests exit 2");
    }
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
}
