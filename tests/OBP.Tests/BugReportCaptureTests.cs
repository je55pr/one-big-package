using System.Text;
using System.Text.Json;
using OBP.Core;
using OBP.Game;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;
using OBP.Runtime.Diagnostics;

namespace OBP.Tests;

public sealed class BugReportCaptureTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "obp-bug-start-" + Guid.NewGuid().ToString("N"));
    private BugReportCapturePaths Paths => new(Path.Combine(_directory, "inbox"), Path.Combine(_directory, "profile.json"), Path.Combine(_directory, "campaign.json"));

    public BugReportCaptureTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void OrdinaryCapturePreservesProfileBytesAndProvenanceWhileChoosingItsSavedDestination()
    {
        byte[] profile = ObpProfileTests.Profile().Encode();
        File.WriteAllBytes(Paths.Profile, profile);
        var args = new CommandLineArgs { BugCapture = true, ProfilePath = Paths.Profile };
        var capture = BugReportCapture.Prepare(args, Paths, CancellationToken.None)!;
        Assert.Equal("obp:fusion-lab", capture.Args.Destination);
        Assert.Equal(args.ProfilePath, capture.Args.ProfilePath);
        Assert.Equal(args.Rac1CampaignSavePath, capture.Args.Rac1CampaignSavePath);
        Assert.Equal(Path.Combine(Paths.Inbox, "live", capture.SessionId.ToString("N"), "session.run.jsonl.gz"), capture.Args.RecordPlaytest);
        Assert.Equal(profile, File.ReadAllBytes(Paths.Profile));
        Assert.Null(capture.ReplayReport);
    }

    [Fact]
    public void UnrecoverableProfileIsRetainedAndCaptureIsRejected()
    {
        byte[] original = Encoding.UTF8.GetBytes("{\"Version\":1,\"Player\":null}");
        File.WriteAllBytes(Paths.Profile, original);
        Assert.Throws<InvalidDataException>(() => BugReportCapture.Prepare(new() { BugCapture = true }, Paths, CancellationToken.None));
        Assert.Equal(original, File.ReadAllBytes(Paths.Profile));
    }

    [Fact]
    public void ExplicitDestinationAndNoProfileKeepTheirExistingStartupSemantics()
    {
        File.WriteAllText(Paths.Profile, "invalid, must not be consulted");
        var explicitWorld = BugReportCapture.Prepare(new() { BugCapture = true, Destination = "rac3:TABLE1" }, Paths, CancellationToken.None)!;
        var noProfile = BugReportCapture.Prepare(new() { BugCapture = true, NoProfile = true }, Paths, CancellationToken.None)!;
        Assert.Equal("rac3:TABLE1", explicitWorld.Args.Destination);
        Assert.Equal("rac1:LEVEL0", noProfile.Args.Destination);
    }

    [Fact]
    public void RetailParityCaptureCopiesTheCampaignAndUsesItsCurrentDestination()
    {
        var campaign = new Rac1CampaignState();
        campaign.ApplyProgressionEvent(0x25);
        Assert.True(campaign.BeginTravel(1));
        Rac1CampaignSaveFile.Save(Paths.Campaign, campaign, Rac1WeaponInventory.CreateOpeningLoadout());
        byte[] original = File.ReadAllBytes(Paths.Campaign);

        var capture = BugReportCapture.Prepare(new() { BugCapture = true, RetailParity = true }, Paths, CancellationToken.None)!;

        string live = Path.Combine(Paths.Inbox, "live", capture.SessionId.ToString("N"));
        string isolated = Path.Combine(live, "campaign.json");
        Assert.Equal("rac1:LEVEL1", capture.Args.Destination);
        Assert.Equal(isolated, capture.Args.Rac1CampaignSavePath);
        Assert.Equal(Path.Combine(live, "session.run.jsonl.gz"), capture.Args.RecordPlaytest);
        Assert.Equal(original, File.ReadAllBytes(isolated));
        File.WriteAllText(isolated, "capture changed its isolated save");
        Assert.Equal(original, File.ReadAllBytes(Paths.Campaign));
    }

    [Fact]
    public async Task ReplayResolvesTheSelectedReportFromItsManifestInbox()
    {
        Guid sessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid reportId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid assemblyId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var inbox = new BugReportInbox(Path.Combine(_directory, "saved-reports"));
        var report = new BugReportEntry(reportId, DateTimeOffset.UnixEpoch, 0, "fixture-report", "rac1:LEVEL0", "Fixture", "Replay fixture");
        var header = new PlaytestRunHeader(PlaytestRunHeader.CurrentVersion, report.Destination, "fixture-source", assemblyId,
            "fixture-engine", "fixture-platform", "fixture-physics", 60, 1440, 960, 720, DateTimeOffset.UnixEpoch,
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), new(new Dictionary<string, PlaytestValue>()))
        {
            PlayerProfile = JsonSerializer.Deserialize<JsonElement>((ObpProfileTests.Profile() with { Destination = report.Destination }).Encode()),
            Assemblies = new Dictionary<string, Guid> { ["fixture"] = assemblyId },
            Sources = [new(ObpSourceGame.Rac1, "fixture-source", Sha256: new string('A', 64))],
            Adapters = [new("obp/shared-player/v1", 1, 60)],
        };
        using var writer = new PlaytestRunWriter(Path.Combine(_directory, "live.run.jsonl.gz"), header);
        writer.Add(new(0, PlaytestStepKind.Process, 1, 1d / 60, [], new Dictionary<string, float>(), []));
        writer.Mark(new(0, report.Marker));
        var session = await inbox.SaveAsync(sessionId, writer, report);
        var args = new CommandLineArgs
        {
            ReplayBugReport = Path.Combine(inbox.SessionDirectory(sessionId), "session.json"),
            BugReportId = reportId.ToString(),
            BugReportInbox = Paths.Inbox,
        };

        var capture = BugReportCapture.Prepare(args, Paths, CancellationToken.None)!;

        Assert.Equal(inbox.Root, capture.Inbox.Root);
        Assert.Equal(report, capture.ReplayReport);
        Assert.Equal(Guid.Empty, capture.SessionId);
        Assert.Equal(args with { ReplayPlaytest = Path.Combine(inbox.SessionDirectory(sessionId), session.Recording.FileName) }, capture.Args);
        Assert.False(Directory.Exists(Paths.Inbox));
    }

    [Theory]
    [InlineData("11111111111111111111111111111111", "report.json", "22222222-2222-2222-2222-222222222222")]
    [InlineData("invalid-session", "session.json", "22222222-2222-2222-2222-222222222222")]
    [InlineData("11111111111111111111111111111111", "session.json", "invalid-report")]
    [InlineData("11111111111111111111111111111111", "session.json", null)]
    public void ReplayRejectsInvalidManifestPathsAndReportIdentities(string session, string file, string? reportId)
    {
        var args = new CommandLineArgs { ReplayBugReport = Path.Combine(Paths.Inbox, session, file), BugReportId = reportId };

        var error = Assert.Throws<InvalidDataException>(() => BugReportCapture.Prepare(args, Paths, CancellationToken.None));

        Assert.Equal("Choose a saved report and its report identity.", error.Message);
        Assert.False(Directory.Exists(Paths.Inbox));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CaptureRejectsReplay(bool savedReport)
    {
        string manifest = Path.Combine(Paths.Inbox, "11111111111111111111111111111111", "session.json");
        var args = new CommandLineArgs
        {
            BugCapture = true,
            ReplayBugReport = savedReport ? manifest : null,
            ReplayPlaytest = savedReport ? null : Path.Combine(_directory, "session.run.jsonl.gz"),
        };

        var error = Assert.Throws<InvalidDataException>(() => BugReportCapture.Prepare(args, Paths, CancellationToken.None));

        Assert.Equal("Choose either bug capture or replay.", error.Message);
        Assert.False(Directory.Exists(Paths.Inbox));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
