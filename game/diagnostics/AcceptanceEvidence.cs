using System.Collections.Immutable;
using System.Text.Json;
using OBP.Core;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

public sealed record AcceptanceCheck(string Id, string? Destination, bool Passed, string Detail);
public sealed record AcceptanceEquipment(string Id, [property: System.Text.Json.Serialization.JsonConverter(typeof(SourceGameWireJsonConverter))] ObpSourceGame SourceGame,
    EquipmentImplementation Implementation, EquipmentUsability Usability, string? Reason);
public sealed record AcceptanceSnapshot(bool Headless, ImmutableArray<ObpBuildIdentity> Sources,
    PlayerSessionConfiguration? Configuration, ImmutableArray<AcceptanceEquipment> Equipment);

/// <summary>One explicit diagnostic run's version-1 evidence and ordered assertions.</summary>
public sealed class AcceptanceEvidence
{
    private readonly List<AcceptanceCheck> _checks = [];
    public ImmutableArray<AcceptanceCheck> Checks => [.. _checks];
    public static void FinishFailure(Exception error, string reason, Action write, Action<string> logError, Action<string, int> quit)
    {
        try { write(); }
        catch (Exception writeError) { logError("[acceptance] evidence unavailable: " + writeError.Message); }
        finally
        {
            try { logError("[acceptance] FAIL: " + error); }
            finally { quit(reason, 2); }
        }
    }
    public void Assert(bool passed, string id, string? destination, string detail)
    {
        _checks.Add(new(id, destination, passed, detail));
        if (!passed) throw new InvalidOperationException(id + ": " + detail);
    }

    public void Write(string? path, string phase, AcceptanceSnapshot snapshot, Exception? failure = null)
    {
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Version = 1,
            Phase = phase,
            Result = failure is null && _checks.Count > 0 && _checks.All(check => check.Passed) ? "passed" : "failed",
            Error = failure?.Message,
            snapshot.Headless,
            Checks = _checks,
            snapshot.Sources,
            snapshot.Configuration,
            snapshot.Equipment,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
