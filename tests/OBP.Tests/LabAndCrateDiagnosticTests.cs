using System.Numerics;
using System.Text.Json;
using OBP.Game.Diagnostics;

namespace OBP.Tests;

public sealed class LabAndCrateDiagnosticTests
{
    [Fact]
    public async Task SourceFreeLabUsesTheOriginalThresholdsMovementAxesAndOneDeferredReload()
    {
        var fixture = new Lab([-6, -5, -4.9f, 14, 14.1f, -13, -13.1f]); var messages = new List<string>();
        var scenario = new FusionLabTraversalScenario(fixture, messages.Add, messages.Add);
        scenario.Definition.Validate();
        Assert.Equal(DiagnosticSourceAccess.AssetFree, scenario.Definition.SourceAccess);
        Assert.Empty(scenario.Definition.RequiredSources);
        using var run = new DiagnosticRunContext(fixture, default, 20);
        await scenario.RunAsync(run);
        Assert.Equal([-1, -1, -1, -1, 1, 1, 0], fixture.Axes.Select(axis => axis.Y));
        Assert.All(fixture.Axes, axis => Assert.Equal(0, axis.X));
        Assert.Equal(1, fixture.Reloads); Assert.True(fixture.Released);
        Assert.Contains(messages, message => message.StartsWith("[fusion-smoke] PASS:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CanceledLabReleasesItsScopedInputAndCannotReloadOrLogSuccess()
    {
        var fixture = new Lab([-6]) { Suspend = true }; var messages = new List<string>();
        using var canceled = new CancellationTokenSource();
        using var run = new DiagnosticRunContext(fixture, canceled.Token, 20);
        var pending = new FusionLabTraversalScenario(fixture, messages.Add, messages.Add).RunAsync(run);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(fixture.Released); Assert.Equal(0, fixture.Reloads); Assert.Empty(messages);
    }

    [Fact]
    public void CrateFocusRemainsOptInAndUnavailableTargetsHaveNoSyntheticPose()
    {
        var probe = new Rac2CrateDiagnosticProbe(); var rewards = new Rac2CrateRewardSnapshot(0, 0, 0);
        Assert.Null(probe.Capture(Target, rewards)); Assert.Empty(probe.HudLine(Target, rewards));
        Assert.False(probe.TryGetFocusPose(Target, null, out _, out _));
        probe.Configure(false, true, null);
        Assert.Equal("requested: no class-500 target", probe.Status);
        Assert.False(probe.TryGetFocusPose(null, null, out _, out _));
        Assert.False(probe.TryGetFocusPose(Target with { Valid = false }, null, out _, out _));
    }

    [Fact]
    public void CrateStagingAndNativeObservationKeepTheEstablishedCaptureSchema()
    {
        var probe = new Rac2CrateDiagnosticProbe(); probe.Configure(true, false, Target);
        Assert.True(probe.TryGetFocusPose(Target, new Vector3(10, 50, 0), out var spawn, out float yaw));
        Assert.Equal(new Vector3(7, 4, 0), spawn); Assert.Equal(MathF.PI * .5f, yaw);
        probe.ObserveStrike(9, 0, "Deactivate", "auto"); probe.Broken = true; probe.RewardStatus = "observed";
        var snapshot = probe.Capture(Target with { Visible = false }, new(2, 3, 4))!;
        Assert.Equal("auto: state 1 -> 3 -> Deactivate", snapshot.Status);
        Assert.True(snapshot.Broken); Assert.False(snapshot.Visible);
        Assert.Equal(2, snapshot.CollectedBolts); Assert.Equal(3, snapshot.OutstandingPickups); Assert.Equal(4, snapshot.DeferredBolts);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));
        Assert.Equal(["TargetInteractionId", "NativeUid", "NativeInstanceIndex", "Visible", "Broken", "Status", "EventFlags", "EventScalar",
            "PvarC8", "Route", "RewardStatus", "CollectedBolts", "OutstandingPickups", "DeferredBolts"],
            json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("0x00010000", snapshot.EventFlags);
        Assert.Equal(2, snapshot.EventScalar);
    }

    private static Rac2CrateTargetSnapshot Target => new("rac2:class500:9", 8, 9, Vector3.Zero, true, true);
    private sealed class Lab(float[] positions) : IFusionLabTraversalFixture, IDiagnosticFrameClock
    {
        private Func<FusionLabTraversalPose, Vector2>? _input;
        private int _index;
        public int Reloads;
        public bool Released, Suspend;
        public List<Vector2> Axes { get; } = [];
        public IDisposable Drive(Func<FusionLabTraversalPose, Vector2> input) { _input = input; return new Release(() => { Released = true; _input = null; }); }
        public FusionLabTraversalPose Read() => new(0, positions[Math.Min(_index, positions.Length - 1)], _index);
        public Task<bool> ReloadDeferredAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); Reloads++; return Task.FromResult(true); }
        public DiagnosticFrameSnapshot ReadFrames() => new((ulong)_index, (ulong)_index);
        public Task NextAsync(DiagnosticFramePhase phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Assert.Equal(DiagnosticFramePhase.Process, phase);
            if (Suspend) return Task.Delay(Timeout.Infinite, cancellationToken);
            Assert.True(_index < positions.Length, "Lab scenario exceeded its recorded route.");
            Axes.Add(_input!(Read())); _index++;
            return Task.CompletedTask;
        }
    }
    private sealed class Release(Action release) : IDisposable { public void Dispose() => release(); }
}
