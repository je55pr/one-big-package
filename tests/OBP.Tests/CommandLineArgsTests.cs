using OneBigPackage;

namespace OBP.Tests;

public sealed class CommandLineArgsTests
{
    [Fact]
    public void PlayerModesAreExplicitAndIndependentOfDestination()
    {
        foreach (var destination in new[] { "rac1:LEVEL0", "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab" })
            Assert.False(CommandLineArgs.Parse(["--destination", destination]).NativeParityRequested);
        Assert.True(CommandLineArgs.Parse(["--retail-parity"]).NativeParityRequested);
        Assert.True(CommandLineArgs.Parse(["--fusion-smoke"]).NativeParityRequested);
        Assert.True(CommandLineArgs.Parse(["--rac1-campaign-persist"]).NativeParityRequested);
        Assert.False(CommandLineArgs.Parse(["--record-playtest", "run.jsonl.gz"]).NativeParityRequested);
        Assert.True(CommandLineArgs.Parse(["--record-playtest", "run.jsonl.gz", "--playtest-smoke"]).NativeParityRequested);
        var diagnostic = CommandLineArgs.Parse(["--diagnostic-player"]);
        Assert.True(diagnostic.DiagnosticPlayer);
        Assert.False(diagnostic.NativeParityRequested);
        var route = CommandLineArgs.Parse(["--player-session-smoke"]);
        Assert.True(route.PlayerSessionSmoke);
        Assert.False(route.NativeParityRequested);
        var capabilities = CommandLineArgs.Parse(["--player-capabilities-smoke", "--player-capabilities-out", "trace.json"]);
        Assert.True(capabilities.PlayerCapabilitiesSmoke); Assert.Equal("trace.json", capabilities.PlayerCapabilitiesOut);
        Assert.False(capabilities.NativeParityRequested);
    }

    [Fact]
    public void BugCaptureAndReportReplay_AreExplicitModesWithLocalInboxAndStableReportIdentity()
    {
        Assert.False(CommandLineArgs.Parse([]).BugCapture);
        var capture = CommandLineArgs.Parse(["--bug-capture", "--bug-report-inbox", "reports", "--bug-report-smoke"]);
        Assert.True(capture.BugCapture);
        Assert.True(capture.BugReportSmoke);
        Assert.Equal("reports", capture.BugReportInbox);
        Assert.Null(capture.ReplayPlaytest);
        var replay = CommandLineArgs.Parse(["--replay-bug-report", "session.json", "--bug-report-id", "stable-id"]);
        Assert.Equal("session.json", replay.ReplayBugReport);
        Assert.Equal("stable-id", replay.BugReportId);
        Assert.False(replay.BugCapture);
    }
    [Fact]
    public void PlaytestOptionsAreOptInAndDistinctFromMovementBookmarks()
    {
        Assert.False(CommandLineArgs.Parse([]).PlaytestRequested);
        var record = CommandLineArgs.Parse(["--destination", "rac1:LEVEL1", "--record-playtest", "run.jsonl.gz", "--playtest-smoke"]);
        Assert.True(record.PlaytestRequested);
        Assert.True(record.PlaytestSmoke);
        Assert.Equal("run.jsonl.gz", record.RecordPlaytest);
        Assert.Null(record.LoadMovementState);
        Assert.Null(record.ReplayPlaytest);
        var replay = CommandLineArgs.Parse(["--replay-playtest", "run.jsonl.gz", "--playtest-report", "report.json", "--playtest-allow-build-change"]);
        Assert.True(replay.PlaytestRequested);
        Assert.True(replay.PlaytestAllowBuildChange);
        Assert.Equal("run.jsonl.gz", replay.ReplayPlaytest);
        Assert.Equal("report.json", replay.PlaytestReport);
        Assert.Null(replay.ReplayForwardFrames);
        Assert.Equal(string.Empty, CommandLineArgs.Parse(["--record-playtest"]).RecordPlaytest);
        Assert.Equal(string.Empty, CommandLineArgs.Parse(["--replay-playtest"]).ReplayPlaytest);
    }

