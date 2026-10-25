using System.Text.Json;
using Godot;
using OBP.Godot;

namespace OneBigPackage;

public partial class OBPGame
{
    private sealed record AcceptanceCheck(string Id, string? Destination, bool Passed, string Detail);
    private readonly List<AcceptanceCheck> _acceptanceChecks = [];
    private void FinishAcceptanceFailure(string phase, Exception error, string reason)
    {
        try { WriteAcceptanceEvidence(phase, error); }
        catch (Exception writeError) { GD.PrintErr("[acceptance] evidence unavailable: " + writeError.Message); }
        finally { GD.PrintErr("[acceptance] FAIL: " + error); ApplicationLifecycle.RequestQuit(this, reason, 2); }
    }
    private void AcceptanceAssert(bool passed, string id, string detail)
    {
        _acceptanceChecks.Add(new(id, _currentDestinationId, passed, detail));
        Require(passed, id + ": " + detail);
    }

    private void WriteAcceptanceEvidence(string phase, Exception? failure = null)
    {
        if (_args.AcceptanceOut is not { } path) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Version = 1,
            Phase = phase,
            Result = failure is null && _acceptanceChecks.Count > 0 && _acceptanceChecks.All(check => check.Passed) ? "passed" : "failed",
            Error = failure?.Message,
            Headless = DisplayServer.GetName() == "headless",
            Checks = _acceptanceChecks,
            Sources = _sources.Attached.Values.Select(source => source.Identity),
            Configuration = _playerSession?.Capture().Configuration,
            Equipment = ReadPlayerEquipment().Select(entry => new { entry.Definition.Id, SourceGame = OBP.Core.SourceIdentityWire.FormatSource(entry.Definition.SourceGame), entry.Definition.Implementation, entry.Usability, entry.Reason }),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
