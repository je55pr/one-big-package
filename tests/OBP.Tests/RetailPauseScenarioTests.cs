using System.Collections.Immutable;
using OBP.Game.Diagnostics;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class RetailPauseScenarioTests
{
    private static DiagnosticScenarioDefinition Definition => new("pause-fixture/v1", DiagnosticSourceAccess.AssetFree,
        DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromSeconds(10), 7200);
    private sealed class Sources : IDiagnosticSources { public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => []; }
    private sealed class Clock(Fixture fixture) : IDiagnosticFrameClock
    {
        public int Waits { get; private set; }
        public Action<int>? OnFrame { get; set; }
        public DiagnosticFrameSnapshot ReadFrames() => new((ulong)Waits, 0);
        public Task NextAsync(DiagnosticFramePhase phase, CancellationToken cancellationToken)
        {
            Assert.Equal(DiagnosticFramePhase.Process, phase);
            fixture.Tick(); Waits++; OnFrame?.Invoke(Waits); cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class Fixture : IDiagnosticRetailPauseFixture
    {
        private int _rootFocus, _cellFocus, _transition;
        private Rac1RetailMenuPage _target;
        public bool Guard { get; private set; }
        public int Restores { get; private set; }
        public bool StallTransitions { get; set; }
        public bool MutateAmmo { get; set; }
        public bool BadPixels { get; set; }
        public HashSet<string> Pressed { get; } = [];
        public DiagnosticRetailPauseSnapshot State { get; set; } = new(true, true, true, false, false, false,
            new(1, 2, 3), 4, [4, 5, 6], [40, 50, 60],
            [.. Enumerable.Range(0, 6).Select(index => new DiagnosticRetailPanel("panel" + index, true, true, 1, new(0, 0, 300, 60)))],
            "panel0", "Weapons", [], Rac1RetailMenuPage.Root, "Captured", false, true, 640, 480);
        public DiagnosticRetailPauseSnapshot Read() => State;
        public bool Open()
        {
            _rootFocus = 0; _cellFocus = 0;
            State = State with { PauseOpen = true, TreePaused = true, RetailEnabled = true, Page = Rac1RetailMenuPage.Root, FocusId = "panel0", FocusText = "Weapons", PointerMode = "Hidden" };
            return true;
        }
        public bool Close()
        {
            State = State with { PauseOpen = false, TreePaused = false, PointerMode = "Captured" };
            return true;
        }
        public IDisposable BeginInputReview()
        {
            Guard = true;
            State = State with { ViewportWidth = 960, ViewportHeight = 720 };
            return new Lease(() => { Close(); Guard = false; Restores++; State = State with { ViewportWidth = 640, ViewportHeight = 480 }; });
        }
        public void InjectAction(string action, bool pressed, bool admitted)
        {
            if (!admitted && Guard) return;
            if (!pressed) { Pressed.Remove(action); return; }
            Pressed.Add(action);
            if (State.Page == Rac1RetailMenuPage.Root)
            {
                if (action == "ui_down") _rootFocus++;
                if (action == "ui_up") _rootFocus--;
                State = State with { FocusId = "panel" + _rootFocus, FocusText = _rootFocus switch { 0 => "Weapons", 1 => "Gadgets", 3 => "Items", _ => "Other" } };
                if (action == "ui_accept") Schedule(_rootFocus == 3 ? Rac1RetailMenuPage.Items : Rac1RetailMenuPage.Weapons);
                if (action == "ui_cancel") Close();
            }
            else
            {
                if (action == "ui_cancel") Schedule(Rac1RetailMenuPage.Root);
                if (State.Page != Rac1RetailMenuPage.Items) return;
                if (action == "ui_right") _cellFocus++;
                if (action == "ui_down") _cellFocus += 3;
                State = State with { FocusId = "cell" + _cellFocus };
                if (MutateAmmo && action == "ui_accept") State = State with { Ammo = [0, 50, 60] };
            }
        }
        private void Schedule(Rac1RetailMenuPage page) { _target = page; _transition = 6; }
        public void Tick()
        {
            if (_transition == 0 || StallTransitions || --_transition != 0) return;
            _rootFocus = 0; _cellFocus = 0;
            State = State with
            {
                Page = _target,
                FocusId = _target == Rac1RetailMenuPage.Items ? "cell0" : "panel0",
                FocusText = "Weapons",
                ItemCells = _target == Rac1RetailMenuPage.Items ? [.. Enumerable.Range(0, 9).Select(index => "cell" + index)] : []
            };
        }
        public DiagnosticPanelCapture CapturePanels(string folder, string fileName) => new(960, 720,
            [.. State.Panels.Select(panel => new DiagnosticPanelPixel(panel.Id, panel.Bounds, 30, 28, BadPixels ? .26f : .1f, .1f, .1f))]);
    }

    [Theory]
    [InlineData("Veldin")]
    [InlineData("Novalis")]
    public async Task LiveReviewPreservesNavigationTimingInventoryAndRestoresItsFixture(string world)
    {
        var fixture = new Fixture(); var clock = new Clock(fixture);
        var result = await new DiagnosticScenarioRunner(new Sources(), clock).RunAsync(
            new Rac1RetailNavigationScenario(Definition, world, fixture, _ => { }), true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Passed, result.Status);
        Assert.True(clock.Waits > 40); Assert.Equal(1, fixture.Restores);
        Assert.False(fixture.State.TreePaused); Assert.False(fixture.Guard); Assert.Empty(fixture.Pressed);
        Assert.Equal(640, fixture.State.ViewportWidth);
    }

    [Theory]
    [InlineData("inventory", "mutated paused gameplay")]
    [InlineData("transition", "did not complete")]
    [InlineData("pixels", "opaque backing")]
    [InlineData("panels", "root panels")]
    public async Task LiveReviewRejectsRegressionsAndCleansUp(string fault, string expected)
    {
        var fixture = new Fixture { MutateAmmo = fault == "inventory", StallTransitions = fault == "transition", BadPixels = fault == "pixels" };
        if (fault == "pixels") fixture.State = fixture.State with { Headless = false };
        if (fault == "panels") fixture.State = fixture.State with { Panels = [] };
        var result = await new DiagnosticScenarioRunner(new Sources(), new Clock(fixture)).RunAsync(
            new Rac1RetailNavigationScenario(Definition, "Veldin", fixture, _ => { }), true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status); Assert.Contains(expected, result.Detail, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Restores); Assert.False(fixture.Guard); Assert.False(fixture.State.TreePaused); Assert.Empty(fixture.Pressed);
    }

    [Fact]
    public async Task CancellationDuringAnAdmittedPressReleasesInputAndRestoresViewport()
    {
        var fixture = new Fixture(); var clock = new Clock(fixture); using var cancellation = new CancellationTokenSource();
        bool heldAtCancel = false;
        clock.OnFrame = frame => { if (frame == 15) { heldAtCancel = fixture.Pressed.Contains("ui_down"); cancellation.Cancel(); } };
        var result = await new DiagnosticScenarioRunner(new Sources(), clock).RunAsync(
            new Rac1RetailNavigationScenario(Definition, "Veldin", fixture, _ => { }), true, cancellation.Token);
        Assert.Equal(DiagnosticRunStatus.Canceled, result.Status); Assert.Equal(1, fixture.Restores);
        Assert.True(heldAtCancel, "Cancellation must land while the admitted press is held.");
        Assert.Empty(fixture.Pressed); Assert.False(fixture.Guard); Assert.False(fixture.State.PauseOpen); Assert.Equal("Captured", fixture.State.PointerMode);
    }

    [Fact]
    public async Task VeldinPauseRetainsSixPanelAndPausedPositionChecks()
    {
        var fixture = new Fixture(); var clock = new Clock(fixture);
        var result = await new DiagnosticScenarioRunner(new Sources(), clock).RunAsync(
            new Rac1VeldinRetailPauseScenario(Definition, fixture, _ => { }), true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Passed, result.Status); Assert.Equal(9, clock.Waits);
        Assert.False(fixture.State.PauseOpen); Assert.False(fixture.State.TreePaused);
    }

    [Fact]
    public async Task VeldinPanelFailureStillClosesPause()
    {
        var fixture = new Fixture(); fixture.State = fixture.State with { Panels = [] };
        var result = await new DiagnosticScenarioRunner(new Sources(), new Clock(fixture)).RunAsync(
            new Rac1VeldinRetailPauseScenario(Definition, fixture, _ => { }), true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status);
        Assert.Contains("six native projected and shaded panels", result.Detail, StringComparison.Ordinal);
        Assert.False(fixture.State.PauseOpen); Assert.False(fixture.State.TreePaused);
    }
}
