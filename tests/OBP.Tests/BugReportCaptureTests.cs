using System.Text;
using OBP.Game;

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

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
