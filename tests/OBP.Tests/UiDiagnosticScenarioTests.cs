using System.Collections.Immutable;
using OBP.Core;
using OBP.Game;
using OBP.Game.Diagnostics;
using OBP.Godot;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class UiDiagnosticScenarioTests
{
    private static DiagnosticScenarioDefinition Definition => new("ui-fixture/v1", DiagnosticSourceAccess.AssetFree,
        DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromSeconds(10), 7200);
    private sealed class Sources : IDiagnosticSources { public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => []; }
    private sealed class Clock : IDiagnosticFrameClock
    {
        public List<DiagnosticFramePhase> Phases { get; } = [];
        public Action? Tick { get; set; }
        public bool RenderAvailable { get; set; } = true;
        public bool Async { get; set; }
        public DiagnosticFrameSnapshot ReadFrames() => new((ulong)Phases.Count, 0);
        public async Task NextAsync(DiagnosticFramePhase phase, CancellationToken cancellationToken)
        {
            Phases.Add(phase); Tick?.Invoke(); cancellationToken.ThrowIfCancellationRequested();
            if (phase == DiagnosticFramePhase.PostDraw && !RenderAvailable) throw new DiagnosticUnavailableException("Post-draw unavailable headlessly.");
            if (Async) await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
    private sealed class Player : IDiagnosticPlayerContinuity
    {
        public DiagnosticPlayerContinuity State { get; set; } = new(new(new(ObpSourceGame.Rac1, "fixture", "ratchet", "native-model"),
            new(ObpSourceGame.Rac1, "fixture", "movement"), new(ObpSourceGame.Rac1, "fixture", "animation"),
            PlayerHostProfiles.Services, 60), new(3, 4, true), "loadout-with-ammo", "all-currencies");
        public DiagnosticPlayerContinuity Capture() => State;
    }
    private sealed class Profile : IDiagnosticProfileEvidence
    {
        public bool HasExplicitPath { get; set; } = true;
        public string? Digest { get; set; } = "retained-save-digest";
        public bool AllowWrite { get; set; }
        public string? ReadDigest() => Digest;
        public bool TrySave(string reason) => AllowWrite;
    }
    private sealed class Menus : IDiagnosticMenuFixture
    {
        public DiagnosticNavigationSnapshot State { get; set; } = new(true, false, false, false, false, false, true, false,
            null, null, false, null, true, true);
        public DiagnosticStartupPresentation Presentation { get; set; } = new([], false, true, true, true, true, 0);
        public List<string> Sections { get; } = [];
        public ImmutableArray<string> Text { get; set; } = ["Play", "World Select", "Continue"];
        public bool LostSource { get; set; }
        public bool ReturnTitleOnBack { get; set; } = true;
        public bool PauseOpen { get; private set; }
        public bool CloseSucceeded { get; set; } = true;
        public bool FailPauseSection { get; set; }
        public Action? Repair { get; set; }
        public DiagnosticNavigationSnapshot ReadNavigation() => State;
        public DiagnosticStartupPresentation ReadStartupPresentation() => Presentation;
        public ImmutableArray<string> ReadVisibleText(DiagnosticMenuSurface surface) => Text;
        public void ShowTitle() { if (LostSource) Repair!(); else State = State with { TitleActive = true, SelectorActive = false }; }
        public void ShowWorldSelect() => State = State with { TitleActive = false, SelectorActive = true };
        public bool Back() { State = State with { TitleActive = ReturnTitleOnBack, SelectorActive = false }; return true; }
        public void PressTitleStart() => ShowWorldSelect();
        public void PressLab() => State = State with { SelectorActive = false, WorldActive = true, WorldLoaded = true, Destination = "obp:fusion-lab" };
        public bool OpenPause() { PauseOpen = true; return true; }
        public bool ClosePause() { PauseOpen = false; return CloseSucceeded; }
        public void ShowPauseSection(string section)
        {
            if (FailPauseSection) throw new InvalidOperationException("original pause section failure");
            Sections.Add(section);
        }
    }
    private sealed class RepairFixture(Menus menus) : IDiagnosticSourceRepairFixture
    {
        public DiagnosticCampaignTravel Travel { get; set; } = new(0, false);
        public bool KeepInterruptedTravel { get; set; }
        public Action? DuringRepair { get; set; }
        public int Detachments { get; private set; }
        public bool Detach(ObpSourceGame game) { menus.LostSource = true; Detachments++; return true; }
        public bool RequireReady(string reason)
        {
            menus.State = menus.State with { WorldLoaded = false, WorldActive = false, TitleActive = false, TitlePresent = false, SourceManagerPresent = true, RepairActive = true };
            if (!KeepInterruptedTravel) Travel = Travel with { TransitionActive = false };
            DuringRepair?.Invoke();
            ApplicationQuitDeferral.TryHandle("trilogy-setup-required", 2);
            return false;
        }
        public DiagnosticCampaignTravel ReadCampaignTravel() => Travel;
        public bool StageCampaignTravel(int destination) { Travel = Travel with { TransitionActive = true }; return true; }
    }
    private sealed class Worlds : IDiagnosticWorldObservations
    {
        public DiagnosticWorldSnapshot ReadWorld() => new("rac1:LEVEL0", ObpSourceGame.Rac1, "fixture", 1);
    }
    private sealed class Navigation(Menus menus) : IDiagnosticNavigationActions
    {
        public Task<DiagnosticActionResult> NavigateAsync(string destinationId, CancellationToken cancellationToken)
        { menus.State = menus.State with { WorldActive = true, WorldLoaded = true }; return Task.FromResult(new DiagnosticActionResult(true, null)); }
    }
    private sealed class Scenario(Func<DiagnosticRunContext, Task> execute) : IDiagnosticScenario
    {
        public DiagnosticScenarioDefinition Definition => UiDiagnosticScenarioTests.Definition;
        public Task RunAsync(DiagnosticRunContext run) => execute(run);
    }
    private sealed class FailingCleanup : IDisposable { public void Dispose() => throw new IOException("fixture cleanup failed"); }
    private static DiagnosticProcessScenario Process(IDiagnosticScenario scenario, int code = 2) => new(scenario, new("success", code), new("ui-failed", 1), requirePolicyExit: code == 2);

    [Theory]
    [InlineData("fresh")]
    [InlineData("resume")]
    [InlineData("invalid-save")]
    public async Task OrdinaryCasesPreserveNavigationFocusSectionsAndRepairExit(string startupCase)
    {
        var menus = new Menus(); var repair = new RepairFixture(menus); var profile = new Profile(); var clock = new Clock();
        menus.Repair = () => repair.RequireReady("title");
        if (startupCase == "resume") menus.State = menus.State with { WorldLoaded = true, Destination = "saved-world", SavedDestination = "saved-world" };
        if (startupCase == "invalid-save") menus.State = menus.State with { ProfileWriteBlocked = true, SaveNotice = "Your saved game could not be opened. Continue without saving." };
        var scenario = Process(new OrdinaryUiScenario(Definition, startupCase, menus, new Player(), profile, repair, _ => { }));
        var result = await new DiagnosticScenarioRunner(new Sources(), clock).RunAsync(scenario, true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Passed, result.Status);
        Assert.Equal(new DiagnosticExit("trilogy-setup-required", 2), scenario.Exit);
        Assert.Equal(new[] { "Weapons", "Gadgets", "Appearance", "Quick Select", "Settings" }, menus.Sections);
        Assert.Equal(1, repair.Detachments); Assert.False(menus.PauseOpen);
        Assert.All(clock.Phases, phase => Assert.Equal(DiagnosticFramePhase.Process, phase));
    }

    [Theory]
    [InlineData("focus", "controller focus")]
    [InlineData("back", "return to the title")]
    [InlineData("provenance", "provenance leaked")]
    [InlineData("preparation", "provenance leaked")]
    [InlineData("save", "profile write")]
    [InlineData("session", "player session")]
    [InlineData("currency", "player session")]
    public async Task OrdinaryFailuresOverrideTheExpectedRepairExit(string fault, string detail)
    {
        var menus = new Menus(); var repair = new RepairFixture(menus); var player = new Player(); var profile = new Profile();
        menus.Repair = () => repair.RequireReady("title");
        if (fault == "focus") menus.State = menus.State with { EnabledButtonFocused = false };
        if (fault == "back") menus.ReturnTitleOnBack = false;
        if (fault == "provenance") menus.Text = ["Attach source adapter"];
        if (fault == "preparation") menus.Text = ["SHARED EQUIPMENT  ·  Preparing trilogy data"];
        if (fault == "save") profile.AllowWrite = true;
        if (fault == "session") repair.DuringRepair = () => player.State = player.State with { LoadoutJson = "reset" };
        if (fault == "currency") repair.DuringRepair = () => player.State = player.State with { CurrenciesJson = "reset" };
        var scenario = Process(new OrdinaryUiScenario(Definition, "fresh", menus, player, profile, repair, _ => { }));
        var result = await new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(scenario, true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status); Assert.Contains(detail, result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new DiagnosticExit("ui-failed", 1), scenario.Exit); Assert.False(menus.PauseOpen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceRepairChecksUncommittedCampaignAndAllPersistentState(bool keepTravel)
    {
        var menus = new Menus(); menus.State = menus.State with { WorldLoaded = true, WorldActive = true };
        var repair = new RepairFixture(menus) { KeepInterruptedTravel = keepTravel };
        var scenario = Process(new SourceSetupScenario(Definition, menus, new Player(), new Profile(), repair, _ => { }));
        var clock = new Clock();
        var result = await new DiagnosticScenarioRunner(new Sources(), clock).RunAsync(scenario, true, CancellationToken.None);
        Assert.Equal(keepTravel ? DiagnosticRunStatus.Failed : DiagnosticRunStatus.Passed, result.Status);
        Assert.Equal(keepTravel ? 1 : 2, scenario.Exit!.Code);
        Assert.Equal(DiagnosticFramePhase.Physics, Assert.Single(clock.Phases));
        Assert.Equal(0, repair.Travel.Level);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupUsesActualPresentedFramesAndReportsHeadlessUnavailable(bool rendered)
    {
        var menus = new Menus(); var clock = new Clock { RenderAvailable = rendered };
        var scenario = new StartupVisibilityScenario(Definition, menus, new Worlds(), new Navigation(menus), _ => { });
        var result = await new DiagnosticScenarioRunner(new Sources(), clock).RunAsync(scenario, true, CancellationToken.None);
        Assert.Equal(rendered ? DiagnosticRunStatus.Passed : DiagnosticRunStatus.Unavailable, result.Status);
        Assert.Equal(rendered ? 9 : 1, clock.Phases.Count);
        Assert.All(clock.Phases, phase => Assert.Equal(DiagnosticFramePhase.PostDraw, phase));
    }

    [Fact]
    public async Task StartupRejectsOneFramePlaceholderInsteadOfPassingLaterFrames()
    {
        var menus = new Menus(); var clock = new Clock();
        clock.Tick = () => { if (clock.Phases.Count == 4) menus.Presentation = menus.Presentation with { VisiblePlaceholders = ["SmokeCube"] }; };
        var result = await new DiagnosticScenarioRunner(new Sources(), clock).RunAsync(
            new StartupVisibilityScenario(Definition, menus, new Worlds(), new Navigation(menus), _ => { }), true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status); Assert.Contains("SmokeCube", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypedRepairDispatchCommitsExitAfterAssertionsAndNeverMistakesItForFailure()
    {
        using var operations = new HostOperationScope(); var menus = new Menus();
        menus.State = menus.State with { WorldLoaded = true };
        var scenario = Process(new SourceSetupScenario(Definition, menus, new Player(), new Profile(), new RepairFixture(menus), _ => { }));
        var quit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        DiagnosticScenarioDispatch.Start([scenario], operations, new(new Sources(), new Clock { Async = true }), (_, code) => quit.TrySetResult(code),
            message => quit.TrySetException(new InvalidOperationException("Unexpected repair failure: " + message)));
        Assert.Equal(2, await quit.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await operations.StopAsync();
    }

    [Fact]
    public async Task CleanupFailureCannotCommitAnExpectedRepairExit()
    {
        using var operations = new HostOperationScope();
        var scenario = Process(new Scenario(async run =>
        {
            await run.WaitAsync(DiagnosticFramePhase.Process);
            run.Own(new FailingCleanup());
            Assert.True(ApplicationQuitDeferral.TryHandle("trilogy-setup-required", 2));
        }));
        var quit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        DiagnosticScenarioDispatch.Start([scenario], operations, new(new Sources(), new Clock { Async = true }), (_, code) => quit.TrySetResult(code), _ => { });
        Assert.Equal(1, await quit.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await operations.StopAsync();
    }

    [Theory]
    [InlineData(true, true, "requires a loaded ordinary world")]
    [InlineData(false, false, "requires --profile")]
    public async Task SourceRepairRequiresOrdinaryAccessAndAPersistentProfile(bool diagnosticAccess, bool hasExplicitPath, string detail)
    {
        var menus = new Menus(); menus.State = menus.State with { WorldLoaded = true, DiagnosticSourceAccess = diagnosticAccess };
        var repair = new RepairFixture(menus);
        var result = await new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(
            new SourceSetupScenario(Definition, menus, new Player(), new Profile { HasExplicitPath = hasExplicitPath }, repair, _ => { }), true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status);
        Assert.Contains(detail, result.Detail, StringComparison.Ordinal);
        Assert.Equal(0, repair.Detachments);
    }

    [Fact]
    public void UiRegistryRetainsAllThreeAliasesAndTwoNestedChecks()
    {
        Assert.Equal(5, UiDiagnosticHarness.Registry.Registrations.Length);
        var aliased = UiDiagnosticHarness.Registry.Registrations.Where(entry => entry.Aliases.Length > 0).ToArray();
        Assert.Equal(3, aliased.Length);
        foreach (var registration in aliased)
        {
            var tokens = registration.Id == "ordinary-ui-smoke" ? new[] { registration.Aliases[0], "fresh" } : [registration.Aliases[0]];
            Assert.True(registration.Requested(CommandLineArgs.Parse(tokens)));
            Assert.False(registration.Requested(new()));
        }
        foreach (string file in new[] { "OrdinaryUiSmoke", "SourceSetupCheck", "Rac1StartupVisibilitySmoke", "Rac1VeldinRetailPauseSmoke", "Rac1WorldRetailReviewSmoke" })
            Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game", "diagnostics", "OBPGame." + file + ".cs")));
    }

    [Fact]
    public async Task AnUnsuccessfulCloseCannotReplaceTheOriginalSectionFailure()
    {
        var menus = new Menus { CloseSucceeded = false, FailPauseSection = true };
        var result = await new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(
            new OrdinaryUiScenario(Definition, "fresh", menus, new Player(), new Profile(), new RepairFixture(menus), _ => { }), true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status);
        Assert.Contains("original pause section failure", result.Detail, StringComparison.Ordinal);
        Assert.False(menus.PauseOpen);
    }

    [Fact]
    public async Task DispatchDoesNotStartOrRequestAnotherQuitAfterHostStop()
    {
        using var operations = new HostOperationScope(); await operations.StopAsync();
        bool entered = false, quit = false;
        var scenario = Process(new Scenario(_ => { entered = true; return Task.CompletedTask; }));
        DiagnosticScenarioDispatch.Start([scenario], operations, new(new Sources(), new Clock()), (_, _) => quit = true, _ => Assert.Fail("Stopped work logged a failure"));
        Assert.False(entered); Assert.False(quit); Assert.Equal(0, operations.PendingCount);
    }
}