    [Fact]
    public void SmokeSceneIsOnlyExplicitlyRequested()
    {
        CommandLineArgs ordinary = CommandLineArgs.Parse([]);
        CommandLineArgs explicitSmoke = CommandLineArgs.Parse(["--test-scene", "smoke"]);

        Assert.Equal("smoke", ordinary.TestScene);
        Assert.False(ordinary.TestSceneExplicit);
        Assert.Equal("smoke", explicitSmoke.TestScene);
        Assert.True(explicitSmoke.TestSceneExplicit);
    }

    [Fact]
    public void KerwanTaxiSmokeIsOptIn()
    {
        Assert.False(CommandLineArgs.Parse([]).Rac1MetropolisTaxiSmoke);
        Assert.True(CommandLineArgs.Parse(["--rac1-metropolis-taxi-smoke"]).Rac1MetropolisTaxiSmoke);
    }

    [Fact]
    public void SourceBootstrapOptionsShareTheParsedModel()
    {
        CommandLineArgs args = CommandLineArgs.Parse([
            "--rac1-iso", "rac1.iso",
            "--gc-iso", "gc.iso",
            "--uya-iso", "uya.iso",
            "--destination", "rac3:TABLE1",
            "--skip-title",
        ]);

        Assert.Equal("rac1.iso", args.Rac1Iso);
        Assert.Equal("gc.iso", args.GcIso);
        Assert.Equal("uya.iso", args.UyaIso);
        Assert.Equal("rac3:TABLE1", args.Destination);
        Assert.True(args.SkipTitle);
        Assert.False(args.TestSceneExplicit);
    }

    [Fact]
    public void Rac1CampaignPersistenceIsOptIn()
    {
        CommandLineArgs ordinary = CommandLineArgs.Parse([]);
        CommandLineArgs defaultPersistence = CommandLineArgs.Parse(["--rac1-campaign-persist"]);
        CommandLineArgs isolatedPersistence = CommandLineArgs.Parse([
            "--rac1-campaign-save",
            "campaign.json",
        ]);

        Assert.False(ordinary.Rac1CampaignPersist);
        Assert.Null(ordinary.Rac1CampaignSavePath);
        Assert.False(ordinary.Rac1CampaignPersistenceRequested);

        Assert.True(defaultPersistence.Rac1CampaignPersist);
        Assert.True(defaultPersistence.Rac1CampaignPersistenceRequested);

        Assert.False(isolatedPersistence.Rac1CampaignPersist);
        Assert.Equal("campaign.json", isolatedPersistence.Rac1CampaignSavePath);
        Assert.True(isolatedPersistence.Rac1CampaignPersistenceRequested);
    }

    [Fact]
    public void Rac1PlayabilityAndSyntheticCombatSmokesAreDistinct()
    {
        CommandLineArgs play = CommandLineArgs.Parse(["--rac1-veldin-play-smoke"]);
        CommandLineArgs legacyPlay = CommandLineArgs.Parse(["--rac1-combat-smoke"]);
        CommandLineArgs contract = CommandLineArgs.Parse(["--rac1-combat-contract-smoke"]);
        CommandLineArgs startup = CommandLineArgs.Parse(["--rac1-startup-visibility-smoke"]);

        Assert.True(play.Rac1VeldinPlaySmoke);
        Assert.False(play.Rac1CombatContractSmoke);

        Assert.True(legacyPlay.Rac1VeldinPlaySmoke);
        Assert.False(legacyPlay.Rac1CombatContractSmoke);

        Assert.False(contract.Rac1VeldinPlaySmoke);
        Assert.True(contract.Rac1CombatContractSmoke);

        Assert.True(startup.Rac1StartupVisibilitySmoke);
        Assert.False(startup.TestSceneExplicit);
    }
}
