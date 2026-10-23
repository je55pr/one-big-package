using System.IO.Compression;
using System.Text.Json;
using OBP.Runtime.Diagnostics;

namespace OBP.Tests;

public sealed class BugReportInboxTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "obp-bug-report-test-" + Guid.NewGuid().ToString("N"));
    private BugReportInbox Inbox => new(Path.Combine(_directory, "inbox"));
    private string Run => Path.Combine(_directory, "live.run.jsonl.gz");

    [Fact]
    public void InterruptedCaptures_AreCountedSeparatelyAndNeverListedAsReadyReports()
    {
        var inbox = Inbox;
        Guid old = Guid.NewGuid(), current = Guid.NewGuid();
        foreach (var id in new[] { old, current })
        {
            string directory = Path.Combine(inbox.Root, "live", id.ToString("N"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "session.run.jsonl.gz.partial"), "unfinished");
        }
        Assert.Equal(1, inbox.InterruptedCaptureCount(current));
        Assert.Empty(inbox.List());
    }

    [Fact]
    public async Task Reports_PublishReplayablePrefixesAndShareOneRecordingAcrossMarkers()
    {
        var inbox = Inbox;
        Guid sessionId = Guid.NewGuid();
        using var writer = new PlaytestRunWriter(Run, Header());
        var one = AddReport(writer, 0, "first");
        await inbox.SaveAsync(sessionId, writer, one);
        var first = inbox.Resolve(sessionId, one.Id);
        Assert.Equal(1, first.Session.Recording.Steps);
        Assert.False(File.Exists(Run));
        var two = AddReport(writer, 1, "second");
        await inbox.SaveAsync(sessionId, writer, two);
        var after = inbox.Resolve(sessionId, one.Id);
        Assert.Equal(2, after.Session.Recording.Steps);
        Assert.Equal(first.Report, after.Report);
        Assert.Equal(after.RecordingPath, inbox.Resolve(sessionId, two.Id).RecordingPath);
        Assert.Single(Directory.GetFiles(inbox.SessionDirectory(sessionId), "*.gz"));
        Assert.Single(inbox.List());
        // No finalization gesture required, and both reports survive abandonment.
    }

    [Fact]
    public async Task PortableExport_ContainsOnlySelectedReportAndCompletedTape_ImportsAndReplays()
    {
        var inbox = Inbox;
        Guid id = Guid.NewGuid();
        using var writer = new PlaytestRunWriter(Run, Header());
        var one = AddReport(writer, 0, "selected");
        await inbox.SaveAsync(id, writer, one);
        await inbox.SaveAsync(id, writer, AddReport(writer, 1, "private other description"));
        string archive = inbox.Export(id, one.Id);
        Assert.InRange(new FileInfo(archive).Length, 1, BugReportInbox.MaximumArchiveBytes);
        using (var zip = ZipFile.OpenRead(archive))
        {
            Assert.Equal(2, zip.Entries.Count);
            using var text = new StreamReader(zip.GetEntry("report.json")!.Open());
            string manifest = text.ReadToEnd();
            Assert.DoesNotContain("private other description", manifest);
            Assert.DoesNotContain(_directory.Replace("\\", "\\\\"), manifest);
            Assert.DoesNotContain("Iso", manifest, StringComparison.OrdinalIgnoreCase);
        }
        var importedInbox = new BugReportInbox(Path.Combine(_directory, "developer"));
        var imported = importedInbox.Import(archive);
        var resolved = importedInbox.Resolve(imported.Id, one.Id);
        Assert.Equal(one, Assert.Single(resolved.Session.Reports));
        using var reader = new PlaytestRunReader(resolved.RecordingPath);
        Assert.Equal(0, reader.ReadNextStep()!.Index);
        Assert.Equal(1, reader.ReadNextStep()!.Index);
        Assert.Null(reader.ReadNextStep());
        Assert.Contains(new(one.Step, one.Marker), reader.Markers);
    }

    [Fact]
    public async Task FailedManifestPublication_LeavesNoReadyReport_AndLiveWriterContinues()
    {
        var inbox = Inbox;
        Guid id = Guid.NewGuid();
        string obstruction = Path.Combine(inbox.SessionDirectory(id), "session.json");
        Directory.CreateDirectory(obstruction);
        using var writer = new PlaytestRunWriter(Run, Header());
        var report = AddReport(writer, 0, "retry");
        var failure = await Record.ExceptionAsync(() => inbox.SaveAsync(id, writer, report));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Null(Assert.Single(inbox.List()).Session);
        Assert.Empty(Directory.GetFiles(inbox.SessionDirectory(id), "*.gz"));
        Directory.Delete(obstruction);
        writer.Add(Step(1));
        await inbox.SaveAsync(id, writer, report);
        Assert.Equal(2, inbox.Resolve(id, report.Id).Session.Recording.Steps);
    }

    [Fact]
    public async Task ChangedOrTruncatedRecording_IsNeverOfferedAsValidReplay()
    {
        var inbox = Inbox;
        Guid id = Guid.NewGuid();
        using var writer = new PlaytestRunWriter(Run, Header());
        var report = AddReport(writer, 0, "changed");
        await inbox.SaveAsync(id, writer, report);
        string path = inbox.Resolve(id, report.Id).RecordingPath;
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        Assert.Throws<InvalidDataException>(() => inbox.Resolve(id, report.Id));
        Assert.Throws<InvalidDataException>(() => inbox.Export(id, report.Id));
    }

    [Fact]
    public async Task ExportSizeLimit_IsEnforcedWithoutDiscardingTheLocalReport()
    {
        var inbox = Inbox;
        Guid id = Guid.NewGuid();
        using var writer = new PlaytestRunWriter(Run, Header());
        var report = AddReport(writer, 0, "large");
        await inbox.SaveAsync(id, writer, report);
        Assert.Throws<IOException>(() => inbox.Export(id, report.Id, maximumBytes: 256));
        Assert.Equal(report, inbox.Resolve(id, report.Id).Report);
        Assert.Throws<ArgumentOutOfRangeException>(() => inbox.Export(id, report.Id, maximumBytes: long.MaxValue));
    }

    [Theory]
    [InlineData("../outside.run.jsonl.gz")]
    [InlineData("C:\\personal\\run.jsonl.gz")]
    public async Task ManifestPaths_CannotEscapeSessionDirectory(string malicious)
    {
        var inbox = Inbox;
        Guid id = Guid.NewGuid();
        using var writer = new PlaytestRunWriter(Run, Header());
        var report = AddReport(writer, 0, "path");
        var session = await inbox.SaveAsync(id, writer, report);
        await File.WriteAllTextAsync(Path.Combine(inbox.SessionDirectory(id), "session.json"),
            JsonSerializer.Serialize(session with { Recording = session.Recording with { FileName = malicious } }));
        Assert.Throws<InvalidDataException>(() => inbox.Read(id));
    }

    [Fact]
    public async Task Import_RejectsExtraFiles_AndDeleteIsExplicitAndSessionScoped()
    {
        var inbox = Inbox;
        Guid id = Guid.NewGuid();
        using var writer = new PlaytestRunWriter(Run, Header());
        var report = AddReport(writer, 0, "delete");
        await inbox.SaveAsync(id, writer, report);
        string archive = inbox.Export(id, report.Id);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update)) zip.CreateEntry("unwanted.iso");
        Assert.Throws<InvalidDataException>(() => inbox.Import(archive));
        string unrelated = Path.Combine(inbox.SessionDirectory(id), "notes.txt");
        await File.WriteAllTextAsync(unrelated, "keep");
        inbox.Delete(id);
        Assert.True(File.Exists(unrelated));
        Assert.False(File.Exists(Path.Combine(inbox.SessionDirectory(id), "session.json")));
    }

    private static BugReportEntry AddReport(PlaytestRunWriter writer, int index, string description)
    {
        Guid id = Guid.NewGuid();
        var report = new BugReportEntry(id, DateTimeOffset.UtcNow, index, "report-" + id.ToString("N"),
            "rac1:LEVEL0", "Bug report", description);
        writer.Add(Step(index));
        writer.Mark(new(index, report.Marker));
        return report;
    }

    private static PlaytestStep Step(int index) => new(index, PlaytestStepKind.Process, (ulong)(index + 1),
        1d / 60, [], new Dictionary<string, float>(), []);
    private static PlaytestRunHeader Header() => new(1, "rac1:LEVEL0", "test-source", Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "test-engine", "test-platform", "test-physics", 60, 1440, 960, 720, DateTimeOffset.UnixEpoch,
        JsonSerializer.SerializeToElement(new { Level = 0 }), JsonSerializer.SerializeToElement(new { Invert = false }),
        new(new Dictionary<string, PlaytestValue>()))
    { Assemblies = new Dictionary<string, Guid> { ["OneBigPackage"] = Guid.Parse("11111111-1111-1111-1111-111111111111") } };

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
