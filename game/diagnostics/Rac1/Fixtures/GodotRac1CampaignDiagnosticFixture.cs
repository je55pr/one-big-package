using Godot;
using OBP.Godot.Player;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OBP.Game.Diagnostics;

internal sealed class Rac1CampaignDiagnosticFacts(Func<Rac1CampaignDiagnosticState> campaign,
    Func<Rac1CampaignDeathDiagnostic> death, Func<bool> plumber) : IRac1CampaignDiagnosticFacts
{
    public Rac1CampaignDiagnosticState ReadCampaign() => campaign();
    public Rac1CampaignDeathDiagnostic ReadDeath() => death();
    public bool PurchasedPlumberRetired => plumber();
}
internal sealed class Rac1CampaignDiagnosticActions(Func<int, Rac1DestinationDiscoveryResult> discover,
    Func<int, Rac1PlanetTravelStartResult> travel, Action openMap, Action<int> reload, Action<double> tick, Action<string> flush) : IRac1CampaignDiagnosticActions
{
    public Rac1DestinationDiscoveryResult Discover(int nativeEvent) => discover(nativeEvent);
    public Rac1PlanetTravelStartResult Travel(int nativeDestination) => travel(nativeDestination);
    public void OpenPlanetMap() => openMap();
    public void ReloadLevel(int nativeLevel) => reload(nativeLevel);
    public void TickSaves(double delta) => tick(delta);
    public void FlushSaves(string boundary) => flush(boundary);
}
internal sealed class GodotRac1CampaignCheckpointFixture(Func<Rac1CampaignCheckpointDiagnostic> read,
    Func<Rac1CheckpointActivation, bool> activate, Func<Rac1CampaignDeathDiagnostic> death, Func<bool> restart) : IRac1CampaignCheckpointFixture
{
    public Rac1CampaignCheckpointDiagnostic Read() => read();
    public System.Numerics.Vector3 Project(RuntimeSpawn placement) => GodotMovementPlayer.Copy(RuntimeSpawnSceneAdapter.ToScenePose(placement).Position);
    public bool Activate(Rac1CheckpointActivation activation) => activate(activation);
    public Rac1CampaignDeathDiagnostic InjectEnvironmentalDeath() => death();
    public bool CompleteEnvironmentalRestart() => restart();
}
internal sealed class GodotRac1MissionDiagnosticUi(Func<CanvasLayer> ui) : IRac1MissionDiagnosticUi
{
    public bool Headless => DisplayServer.GetName() == "headless";
    public IRac1MissionDiagnosticHint ShowHint()
    {
        var label = new Label { Name = "MissionPlayCheck", Position = new(24, 72) };
        ui().AddChild(label); return new Hint(label);
    }
    private sealed class Hint(Label label) : IRac1MissionDiagnosticHint
    {
        public void SetText(string instruction) => label.Text = "MISSION PLAY CHECK\n" + instruction;
        public void Dispose() { if (GodotObject.IsInstanceValid(label) && !label.IsQueuedForDeletion()) label.QueueFree(); }
    }
}
