namespace OBP.Game.Diagnostics;

internal sealed class Rac1CampaignDiagnosticHarness(MovementScenarioPorts movement, Rac1CampaignScenarioPorts ports)
{
    internal IDiagnosticScenario CreateCampaign(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1CampaignScenario(context, ports).RunAsync()),
        new("rac1-campaign-smoke-pass", 0), new("rac1-campaign-smoke-fail", 4));
    internal IDiagnosticScenario CreateMission(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1MissionPlayScenario(context, ports.Facts, ports.Save, ports.Ui).RunAsync()),
        new("rac1-mission-play-smoke-pass", 0), new("rac1-mission-play-smoke-fail", 4));
}
