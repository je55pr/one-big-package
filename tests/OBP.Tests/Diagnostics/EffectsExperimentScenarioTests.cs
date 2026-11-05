using System.Collections.Immutable;
using System.Text.Json;
using OBP.Core;
using OBP.Fusion;
using OBP.Game.Diagnostics;
using OBP.Runtime.Effects;

namespace OBP.Tests;

public sealed class EffectsExperimentScenarioTests
{
    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, false, true)]
    public async Task SyntheticObservationsRequireAdvancingTrilogyClocks(bool frozen, bool foreignSource, bool oneLagging, bool incomplete, bool extraSource)
    {
        string directory = Path.Combine(Path.GetTempPath(), "obp-effects-clock-" + Guid.NewGuid().ToString("N"));
        string report = Path.Combine(directory, "report.json");
        try
        {
            // Synthetic port observations only; no renderer, retail assets or native parity is claimed.
            var fixture = new Fixture(frozen, foreignSource, oneLagging, incomplete, extraSource);
            if (foreignSource)
            {
                fixture.Start();
                var substituted = fixture.Read();
                Assert.Equal(6, substituted.Length);
                Assert.Equal(3, substituted.Select(observation => observation.Recipe.Source.Game).Distinct().Count());
                Assert.DoesNotContain(substituted, observation => observation.Recipe.Source.Game == ObpSourceGame.Rac1);
                Assert.Equal(2, substituted.Count(observation => observation.Recipe.Source.Game == ObpSourceGame.Synthetic));
                fixture.Reset();
            }
            if (extraSource)
            {
                fixture.Start();
                var extra = fixture.Read();
                Assert.Equal(6, extra.Length);
                Assert.Equal(4, extra.Select(observation => observation.Recipe.Source.Game).Distinct().Count());
                foreach (var game in new[] { ObpSourceGame.Rac1, ObpSourceGame.Rac2, ObpSourceGame.Rac3, ObpSourceGame.Synthetic })
                    Assert.Contains(extra, observation => observation.Recipe.Source.Game == game);
                fixture.Reset();
            }
            var definition = new DiagnosticScenarioDefinition("synthetic-effects-clock", DiagnosticSourceAccess.AssetFree,
                DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromSeconds(10), 10000);
            var scenario = new EffectsExperimentScenario(definition, fixture, fixture, fixture, report, _ => { });
            using var run = new DiagnosticRunContext(fixture, CancellationToken.None, 10000);
            if (frozen || foreignSource || oneLagging || incomplete || extraSource)
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.RunAsync(run));
                Assert.Contains(foreignSource || extraSource ? "all three trilogy source identities" : "twenty active ticks together", error.Message);
                Assert.Equal(frozen || oneLagging || incomplete ? 120 : 20, fixture.PhysicsWaits);
                Assert.Equal(0, fixture.Captures);
                Assert.False(File.Exists(report));
            }
            else
            {
                await scenario.RunAsync(run);
                Assert.Equal(5, fixture.Captures);
                using var document = JsonDocument.Parse(File.ReadAllText(report));
                Assert.True(document.RootElement.GetProperty("Passed").GetBoolean());
                Assert.Equal(5, document.RootElement.GetProperty("Captures").GetArrayLength());
                Assert.Equal(14, document.RootElement.GetProperty("Samples").GetArrayLength());
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private sealed class Fixture(bool frozen, bool foreignSource, bool oneLagging, bool incomplete, bool extraSource) : IDiagnosticEffectsFixture, IDiagnosticFrameClock,
        IDiagnosticNavigationActions, IDiagnosticSourceAttachmentFixture
    {
        private bool _active, _detached;
        private long _tick;
        private static readonly ObpSourceGame[] Games = [ObpSourceGame.Rac1, ObpSourceGame.Rac2, ObpSourceGame.Rac3];
        public int PhysicsWaits { get; private set; }
        public int Captures { get; private set; }
        public bool Ready => true;
        public string Status => "synthetic rac2 source removal";
        public int RenderedParticles => _active ? 6 : 0;
        public int TextureCount => _active ? 6 : 0;
        public int BatchCount => _active ? 3 : 0;
        public bool RetiredWorldClean => true;
        public ImmutableArray<EffectObservation> Read() => !_active ? [] : [.. Enumerable.Range(0, incomplete ? 5 : 6)
            .Select(index => new EffectObservation(new("synthetic", 0, 0, index),
                new(new((foreignSource && index % 3 == 0) || (extraSource && index == 0) ? ObpSourceGame.Synthetic : Games[index % 3],
                    "synthetic", "fixture", "effect", index.ToString()), "synthetic"),
                new RuntimeEffectState(oneLagging ? Math.Min(_tick, index == 0 ? 19 : 20) : _tick, 1, true, 1, 1, 1)))
            .Where(observation => !_detached || observation.Recipe.Source.Game != ObpSourceGame.Rac2)];
        public string ProtectedState() => "synthetic protected player state";
        public void Start() { _active = true; _tick = 0; }
        public void Reset() => _active = false;
        public void StopAll() => _active = false;
        public bool Rejects(EffectHandle _) => true;
        public IDisposable Camera() => new Release(() => { });
        public void Baseline() { /* Asset-free observations have no renderer baseline. */ }
        public EffectImageEvidence Capture(string name) { Captures++; return new(name, 1, 1, new Dictionary<string, int>()); }
        public void RememberWorld() { /* This synthetic fixture retains no world lease. */ }
        public DiagnosticFrameSnapshot ReadFrames() => new(0, (ulong)PhysicsWaits);
        public Task NextAsync(DiagnosticFramePhase phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (phase == DiagnosticFramePhase.Physics) { PhysicsWaits++; if (_active && !frozen) _tick++; }
            return Task.CompletedTask;
        }
        public Task<DiagnosticActionResult> NavigateAsync(string _, CancellationToken __)
            => Task.FromResult(new DiagnosticActionResult(true, null));
        public IDisposable Detach(ObpSourceGame game, bool _)
        {
            Assert.Equal(ObpSourceGame.Rac2, game);
            _detached = true;
            return new Release(() => _detached = false);
        }
    }
    private sealed class Release(Action release) : IDisposable { public void Dispose() => release(); }
}
