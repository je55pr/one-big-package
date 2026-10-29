using System.Collections.Immutable;
using OBP.Core;
using OBP.Game;
using OBP.Game.Diagnostics;
using OBP.IO;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;
using OBP.Runtime;
using Xunit;

namespace OBP.Tests;

public sealed class Rac3RatchetSeedScenarioTests
{
    [Fact]
    public async Task AdmissionRequiresExplicitRac3ScenarioAndSamplesAfter45PhysicsFrames()
    {
        var args = CommandLineArgs.Parse(["--rac3-target-seed-audit", "--destination", "rac3:TABLE1"]);
        Assert.True(args.NativeParityRequested);
        Assert.Single(WorldSmokeHarness.Registry.Select(args, ObpSourceGame.Rac3, 1));
        Assert.Empty(WorldSmokeHarness.Registry.Select(args, ObpSourceGame.Rac1, 1));
        Assert.Empty(WorldSmokeHarness.Registry.Select(args, ObpSourceGame.Rac3, 2));

        var definition = new DiagnosticScenarioDefinition(
            "rac3-target-seed-audit/v1",
            DiagnosticSourceAccess.ExplicitSourceDiagnostic,
            DiagnosticSetup.ExistingWorld,
            [new(ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId)],
            TimeSpan.FromSeconds(5), 46, "rac3:TABLE1");
        int samples = 0;
        string? report = null;
        var scenario = new Rac3RatchetSeedScenario(definition, () =>
        {
            samples++;
            return new(7, 5, 3, 2, 1, 0);
        }, message => report = message);
        var clock = new FrameClock();
        var runner = new DiagnosticScenarioRunner(new Sources(), clock,
            new ExistingDiagnosticWorld(() =>
                new("rac3:TABLE1", (WorldContentIdentity)ObpSourceGame.Rac3, "world-a", 1)));

        var denied = await runner.RunAsync(scenario, explicitlyRequested: false, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Unavailable, denied.Status);
        Assert.Equal(0, samples);
        Assert.Equal(0, clock.Physics);

        var passed = await runner.RunAsync(scenario, explicitlyRequested: true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Passed, passed.Status);
        Assert.Equal(45, clock.Physics);
        Assert.Equal(1, samples);
        Assert.Contains("primary=3/5", report, StringComparison.Ordinal);
        Assert.Contains("unknown=1/0", report, StringComparison.Ordinal);
        Assert.Contains("selector-slots=10", report, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void OriginalTable1AuditRetainsAuthoredObjectCountAndProducesDeterministicSnapshots()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        RuntimeWorld world = Rac3WorldProvider.Instance.Load(iso!, Rac3DestinationCatalogue.Instance.FindByTableIndex(1)!);
        int count = world.DynamicObjects?.Count ?? 0;
        var ratchet = new Rac3NativePoint(0, 0, 0);
        Rac3RatchetSeedSnapshot first = Rac3RatchetSeedSampler.Sample(world, ratchet);
        Rac3RatchetSeedSnapshot second = Rac3RatchetSeedSampler.Sample(world, ratchet);
        Assert.Equal(first, second);
        Assert.Equal(count, world.DynamicObjects?.Count);
        Assert.True(first.Admitted > 0);
        Assert.True(first.Authored >= first.Admitted);
        Assert.InRange(first.PrimaryUnknown, 0, first.Admitted);
        Assert.InRange(first.SecondaryUnknown, 0, first.Admitted);
        Assert.InRange(first.PrimarySeeded, 0, first.Admitted);
        Assert.InRange(first.SecondarySeeded, 0, first.Admitted);
    }

    private sealed class Sources : IDiagnosticSources
    {
        public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() =>
            [new(ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId, true)];
    }

    private sealed class FrameClock : IDiagnosticFrameClock
    {
        public int Physics { get; private set; }
        public DiagnosticFrameSnapshot ReadFrames() => new(0, (ulong)Physics);
        public Task NextAsync(DiagnosticFramePhase phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (phase == DiagnosticFramePhase.Physics) Physics++;
            return Task.CompletedTask;
        }
    }
}
