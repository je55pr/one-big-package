namespace OBP.Game.Diagnostics;

public interface ICaptureEquipmentActions
{
    bool AssignQuickSelect(int slot, string item);
    bool Select(string item);
}
public sealed class ReconciliationCaptureScenario(CaptureDiagnosticContext context, ICaptureRecorderDiagnosticFixture recorder,
    IDiagnosticNavigationActions navigation, ICaptureEquipmentActions equipment, IDiagnosticAcceptanceReporter report) : CaptureScenarioSupport(context)
{
    public static System.Collections.Immutable.ImmutableArray<string> Route { get; } = ["rac1:LEVEL0", "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab", "rac1:LEVEL0"];
    public async Task RunAsync()
    {
        while (recorder.Read().WaitingForEntry) await Frames();
        var configuration = recorder.Read().Configuration ?? throw new DiagnosticUnavailableException("The capture player configuration is unavailable.");
        int visit = 0;
        foreach (string destination in Route)
        {
            await EnterAsync(destination);
            var state = recorder.Read();
            report.Assert(state.Configuration == configuration && state.GenuineAvatar && state.PlayerPresent && state.NativeProfile &&
                state.PhysicsTicksPerSecond == configuration.NativeTicksPerSecond, "same-player", "genuine Ratchet, selected native profile and physics cadence across worlds");
            report.Assert(state.HudHealth && state.HudBolts && state.HudVisible, "same-hud", "persistent health/wallet HUD visible");
            await Physics(5);
            Require(equipment.AssignQuickSelect(7, "rac3/item/43"), "capture quick slot unavailable");
            foreach (string item in new[] { "rac2/item/30", "rac3/item/43" })
            {
                Require(equipment.Select(item), "capture selection failed: " + item); int before = recorder.Read().Shots;
                Pad(CaptureButton.B, true); await Physics(item == "rac3/item/43" ? 4 : 18); Pad(CaptureButton.B, false); await Physics(65);
                report.Assert(recorder.Read().Shots > before, "recorded-native-firing", item);
            }
            Key(CaptureKey.F8, true, CaptureKeyOrigin.Physical); await Frames(); Key(CaptureKey.F8, false, CaptureKeyOrigin.Physical); await Physics(2);
            var bookmark = recorder.RequestBookmark(visit++, destination);
            for (int frame = 0; !bookmark.Exists && frame < 600; frame++) await Frames();
            report.Assert(bookmark.Exists && bookmark.ReadDestination() == destination, "universal-bookmark", "completed prefix and shared profile at " + destination);
            Log("[reconciliation-capture] selection, firing, marker: " + destination);
        }
        report.Write("capture-route"); recorder.Save();
    }
    private async Task EnterAsync(string destination)
    {
        if (recorder.Read().Destination == destination) return;
        string before = recorder.Read().ProfileJson;
        Require((await navigation.NavigateAsync(destination, Lifetime)).Accepted, "capture world entry failed");
        report.Assert(before == recorder.Read().ProfileJson, "transition-preserves-session", "player state unchanged at world commit");
        while (recorder.Read().PreparingWorld) await Frames();
    }
    public async Task VerifyFinishedContinuationAsync()
    {
        report.Assert((await navigation.NavigateAsync("rac2:LEVEL1", Lifetime)).Accepted, "finished-capture-entry", "world can change after F9 finishes the tape");
        var before = recorder.Read(); await Physics(3); var after = recorder.Read();
        report.Assert(after.Finished && !after.PreparingWorld && after.PlayerNode == before.PlayerNode && after.PhysicsTick > before.PhysicsTick,
            "finished-capture-continues", "finished recorder leaves the player and world clocks running");
        report.Write("capture-route");
    }
}
