using System.Text.Json;
using OBP.Game.Diagnostics;

namespace OBP.Tests;

public sealed class BugReporterReplayEvidenceTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("duplicate-replay")]
    [InlineData("foreign-report")]
    [InlineData("missing-marker")]
    [InlineData("duplicate-expected")]
    [InlineData("one-replay")]
    [InlineData("three-replays")]
    [InlineData("changed-build")]
    [InlineData("diverged")]
    public void ReportReplayEvidenceMustCoverBothSavedReportIdentities(string fault)
    {
        // Synthetic result metadata checks case coverage, not native replay parity.
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var foreign = Guid.Parse("00000000-0000-0000-0000-000000000003");
        static JsonElement Result(Guid id, string outcome = "matched", bool changed = false) =>
            JsonSerializer.SerializeToElement(new { Result = outcome, BuildChanged = changed, ReportMarker = new { Id = id } });
        Guid[] expected = [first, second];
        JsonElement[] results = [Result(first), Result(second)];
        switch (fault)
        {
            case "complete": break;
            case "duplicate-replay": results[1] = Result(first); break;
            case "foreign-report": results[1] = Result(foreign); break;
            case "missing-marker": results[1] = JsonSerializer.SerializeToElement(new { Result = "matched", BuildChanged = false }); break;
            case "duplicate-expected": expected = [first, first]; break;
            case "one-replay": results = [Result(first)]; break;
            case "three-replays": results = [Result(first), Result(second), Result(foreign)]; break;
            case "changed-build": results[1] = Result(second, changed: true); break;
            case "diverged": results[1] = Result(second, "diverged"); break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
        Assert.Equal(fault == "complete", BugReporterScenario.MatchesSavedReportReplays(expected, results));
    }

}
