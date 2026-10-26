using System.Numerics;
using OBP.Game;
using OBP.Game.Diagnostics;

namespace OBP.Tests;

public sealed class MovementDiagnosticTests
{
    private sealed class Clock : IDiagnosticFrameClock
    {
        public List<DiagnosticFramePhase> Phases { get; } = [];
        public int ProcessFrames { get; private set; }
        public bool Block { get; init; }
        public int Subscribers { get; private set; }
        public DiagnosticFrameSnapshot ReadFrames() => new((ulong)ProcessFrames, (ulong)Phases.Count(p => p == DiagnosticFramePhase.Physics));
        public async Task NextAsync(DiagnosticFramePhase phase, CancellationToken token)
        {
            Phases.Add(phase);
            if (phase == DiagnosticFramePhase.Process) ProcessFrames++;
            if (!Block) return;
            Subscribers++;
            var signal = new TaskCompletionSource();
            using var registration = token.Register(() => signal.TrySetCanceled(token));
            try { await signal.Task; }
            finally { Subscribers--; }
        }
    }
    private sealed class Fixture
    {
        public Dictionary<string, float> Actions { get; } = [];
        public Dictionary<DiagnosticPhysicalKey, bool> Keys { get; } = [];
        public Dictionary<DiagnosticPhysicalButton, bool> Buttons { get; } = [];
        public MovementDiagnosticDriver Create(DiagnosticRunContext run) => new(run,
            new DiagnosticInputActions(_ => true, (action, value) => Actions[action] = value),
            new DiagnosticPhysicalInput((key, value) => Keys[key] = value, (button, value) => Buttons[button] = value));
    }

    [Fact]
    public async Task GroundedWaitObservesCompletedPhysicsAndRequiresThreeConsecutiveNativeSupports()
    {
        var clock = new Clock(); using var run = new DiagnosticRunContext(clock, CancellationToken.None, 30);
        using var driver = new Fixture().Create(run);
        await driver.WaitGroundedAsync(() => true, () => clock.ProcessFrames != 2, 6);
        Assert.Equal(5, clock.ProcessFrames);
        Assert.Equal(Enumerable.Range(0, 5).SelectMany(_ => new[] { DiagnosticFramePhase.Physics, DiagnosticFramePhase.Process }), clock.Phases);
    }

    [Fact]
    public async Task CancellationUnsubscribesTheFrameWaitAndReleasesEveryOwnedPress()
    {
        var clock = new Clock { Block = true }; var fixture = new Fixture(); using var cancel = new CancellationTokenSource();
        var run = new DiagnosticRunContext(clock, cancel.Token, 30);
        var driver = run.Own(fixture.Create(run));
        driver.SetAction(MovementDiagnosticDriver.Jump, true); driver.Analogue(.6f, 1);
        driver.SetKey(DiagnosticPhysicalKey.E, true);
        Task pending = driver.PhysicsAsync(); Assert.Equal(1, clock.Subscribers);
        cancel.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cancel.Token, error.CancellationToken);
        run.Dispose(); Assert.Empty(run.CleanupFailures);
        Assert.Equal(0, clock.Subscribers); Assert.All(fixture.Actions.Values, value => Assert.Equal(0, value));
        Assert.False(fixture.Keys[DiagnosticPhysicalKey.E]);
        Assert.Throws<ObjectDisposedException>(() => driver.SetKey(DiagnosticPhysicalKey.E, true));
    }

    [Fact]
    public async Task ControllerEdgesRetainTheOriginalProcessAndThreePhysicsFramePhases()
    {
        var clock = new Clock(); var fixture = new Fixture(); using var run = new DiagnosticRunContext(clock, CancellationToken.None, 20);
        using var driver = fixture.Create(run);
        await driver.TapButtonAsync(DiagnosticPhysicalButton.EquippedUse);
        Assert.False(fixture.Buttons[DiagnosticPhysicalButton.EquippedUse]);
        Assert.Equal(new[] { DiagnosticFramePhase.Process, DiagnosticFramePhase.Physics, DiagnosticFramePhase.Physics, DiagnosticFramePhase.Physics,
            DiagnosticFramePhase.Process, DiagnosticFramePhase.Physics, DiagnosticFramePhase.Physics, DiagnosticFramePhase.Physics }, clock.Phases);
    }

    [Fact]
    public void PhysicalLeasesDisposedOutOfOrderPreserveOtherSimulationOwners()
    {
        bool heldKey = false, heldButton = false;
        var keyEdges = new List<bool>(); var buttonEdges = new List<bool>();
        var input = new DiagnosticPhysicalInput((_, value) => { heldKey = value; keyEdges.Add(value); },
            (_, value) => { heldButton = value; buttonEdges.Add(value); });
        using var first = input.Acquire(); using var second = input.Acquire();
        first.SetKey(DiagnosticPhysicalKey.W, true); second.SetKey(DiagnosticPhysicalKey.W, true);
        first.SetButton(DiagnosticPhysicalButton.Wrench, true); second.SetButton(DiagnosticPhysicalButton.Wrench, true);
        first.Dispose(); Assert.True(heldKey); Assert.True(heldButton);
        second.Dispose(); Assert.False(heldKey); Assert.False(heldButton);
        Assert.Equal(new[] { true, false }, keyEdges);
        Assert.Equal(new[] { true, false }, buttonEdges);
    }

    [Fact]
    public void ConditionedAxesRetainTheRecoveredDeadzoneInverseIncludingSignedDiagonals()
    {
        Assert.Equal(Vector2.Zero, MovementDiagnosticDriver.RawConditionedAxes(0, 0));
        Assert.Equal(new Vector2(.96875f, -.96875f), MovementDiagnosticDriver.RawConditionedAxes(1, -1));
        Assert.Equal(new Vector2(.671875f, .671875f), MovementDiagnosticDriver.RawConditionedAxes(.5, .5));
        Assert.Equal(Vector2.Zero, MovementDiagnosticDriver.RawConditionedAxes(1e-9, -1e-9));
    }

    [Theory]
    [InlineData("airborne")]
    [InlineData("environmental-death")]
    [InlineData("landing")]
    [InlineData("swim")]
    [InlineData("slope-jump")]
    [InlineData("small-step")]
    [InlineData("swingshot")]
    [InlineData("traversal-contact")]
    [InlineData("controller-weapons")]
    public void IndividualCasesUseTheExistingExplicitMovementDispatch(string name)
    {
        var args = CommandLineArgs.Parse(["--movement-case", name]);
        Assert.True(args.MovementSmoke); Assert.Equal(name, args.MovementCase);
        Assert.Equal(name is "environmental-death" or "swim", args.NativeParityRequested);
        Assert.Equal("movement-smoke", Assert.Single(WorldSmokeHarness.Registry.Select(args, OBP.Core.ObpSourceGame.Rac1, 1)).Id);
    }

    [Fact]
    public void UnknownOrMissingCasesFailAtTheValidatedCliBoundary()
    {
        Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse(["--movement-case", "unknown"]));
        Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse(["--movement-case"]));
    }
}
