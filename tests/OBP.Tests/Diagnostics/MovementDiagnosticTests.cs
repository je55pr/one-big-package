using System.Numerics;
using System.Collections.Immutable;
using OBP.Game;
using OBP.Game.Diagnostics;
using OBP.RAC1.Player;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class MovementDiagnosticTests
{
    private sealed class Clock : IDiagnosticFrameClock
    {
        public List<DiagnosticFramePhase> Phases { get; } = [];
        public int ProcessFrames { get; private set; }
        public bool Block { get; init; }
        public Action? BeforeCompletion { get; init; }
        public int Subscribers { get; private set; }
        public DiagnosticFrameSnapshot ReadFrames() => new((ulong)ProcessFrames, (ulong)Phases.Count(p => p == DiagnosticFramePhase.Physics));
        public async Task NextAsync(DiagnosticFramePhase phase, CancellationToken token)
        {
            Phases.Add(phase);
            if (phase == DiagnosticFramePhase.Process) ProcessFrames++;
            if (!Block) { BeforeCompletion?.Invoke(); return; }
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

    private sealed class WrenchApproach : IRac1WrenchApproachFixture
    {
        public double ControlYaw => 0;
        public bool Admits(Vector3 targetCenter) => false;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrenchApproachRetainsDiagonalRunSpeedAndReleasesInputOnTimeoutOrCancellation(bool canceled)
    {
        using var cancel = new CancellationTokenSource();
        var fixture = new Fixture();
        Rac1AnalogueInput.Conditioned? observed = null;
        var clock = new Clock
        {
            BeforeCompletion = () =>
            {
                observed = Rac1AnalogueInput.ConditionUnitAxes(
                    fixture.Actions.GetValueOrDefault(MovementDiagnosticDriver.Right) - fixture.Actions.GetValueOrDefault(MovementDiagnosticDriver.Left),
                    fixture.Actions.GetValueOrDefault(MovementDiagnosticDriver.Forward) - fixture.Actions.GetValueOrDefault(MovementDiagnosticDriver.Back));
                if (canceled) cancel.Cancel();
            }
        };
        var player = new PlayerFixture();
        using var run = new DiagnosticRunContext(clock, cancel.Token, 10);
        using var driver = fixture.Create(run);
        var scenario = new Rac1WrenchApproachScenario(new(Ports(player, clock), driver), new WrenchApproach());
        Task Approach() => scenario.RunAsync(() => player.State.GlobalPosition + new Vector3(-1, 0, -1), canceled ? 2 : 1, "diagonal regression");
        if (canceled) await Assert.ThrowsAnyAsync<OperationCanceledException>(Approach);
        else await Assert.ThrowsAsync<TimeoutException>(Approach);
        var input = Assert.IsType<Rac1AnalogueInput.Conditioned>(observed);
        Assert.Equal(Rac1AnalogueSpeedBand.Run, input.SpeedBand);
        Assert.InRange(input.Magnitude, .999999, 1);
        Assert.InRange(input.Direction.X, Math.Sqrt(.5) - .000001, Math.Sqrt(.5) + .000001);
        Assert.InRange(input.Direction.Y, Math.Sqrt(.5) - .000001, Math.Sqrt(.5) + .000001);
        Assert.All(fixture.Actions.Values, value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData("full")]
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

    [Fact]
    public void FailedPhysicalReleaseCannotPoisonTheNextOwner()
    {
        bool held = false, failRelease = true;
        var input = new DiagnosticPhysicalInput((_, value) =>
        {
            if (!value && failRelease) { failRelease = false; throw new IOException("Expected release failure"); }
            held = value;
        }, (_, _) => { });
        using var first = input.Acquire(); first.SetKey(DiagnosticPhysicalKey.E, true);
        Assert.Throws<AggregateException>(first.Dispose); Assert.True(held);
        using var second = input.Acquire(); second.SetKey(DiagnosticPhysicalKey.E, true); second.Dispose();
        Assert.False(held);
        first.Dispose(); Assert.False(held);
    }

    [Fact]
    public void FailedPhysicalPressStillGetsAReleaseAttempt()
    {
        bool held = false, failPress = true;
        var input = new DiagnosticPhysicalInput((_, value) =>
        {
            held = value;
            if (value && failPress) { failPress = false; throw new IOException("Expected partially applied press"); }
        }, (_, _) => { });
        using var lease = input.Acquire();
        Assert.Throws<IOException>(() => lease.SetKey(DiagnosticPhysicalKey.E, true)); Assert.True(held);
        lease.Dispose(); Assert.False(held);
    }

    [Fact]
    public async Task CancelledDriverAllowsNeutralReleaseAndRejectsALateButtonPress()
    {
        using var cancel = new CancellationTokenSource();
        var clock = new Clock { BeforeCompletion = cancel.Cancel }; var fixture = new Fixture();
        using var run = new DiagnosticRunContext(clock, cancel.Token, 20); using var driver = fixture.Create(run);
        driver.Analogue(.5f, 1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.SetButtonAsync(DiagnosticPhysicalButton.EquippedUse, true));
        Assert.Empty(fixture.Buttons);
        driver.Analogue(0, 0); await driver.SetButtonAsync(DiagnosticPhysicalButton.EquippedUse, false);
        Assert.All(fixture.Actions.Values, value => Assert.Equal(0, value));
    }

    [Fact]
    public void NormalizationRetainsGodotsZeroVectorBehaviour()
    {
        Assert.Equal(Vector3.Zero, DiagnosticMovementMath.Normalized(Vector3.Zero));
        Assert.Equal(Vector3.UnitX, DiagnosticMovementMath.Normalized(new(5, 0, 0)));
        Assert.True(float.IsNaN(DiagnosticMovementMath.Normalized(new(float.NaN, 0, 0)).X));
    }

    private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    private sealed class PlayerFixture : IDiagnosticMovementPlayer
    {
        public DiagnosticMovementState State { get; set; } = new()
        {
            GlobalPosition = new(1, 2, 3),
            Rotation = new(0, .5f, 0),
            Rac1CurrentYaw = .5,
            Rac1ClankAvailable = true,
            Rac1DevelopmentPackPreview = 2
        };
        public bool FailPack { get; set; }
        public bool FailSettingsCleanup { get; set; }
        public DiagnosticMovementState Read() => State;
        public IDisposable PreserveDevelopmentSettings() => new Release(() => { if (FailSettingsCleanup) throw new IOException("settings cleanup"); });
        public void SetPosition(Vector3 position) => State = State with { GlobalPosition = position };
        public void ResetAt(Vector3 position) => SetPosition(position);
        public void SetRotation(Vector3 rotation) => State = State with { Rotation = rotation };
        public void SetYaw(double yaw) => State = State with { Rac1CurrentYaw = yaw };
        public void SetBackpack(Rac1TraversalEquipmentId? backpack) => State = State with { Rac1ActiveBackpack = backpack };
        public void SetClank(bool available) => State = State with { Rac1ClankAvailable = available };
        public void CyclePack()
        {
            if (FailPack) throw new IOException("pack cleanup");
            State = State with { Rac1DevelopmentPackPreview = State.Rac1DevelopmentPackPreview switch { null => 2, 2 => 3, _ => null } };
        }
        public void ToggleSwingshot() => State = State with { Rac1DevelopmentSwingshotPreview = !State.Rac1DevelopmentSwingshotPreview };
        public void SetSnapReach(float reach) { }
        public void ResetCollisionDiagnostics() { }
        public void ResetToSpawn() { }
        public void StageWater(Vector3 position) => SetPosition(position);
        public void ConfigureTargets(ImmutableArray<RuntimeDynamicObject> targets) { }
        public void RestoreWorldTargets() { }
        public IDisposable SubscribeDrowned(Action action) => new Release(() => { });
    }
    private sealed class Geometry : IDiagnosticGeometryLease
    {
        public Vector3 GlobalPosition => Vector3.Zero;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        public void AddMesh(string name, int rawFaceType, params (Vector3 A, Vector3 B, Vector3 C)[] triangles) { }
        public IDisposable AddBox(string name, Vector3 size, Vector3 position, float yaw) => new Release(() => { });
    }
    private sealed class Sources : IDiagnosticSources { public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => []; }
    private static MovementScenarioPorts Ports(PlayerFixture player, Clock clock) => new(player, null!, null!, null!,
        new DiagnosticInputActions(_ => true, (_, _) => { }), new DiagnosticPhysicalInput((_, _) => { }, (_, _) => { }), clock, _ => { }, _ => { });
    private static DiagnosticScenarioDefinition Definition => new("movement-cleanup-fixture/v1", DiagnosticSourceAccess.AssetFree,
        DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromSeconds(10), 100);
    private sealed class RestoreProbe(MovementDiagnosticContext context, PlayerFixture player, Geometry geometry) : MovementScenarioSupport(context)
    {
        public async Task RunAsync()
        {
            var saved = player.Read(); player.State = saved with { GlobalPosition = new(100, 200, 300), Rac1DevelopmentPackPreview = 3 };
            player.FailPack = true;
            try { throw new InvalidOperationException("original scenario assertion"); }
            finally { await RestoreFixturePlayerAsync(geometry, saved); }
        }
    }

    [Fact]
    public async Task RunnerRetainsTheOriginalFailureWhenOwnedSettingsCleanupFails()
    {
        var clock = new Clock(); var player = new PlayerFixture { FailSettingsCleanup = true };
        var scenario = new MovementDiagnosticScenario(Definition, Ports(player, clock), _ => throw new InvalidOperationException("original scenario assertion"));
        var result = await new DiagnosticScenarioRunner(new Sources(), clock).RunAsync(scenario, true, CancellationToken.None);
        var errors = Assert.IsType<AggregateException>(result.Error).Flatten().InnerExceptions;
        Assert.Contains(errors, error => error.Message == "original scenario assertion");
        Assert.Contains(errors, error => error.Message == "settings cleanup");
    }

    [Fact]
    public async Task FailedPackRestoreDoesNotSkipPositionRestoreOrHideTheAssertion()
    {
        var clock = new Clock(); var player = new PlayerFixture(); var original = player.State; var geometry = new Geometry();
        var scenario = new MovementDiagnosticScenario(Definition, Ports(player, clock), context => new RestoreProbe(context, player, geometry).RunAsync());
        var result = await new DiagnosticScenarioRunner(new Sources(), clock).RunAsync(scenario, true, CancellationToken.None);
        var errors = Assert.IsType<AggregateException>(result.Error).Flatten().InnerExceptions;
        Assert.Contains(errors, error => error.Message == "original scenario assertion");
        Assert.Contains(errors, error => error.Message == "pack cleanup");
        Assert.Equal(original.GlobalPosition, player.State.GlobalPosition);
        Assert.Equal(original.Rotation, player.State.Rotation); Assert.Equal(original.Rac1CurrentYaw, player.State.Rac1CurrentYaw);
        Assert.True(geometry.Disposed);
    }
}
