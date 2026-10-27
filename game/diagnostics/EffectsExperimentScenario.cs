using System.Collections.Immutable;
using System.Text.Json;
using OBP.Core;
using OBP.Fusion;

namespace OBP.Game.Diagnostics;

internal sealed record EffectImageEvidence(string File, int Width, int Height, IReadOnlyDictionary<string, int> ChangedPixels);
internal interface IDiagnosticEffectsFixture
{
    bool Ready { get; }
    string Status { get; }
    ImmutableArray<EffectObservation> Read();
    int RenderedParticles { get; }
    int TextureCount { get; }
    int BatchCount { get; }
    string ProtectedState();
    void Start();
    void Reset();
    void StopAll();
    bool Rejects(EffectHandle handle);
    IDisposable Camera();
    void Baseline();
    EffectImageEvidence Capture(string name);
    void RememberWorld();
    bool RetiredWorldClean { get; }
}

/// <summary>Rendered, source-authorized effect acceptance; assertions and scenario flow stay outside the composition root.</summary>
internal sealed class EffectsExperimentScenario(DiagnosticScenarioDefinition definition, IDiagnosticEffectsFixture effects,
    IDiagnosticNavigationActions navigation, IDiagnosticSourceAttachmentFixture sources, string reportPath, Action<string> log) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition => definition;
    public async Task RunAsync(DiagnosticRunContext run)
    {
        var captures = new List<EffectImageEvidence>();
        var samples = new List<object>();
        await Ready(run);
        using (effects.Camera())
        {
            effects.Reset(); await run.WaitAsync(DiagnosticFramePhase.PostDraw); effects.Baseline();
            for (int repetition = 0; repetition < 10; repetition++)
            {
                effects.Start(); await Active(run);
                Require(effects.RenderedParticles > 0 && effects.TextureCount >= 6, "Qualified textures did not reach the real renderer.");
                if (repetition == 0) captures.Add(effects.Capture("fusion-lab.png"));
                samples.Add(effects.Read().Select(e => new { e.Recipe, e.State }).ToArray());
                var old = effects.Read()[0].Handle; string before = effects.ProtectedState();
                effects.Reset(); Require(before == effects.ProtectedState(), "Effect reset changed player/loadout or actor health/identity.");
                Require(effects.Read().Length == 0 && effects.TextureCount == 0 && effects.BatchCount == 0 && effects.Rejects(old), "Reset retained an effect handle or GPU resource.");
                await run.WaitAsync(DiagnosticFramePhase.PostDraw);
            }
            effects.Start(); await Active(run); effects.StopAll();
            for (int i = 0; i < 180 && effects.Read().Length != 0; i++) await run.WaitAsync(DiagnosticFramePhase.Physics);
            await run.WaitAsync(DiagnosticFramePhase.PostDraw);
            Require(effects.Read().Length == 0 && effects.TextureCount == 0 && effects.BatchCount == 0, "Stopped effects failed to drain and release resources.");
            effects.Start(); await Active(run);
            using (sources.Detach(ObpSourceGame.Rac2, refreshOnDetach: false))
            {
                await run.WaitAsync(DiagnosticFramePhase.Physics, 2); await run.WaitAsync(DiagnosticFramePhase.PostDraw);
                Require(effects.Read().All(e => e.Recipe.Source.Game != ObpSourceGame.Rac2), "Detached GC source retained live particles.");
                Require(effects.Status.Contains("rac2", StringComparison.Ordinal), "Source removal had no actionable diagnostic.");
            }
        }
        foreach (string destination in new[] { "rac1:LEVEL0", "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab" })
        {
            effects.RememberWorld();
            var result = await navigation.NavigateAsync(destination, run.CancellationToken);
            Require(result.Accepted, "World entry failed: " + destination + " " + result.Reason);
            await Ready(run); Require(effects.RetiredWorldClean, "A retired world retained effect instances or resources.");
            using var camera = effects.Camera();
            effects.Reset(); await run.WaitAsync(DiagnosticFramePhase.PostDraw); effects.Baseline();
            effects.Start(); await Active(run);
            captures.Add(effects.Capture(destination.Replace(':', '-') + ".png"));
            samples.Add(new { Destination = destination, Native = effects.Read().Select(e => new { e.Recipe, e.State }).ToArray() });
        }
        string output = Path.GetFullPath(reportPath); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Version = 1, Passed = true, Captures = captures, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
        log("[effects-smoke] PASS: six native instances, ten isolated resets, drain/source removal and four-destination re-entry.");
    }
    private async Task Ready(DiagnosticRunContext run)
    {
        for (int i = 0; i < 18000 && !effects.Ready; i++) await run.WaitAsync(DiagnosticFramePhase.Process);
        Require(effects.Ready, "Native effects did not prepare: " + effects.Status);
    }
    private async Task Active(DiagnosticRunContext run)
    {
        for (int i = 0; i < 120 && (effects.Read().Length != 6 || effects.Read().Any(e => e.State.NativeTick < 20)); i++)
            await run.WaitAsync(DiagnosticFramePhase.Physics);
        await run.WaitAsync(DiagnosticFramePhase.PostDraw);
        var states = effects.Read();
        Require(states.Length == 6 && states.All(e => e.State.ParticleCount > 0), "The six native generators are not active together.");
        Require(states.Select(e => e.Recipe.Source.Game).Distinct().Count() == 3, "The room did not preserve three source identities.");
    }
    private static void Require(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
}
