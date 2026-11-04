namespace OBP.Game.Diagnostics;

/// <summary>The existing asset-free CI startup assertion, with the same six process-frame settling phase.</summary>
public sealed class SourceFreeStartupScenario(IDiagnosticSceneObservations scene) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = new("godot-ci-startup/v1", DiagnosticSourceAccess.AssetFree,
        DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromSeconds(30), 6);

    public async Task RunAsync(DiagnosticRunContext run)
    {
        await run.WaitAsync(DiagnosticFramePhase.Process, 6);
        var snapshot = scene.ReadScene();
        if (!snapshot.RootLoaded || !snapshot.SourceBootstrapPresent || !snapshot.UiPresent || !snapshot.SourceManagerPresent)
            throw new InvalidOperationException("Asset-free startup failed to display the Game Sources UI");
    }
}
