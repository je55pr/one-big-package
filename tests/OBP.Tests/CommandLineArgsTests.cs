using OBP.Game;

namespace OBP.Tests;

public sealed class CommandLineArgsTests
{
    [Fact]
    public void EightTyhrranoidTestIsExplicitAndDoesNotChangeOrdinaryLabOrReplayMode()
    {
        Assert.False(CommandLineArgs.Parse([]).TyhrranoidTest);
        var test = CommandLineArgs.Parse(["--destination", "obp:fusion-lab", "--tyhrranoid-test"]);
        Assert.True(test.TyhrranoidTest);
        Assert.Equal("obp:fusion-lab", test.Destination);
        Assert.False(test.FusionSmoke);
        Assert.False(test.NativeParityRequested);
        var firstPage = CommandLineArgs.Parse(["--tyhrranoid-audit-page", "0"]);
        var nextPage = CommandLineArgs.Parse(["--tyhrranoid-audit-page", "1"]);
        Assert.True(firstPage.TyhrranoidTest);
        Assert.Equal(0, firstPage.TyhrranoidAuditPage);
        Assert.Equal(1, nextPage.TyhrranoidAuditPage);
        Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse(["--tyhrranoid-audit-page", "2"]));
    }

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
        Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse(["--record-playtest"]));
        Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse(["--replay-playtest"]));
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
        Assert.Equal("gc.iso", args.Rac2Iso);
        Assert.Equal("uya.iso", args.Rac3Iso);
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

    public static IEnumerable<object[]> ValueOptions() => new[]
    {
        "--test-scene", "--capture-frame", "--capture-out", "--fixed-seed", "--record-playtest", "--replay-playtest",
        "--playtest-report", "--bug-report-inbox", "--replay-bug-report", "--bug-report-id", "--movement-state-file",
        "--load-movement-state", "--replay-forward", "--replay-out", "--rac1-iso", "--rac2-iso", "--rac3-iso",
        "--gc-iso", "--uya-iso", "--gc-level", "--planet", "--destination", "--profile-smoke", "--acceptance-out",
        "--profile", "--source-config", "--ordinary-ui-smoke", "--player-capabilities-out", "--stress-switch", "--overlay", "--shots", "--shots-world",
        "--shots-out", "--rac1-campaign-save",
    }.Select(option => new object[] { option });

    [Theory]
    [MemberData(nameof(ValueOptions))]
    public void EveryValueOptionRejectsMissingEmptyAndFlagValues(string option)
    {
        foreach (string[] tokens in new[] { new[] { option }, new[] { option, "" }, new[] { option, "--skip-title" } })
            Assert.Contains(option, Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse(tokens)).Message);
    }

    [Theory]
    [InlineData("--typo")]
    [InlineData("unexpected-position")]
    [InlineData("--headless")]
    [InlineData("--direct-load")]
    public void UnknownUserArgumentsAreRejected(string option) =>
        Assert.Contains(option, Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse([option])).Message);

    [Theory]
    [InlineData("--capture-frame")]
    [InlineData("--fixed-seed")]
    [InlineData("--gc-level")]
    [InlineData("--replay-forward")]
    public void NumericOptionsRejectMalformedAndOverflowingIntegers(string option)
    {
        foreach (string invalid in new[] { "NaN", "1.5", "2147483648", "-2147483649" })
            Assert.Contains(option, Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse([option, invalid])).Message);
    }

    [Theory]
    [InlineData("--replay-forward", "0")]
    [InlineData("--replay-forward", "3601")]
    [InlineData("--compose-reload", "0")]
    [InlineData("--compose-reload", "-1")]
    public void DefinedNumericRangesAreEnforced(string option, string value) =>
        Assert.Contains(option, Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse([option, value])).Message);

    [Theory]
    [InlineData("--rac2-iso", "--gc-iso")]
    [InlineData("--rac3-iso", "--uya-iso")]
    public void CanonicalSourceOptionsAndLegacyAliasesShareOneValidatedValue(string canonical, string alias)
    {
        foreach (var order in new[] { new[] { canonical, alias }, new[] { alias, canonical } })
        {
            var parsed = CommandLineArgs.Parse([order[0], "source.iso", order[1], "source.iso"]);
            Assert.Equal("source.iso", canonical == "--rac2-iso" ? parsed.Rac2Iso : parsed.Rac3Iso);
            string message = Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse([order[0], "a.iso", order[1], "b.iso"])).Message;
            Assert.Contains(canonical, message); Assert.Contains(alias, message);
        }
    }

    [Theory]
    [InlineData("--record-playtest a --replay-playtest b", "--record-playtest")]
    [InlineData("--bug-capture --record-playtest a", "--bug-capture")]
    [InlineData("--bug-capture --replay-playtest a", "--bug-capture")]
    [InlineData("--bug-capture --replay-bug-report a", "--bug-capture")]
    [InlineData("--replay-forward 1", "--load-movement-state")]
    [InlineData("--replay-playtest a --capture-frame 30", "--capture-frame")]
    public void EstablishedIncompatibleModesFailBeforeStartup(string command, string diagnostic) =>
        Assert.Contains(diagnostic, Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse(command.Split(' '))).Message);

    [Fact]
    public void OmittedDefaultsAndOpaqueValuesRemainCompatible()
    {
        var defaults = CommandLineArgs.Parse([]);
        Assert.Equal(1, defaults.Rac2Level);
        Assert.Null(defaults.CaptureFrame); Assert.Null(defaults.FixedSeed);
        var parsed = CommandLineArgs.Parse(["--fixed-seed", "-2147483648", "--capture-frame", "0", "--gc-level", "17",
            "--destination", "extension:custom-world", "--rac2-iso", "a path with spaces.iso", "--overlay", "custom-layer",
            "--automation", "--smoke"]);
        Assert.Equal(int.MinValue, parsed.FixedSeed); Assert.Equal(0, parsed.CaptureFrame); Assert.Equal(17, parsed.Rac2Level);
        Assert.Equal("extension:custom-world", parsed.Destination); Assert.Equal("a path with spaces.iso", parsed.Rac2Iso);
        Assert.Equal("custom-layer", parsed.Overlay);
        Assert.Equal(3600, CommandLineArgs.Parse(["--load-movement-state", "state.json", "--replay-forward", "3600"]).ReplayForwardFrames);
    }

    [Theory]
    [InlineData("--compose")]
    [InlineData("--composition")]
    [InlineData("--composition-view")]
    [InlineData("--compose-worlds")]
    [InlineData("--compose-solve")]
    [InlineData("--compose-solve-scale")]
    [InlineData("--compose-reload")]
    public void RetiredComparisonOptionsFailExplicitly(string option) =>
        Assert.Contains("Unknown OBP argument: " + option, Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse([option])).Message);
}
