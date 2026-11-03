using System.IO.Compression;
using System.Text.Json;
using OBP.Runtime.Diagnostics;

namespace OBP.Tests;

public sealed class PlaytestRunTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "obp-playtest-test-" + Guid.NewGuid().ToString("N"));
    private string RunPath => Path.Combine(_directory, "run.jsonl.gz");

    [Fact]
    public async Task Snapshot_FinalizesExactPrefixesWithoutChangingOrStoppingOriginalInputs()
    {
        string first = Path.Combine(_directory, "first.gz"), second = Path.Combine(_directory, "second.gz");
        using var writer = new PlaytestRunWriter(RunPath, Header());
        writer.Add(Step(0));
        writer.Mark(new(0, "report-one"));
        await writer.ExportSnapshotAsync(first);
        Assert.False(File.Exists(RunPath));
        writer.Add(Step(1));
        writer.Mark(new(1, "report-two"));
        await writer.ExportSnapshotAsync(second);
        writer.Add(Step(2));
        await writer.FinishAsync("closed");

        foreach (var (path, count) in new[] { (first, 1), (second, 2), (RunPath, 3) })
        {
            using var reader = new PlaytestRunReader(path);
            for (int index = 0; index < count; index++)
                Assert.Equal(JsonSerializer.Serialize(Step(index)), JsonSerializer.Serialize(reader.ReadNextStep()));
            Assert.Null(reader.ReadNextStep());
            Assert.Equal(count, reader.End!.Steps);
            Assert.Contains(new(0, "report-one"), reader.Markers);
            if (count > 1) Assert.Contains(new(1, "report-two"), reader.Markers);
        }
    }

    [Fact]
    public async Task FailedSnapshot_DoesNotOverwriteExistingFileOrStopLiveRecording()
    {
        Directory.CreateDirectory(_directory);
        string existing = Path.Combine(_directory, "existing.gz");
        await File.WriteAllTextAsync(existing, "keep");
        using var writer = new PlaytestRunWriter(RunPath, Header());
        writer.Add(Step(0));
        await Assert.ThrowsAsync<IOException>(() => writer.ExportSnapshotAsync(existing));
        Assert.Equal("keep", await File.ReadAllTextAsync(existing));
        writer.Add(Step(1));
        await writer.FinishAsync("saved");
        using var reader = new PlaytestRunReader(RunPath);
        Assert.NotNull(reader.ReadNextStep());
        Assert.NotNull(reader.ReadNextStep());
        Assert.Null(reader.ReadNextStep());
    }

    [Fact]
    public async Task CompletedSnapshot_SurvivesAbandonedLiveSession()
    {
        string snapshot = Path.Combine(_directory, "report.gz");
        using (var writer = new PlaytestRunWriter(RunPath, Header()))
        {
            writer.Add(Step(0));
            await writer.ExportSnapshotAsync(snapshot);
            writer.Add(Step(1));
        }
        Assert.False(File.Exists(RunPath));
        using var report = new PlaytestRunReader(snapshot);
        Assert.NotNull(report.ReadNextStep());
        Assert.Null(report.ReadNextStep());
        using var unfinished = new PlaytestRunReader(RunPath + ".partial");
        Assert.NotNull(unfinished.ReadNextStep());
        Assert.NotNull(unfinished.ReadNextStep());
        Assert.Throws<InvalidDataException>(() => unfinished.ReadNextStep());
    }

    [Fact]
    public void BuildIdentity_DetectsReferencedLibraryChangesAndMissingModules()
    {
        var header = Header();
        var same = header.Assemblies.ToDictionary(a => a.Key, a => a.Value);
        Assert.True(header.MatchesAssemblies(same));
        same["OBP.RAC1"] = Guid.NewGuid();
        Assert.False(header.MatchesAssemblies(same));
        same = header.Assemblies.ToDictionary(a => a.Key, a => a.Value);
        same.Remove("OBP.RAC1");
        Assert.False(header.MatchesAssemblies(same));
        Assert.Throws<InvalidDataException>(() => (header with { Assemblies = new Dictionary<string, Guid>() }).Validate());
    }

    [Fact]
    public async Task StreamedRun_RoundTripsInputsStateMarkersAndExplicitCompletion()
    {
        var step = Step(0) with
        {
            Events = [new(PlaytestInputKind.JoyAxis, 2, Code: 0, Value: .625f),
                new(PlaytestInputKind.MouseButton, 2, Code: 1, Pressed: true, X: 100, Y: 250, DoubleClick: true)],
            Width = 1000,
            Height = 700,
            Actions = new Dictionary<string, float> { ["obp_move_right"] = .625f },
            Players = [new(.625f, 0, 0, 0, false, false, false, false, false, false)],
            Checkpoint = State(4.25),
        };
        using (var writer = new PlaytestRunWriter(RunPath, Header()))
        {
            writer.Add(step);
            writer.Mark(new(0, "Bug 1"));
            await writer.FinishAsync("saved");
        }
        Assert.False(File.Exists(RunPath + ".partial"));
        using var reader = new PlaytestRunReader(RunPath);
        Assert.Equal(Header().AssemblyId, reader.Header.AssemblyId);
        var restored = reader.ReadNextStep()!;
        Assert.Equal(step.Events, restored.Events);
        Assert.Equal(step.Players, restored.Players);
        Assert.Equal((1000, 700), (restored.Width, restored.Height));
        Assert.Equal(.625f, restored.Actions["obp_move_right"]);
        Assert.Null(PlaytestCheckpoint.Compare(step.Checkpoint!, restored.Checkpoint!, 0));
        Assert.Null(reader.ReadNextStep());
        Assert.Equal(new PlaytestMarker(0, "Bug 1"), Assert.Single(reader.Markers));
        Assert.Equal(new PlaytestRunEnd(1, "saved"), reader.End);
    }

    [Fact]
    public void AbandonedWriter_RetainsPartialAndCannotBeMistakenForCompletedRun()
    {
        using (var writer = new PlaytestRunWriter(RunPath, Header())) writer.Add(Step(0));
        Assert.False(File.Exists(RunPath));
        Assert.True(File.Exists(RunPath + ".partial"));
        using var reader = new PlaytestRunReader(RunPath + ".partial");
        Assert.NotNull(reader.ReadNextStep());
        Assert.Throws<InvalidDataException>(() => reader.ReadNextStep());
        Assert.Throws<IOException>(() => new PlaytestRunWriter(RunPath, Header()));
    }

    [Fact]
    public void Observations_CompareSortedKeysAndToleranceWithoutCorrectingActualState()
    {
        var expected = new PlaytestCheckpoint(new Dictionary<string, PlaytestValue>
        {
            ["z.position"] = new([1, 2, 3], Tolerance: .002),
            ["a.state"] = new([8]),
        });
        var nearby = new PlaytestCheckpoint(new Dictionary<string, PlaytestValue>
        {
            ["a.state"] = new([8]),
            ["z.position"] = new([1.001, 2, 3]),
        });
        Assert.Null(PlaytestCheckpoint.Compare(expected, nearby, 60));
        var changed = new PlaytestCheckpoint(new Dictionary<string, PlaytestValue>
        {
            ["z.position"] = new([100, 2, 3]),
            ["a.state"] = new([9]),
        });
        var difference = PlaytestCheckpoint.Compare(expected, changed, 120)!;
        Assert.Equal(120, difference.Step);
        Assert.Equal("a.state", difference.Field);
        Assert.Equal(9, changed.Values["a.state"].Numbers![0]);
        Assert.Equal("Missing field", PlaytestCheckpoint.Compare(expected, State(0), 1)!.Reason);
    }

    [Theory]
    [InlineData("missing-end")]
    [InlineData("extra-header")]
    [InlineData("step-order")]
    [InlineData("clock-order")]
    [InlineData("event-clock")]
    [InlineData("end-count")]
    [InlineData("after-end")]
    [InlineData("ambiguous")]
    public void Reader_RejectsIncompleteOrContradictoryTapes(string fault)
    {
        var entries = new List<PlaytestRunEntry> { new(Header: Header()), new(Step: Step(0)) };
        switch (fault)
        {
            case "extra-header": entries.Add(new(Header: Header())); break;
            case "step-order": entries.Add(new(Step: Step(2))); break;
            case "clock-order": entries.Add(new(Step: Step(1) with { Milliseconds = 1 })); break;
            case "event-clock": entries.Add(new(Step: Step(1) with { Events = [new(PlaytestInputKind.Key, 0, Code: 32)] })); break;
            case "end-count": entries.Add(new(End: new(2, "saved"))); break;
            case "after-end": entries.Add(new(End: new(1, "saved"))); entries.Add(new(Step: Step(1))); break;
            case "ambiguous": entries.Add(new(Step: Step(1), End: new(1, "saved"))); break;
        }
        Write(entries);
        using var reader = new PlaytestRunReader(RunPath);
        Assert.Throws<InvalidDataException>(() => { while (reader.ReadNextStep() is not null) { } });
    }

    [Fact]
    public void Reader_RejectsUnsupportedHeaderAndOversizedExpandedRecord()
    {
        Write([new(Header: Header() with { Version = 999 })]);
        Assert.Throws<InvalidDataException>(() => new PlaytestRunReader(RunPath));
        using (var file = File.Create(RunPath))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var text = new StreamWriter(gzip)) text.WriteLine(new string(' ', 4 * 1024 * 1024 + 1));
        Assert.Throws<InvalidDataException>(() => new PlaytestRunReader(RunPath));
    }

    [Fact]
    public async Task Writer_RejectsInvalidControlsStateAndNoncontiguousSteps()
    {
        using var writer = new PlaytestRunWriter(RunPath, Header());
        Assert.Throws<InvalidDataException>(() => writer.Add(Step(1)));
        Assert.Throws<InvalidDataException>(() => writer.Add(Step(0) with { Players = [new(float.NaN, 0, 0, 0, false, false, false, false, false, false)] }));
        Assert.Throws<InvalidDataException>(() => writer.Add(Step(0) with { Checkpoint = State(double.PositiveInfinity) }));
        Assert.Throws<InvalidDataException>(() => writer.Add(Step(0) with { Events = [new(PlaytestInputKind.JoyAxis, 0, Code: 99)] }));
        Assert.Throws<InvalidDataException>(() => writer.Add(Step(0) with { Width = 0 }));
        Assert.Throws<InvalidDataException>(() => writer.Add(Step(0) with { Height = 20000 }));
        writer.Add(Step(0));
        await writer.FinishAsync("saved");
        Assert.Throws<InvalidOperationException>(() => writer.Add(Step(1)));
    }

    private void Write(IEnumerable<PlaytestRunEntry> entries)
    {
        Directory.CreateDirectory(_directory);
        using var file = File.Create(RunPath);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        using var text = new StreamWriter(gzip);
        foreach (var entry in entries) text.WriteLine(JsonSerializer.Serialize(entry));
    }

    private static PlaytestCheckpoint State(double value) => new(new Dictionary<string, PlaytestValue> { ["position"] = new([value]) });
    private static PlaytestStep Step(int index) => new(index, PlaytestStepKind.Physics, (ulong)(index + 1) * 3, 1d / 60, [], new Dictionary<string, float>(), []);
    private static PlaytestRunHeader Header() => new(1, "rac1:LEVEL0", "test-source", Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "test-engine", "test-platform", "test-physics", 60, 1440, 960, 720, DateTimeOffset.UnixEpoch,
        JsonSerializer.SerializeToElement(new { Test = true }), JsonSerializer.SerializeToElement(new { Test = true }), State(0))
    {
        Assemblies = new Dictionary<string, Guid>
        {
            ["OneBigPackage"] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ["OBP.RAC1"] = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        },
    };
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
