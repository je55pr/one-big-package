using System.Collections.Immutable;
using System.Numerics;
using OBP.Core;
using OBP.Game.Diagnostics;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1CampaignDiagnosticTests
{
    [Fact]
    public async Task MissionObserverAcceptsTheOrderedNativeFactsWithoutSynthesizingInput()
    {
        var fixture = new ObserverFixture();
        var result = await fixture.RunAsync();
        Assert.Equal(DiagnosticRunStatus.Passed, result.Status);
        Assert.Equal(0, fixture.InputWrites); Assert.Equal(0, fixture.Player.Mutations);
        Assert.Equal(new[] { "1:1", "1:1,2", "1:1,2" }, fixture.PersistedReads);
        Assert.Contains(fixture.Logs, line => line.Contains("native Veldin encounter -> Novalis", StringComparison.Ordinal));
        Assert.Contains(fixture.Logs, line => line.Contains("native Plumber purchase -> Aridia", StringComparison.Ordinal));
        Assert.Contains(fixture.Logs, line => line.Contains("death/retry, map travel", StringComparison.Ordinal));
        Assert.True(fixture.Ui.HintDisposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissionObserverRejectsDevelopmentFlightOrExternalTravel(bool flight)
    {
        var fixture = new ObserverFixture { ExternalTravel = !flight };
        fixture.Player.Flight = flight;
        var result = await fixture.RunAsync();
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status);
        Assert.Contains(flight ? "Development flight" : "External world travel", result.Detail, StringComparison.Ordinal);
        Assert.True(fixture.Ui.HintDisposed); Assert.Equal(0, fixture.InputWrites);
    }

    [Fact]
    public async Task ClosingTheObserverCancelsItsWaitAndRemovesTheHintWithoutAFalsePass()
    {
        using var stop = new CancellationTokenSource();
        var fixture = new ObserverFixture { BlockObserver = true };
        var pending = fixture.RunAsync(stop.Token);
        await fixture.Ui.Shown.Task.WaitAsync(TimeSpan.FromSeconds(10)); stop.Cancel();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(DiagnosticRunStatus.Canceled, result.Status);
        Assert.True(fixture.Ui.HintDisposed); Assert.Equal(0, fixture.InputWrites);
        Assert.DoesNotContain(fixture.Logs, line => line == "[rac1-mission-play] PASS");
    }

    [Fact]
    public async Task HeadlessFreshObservationReportsUnavailableInsteadOfClaimingHumanPlay()
    {
        var fixture = new ObserverFixture(); fixture.Ui.Headless = true;
        var result = await fixture.RunAsync();
        Assert.Equal(DiagnosticRunStatus.Unavailable, result.Status);
        Assert.Contains("needs a player", result.Detail, StringComparison.Ordinal);
        Assert.False(fixture.Ui.Shown.Task.IsCompleted); Assert.Equal(0, fixture.InputWrites);
    }

    [Theory]
    [InlineData(false, false, ObpSourceGame.Rac1, false)]
    [InlineData(true, true, ObpSourceGame.Rac1, false)]
    [InlineData(true, false, ObpSourceGame.Rac2, false)]
    [InlineData(true, false, ObpSourceGame.Rac3, false)]
    [InlineData(true, false, ObpSourceGame.Rac1, true)]
    public void CampaignRequiresExplicitSourceSaveAndRejectsTheSharedProfile(bool explicitPath, bool shared, ObpSourceGame source, bool accepted)
    {
        var save = new Rac1CampaignSaveDiagnostics(() => Provenance() with { ExplicitPath = explicitPath, SharedProfileActive = shared, Source = source },
            () => null, () => null);
        if (accepted) Rac1CampaignScenarioAssertions.IsolatedSave(save);
        else Assert.Throws<InvalidOperationException>(() => Rac1CampaignScenarioAssertions.IsolatedSave(save));
    }

    [Fact]
    public void NativeInventoryUnknownSlotsRemainExactAndSnapshotsCannotMutateTheSource()
    {
        var opening = Rac1WeaponInventory.CreateOpeningLoadout().CapturePersistentState();
        var ammo = opening.Ammo.ToArray(); var items = opening.Items.ToArray(); var flags = opening.UnlockFlags.ToArray();
        ammo[36] = 0x123456; items[36] = 0x81; flags[36] = 0xa5;
        var inventory = new Rac1WeaponInventory(opening with { Ammo = ammo, Items = items, UnlockFlags = flags });
        var restored = new Rac1CampaignRestoreResult(new(), inventory, Rac1CampaignRestoreKind.RestoredCurrentSchema);
        var frozen = Rac1CampaignSaveDiagnostics.Freeze(restored);
        using var file = new SaveFile();
        Rac1CampaignSaveFile.Save(file.Path, restored.Campaign, inventory);
        var fixture = new Rac1CampaignSaveDiagnostics(Provenance, () => file.Path, () => file.Path);
        var stored = fixture.ReadPersisted();
        Assert.Equal(0x123456, stored.Ammo[36]); Assert.Equal(0x81, stored.Items[36]); Assert.Equal(0xa5, stored.UnlockFlags[36]);
        Assert.Equal(frozen.Ammo.ToArray(), stored.Ammo.ToArray()); Assert.Equal(frozen.Items.ToArray(), stored.Items.ToArray()); Assert.Equal(frozen.UnlockFlags.ToArray(), stored.UnlockFlags.ToArray());
        items[36] = 0; flags[36] = 0; ammo[36] = 0;
        Assert.Equal(0x123456, frozen.Ammo[36]); Assert.Equal(0x81, frozen.Items[36]); Assert.Equal(0xa5, frozen.UnlockFlags[36]);
    }

    [Fact]
    public void CorruptFileWitnessDetectsOverwriteAndTemporarySaveWithoutExposingBytesOrPaths()
    {
        using var file = new SaveFile(); File.WriteAllText(file.Path, "{broken");
        var fixture = new Rac1CampaignSaveDiagnostics(Provenance, () => file.Path, () => file.Path);
        var witness = fixture.CaptureFile();
        Assert.True(witness.Unchanged); Assert.False(witness.TemporaryFileExists);
        File.WriteAllText(file.Path + ".tmp", "staged"); Assert.True(witness.TemporaryFileExists);
        File.WriteAllText(file.Path, "changed"); Assert.False(witness.Unchanged);
        File.Delete(file.Path); Assert.False(witness.Unchanged);
    }

    private static Rac1CampaignSaveProvenance Provenance() => new(ObpSourceGame.Rac1, "fixture", true, false, true, true, false, Rac1CampaignRestoreKind.DefaultedMissingState);
    private sealed class SaveFile : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "obp-campaign-diagnostic-" + Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public SaveFile() { Directory.CreateDirectory(_directory); Path = System.IO.Path.Combine(_directory, "campaign.json"); }
        public void Dispose() { File.Delete(Path); File.Delete(Path + ".tmp"); Directory.Delete(_directory); }
    }
    private sealed class Release(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class Ui : IRac1MissionDiagnosticUi
    {
        public bool Headless { get; set; }
        public bool HintDisposed { get; private set; }
        public TaskCompletionSource Shown { get; } = new();
        public IRac1MissionDiagnosticHint ShowHint() { Shown.SetResult(); return new Hint(this); }
        private sealed class Hint(Ui owner) : IRac1MissionDiagnosticHint
        { public void SetText(string instruction) { } public void Dispose() => owner.HintDisposed = true; }
    }
    private sealed class Player : IDiagnosticMovementPlayer
    {
        public bool Flight; public int Mutations;
        public DiagnosticMovementState Read() => new() { Grounded = true, Rac1MovementPhase = Rac1RatchetMovementPhase.Grounded, Rac1GameplayAlive = true, FlyEnabled = Flight };
        public IDisposable PreserveDevelopmentSettings() => new Release(() => { });
        public void ResetCollisionDiagnostics() => Mutations++;
        public void ResetToSpawn() => Mutations++;
        public void ResetAt(Vector3 position) => Mutations++;
        public void SetPosition(Vector3 position) => Mutations++;
        public void SetRotation(Vector3 rotation) => Mutations++;
        public void SetYaw(double yaw) => Mutations++;
        public void SetBackpack(Rac1TraversalEquipmentId? backpack) => Mutations++;
        public void SetClank(bool available) => Mutations++;
        public void CyclePack() => Mutations++;
        public void ToggleSwingshot() => Mutations++;
        public void SetSnapReach(float reach) => Mutations++;
        public void StageWater(Vector3 position) => Mutations++;
        public void ConfigureTargets(ImmutableArray<RuntimeDynamicObject> targets) => Mutations++;
        public void RestoreWorldTargets() => Mutations++;
        public IDisposable SubscribeDrowned(Action action) => new Release(() => { });
    }
    private sealed class Sources : IDiagnosticSources { public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => []; }
    private sealed class ObserverFixture : IRac1CampaignDiagnosticFacts, IRac1CampaignDiagnosticSave, IDiagnosticFrameClock, IDiagnosticMovementWorld
    {
        public Player Player { get; } = new(); public Ui Ui { get; } = new();
        public List<string> Logs { get; } = []; public List<string> PersistedReads { get; } = [];
        public int InputWrites; public bool ExternalTravel; public bool BlockObserver;
        private int _step;
        private int Current => _step is 2 or 3 or 4 or 5 or >= 7 ? 1 : _step == 6 ? 2 : 0;
        private int[] Admissions => _step >= 3 ? [1, 2] : _step >= 1 ? [1] : [];
        public async Task<DiagnosticRunResult> RunAsync(CancellationToken token = default)
        {
            var ports = new MovementScenarioPorts(Player, null!, null!, this, new DiagnosticInputActions(_ => true, (_, _) => InputWrites++),
                new DiagnosticPhysicalInput((_, _) => InputWrites++, (_, _) => InputWrites++), this, Logs.Add, Logs.Add);
            var definition = new DiagnosticScenarioDefinition("mission-observer-fixture/v1", DiagnosticSourceAccess.AssetFree, DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromSeconds(20), 100);
            return await new DiagnosticScenarioRunner(new Sources(), this).RunAsync(new MovementDiagnosticScenario(definition, ports,
                context => new Rac1MissionPlayScenario(context, this, this, Ui).RunAsync()), true, token);
        }
        public Rac1CampaignDiagnosticState ReadCampaign() => new(Current, Current, [.. Admissions],
            [Rac1LevelVisitState.Completed], false)
        { NovalisAvailable = _step >= 1, AridiaAvailable = _step >= 3 };
        public Rac1CampaignDeathDiagnostic ReadDeath() => new(_step == 4 ? 0 : 4, _step == 4, false, _step >= 4 ? 1 : 0);
        public bool PurchasedPlumberRetired => _step >= 3;
        public Rac1CampaignSaveProvenance ReadProvenance() => Provenance();
        public Rac1CampaignNativeSaveDiagnostic ReadPersisted()
        {
            PersistedReads.Add(Current + ":" + string.Join(',', Admissions));
            return new(Rac1CampaignRestoreKind.RestoredCurrentSchema, Current, Admissions.Length, [], [.. Admissions], [], [], [], [], [], 0, [], 0, [], []);
        }
        public IRac1CampaignFileWitness CaptureFile() => throw new InvalidOperationException("The observer must never capture/mutate the campaign file.");
        public DiagnosticFrameSnapshot ReadFrames() => new((ulong)_step, 0);
        public Task NextAsync(DiagnosticFramePhase phase, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (phase == DiagnosticFramePhase.Process && Ui.Shown.Task.IsCompleted)
            {
                if (BlockObserver) return Task.Delay(Timeout.Infinite, token);
                _step++;
            }
            return Task.CompletedTask;
        }
        public DiagnosticMovementWorld Read() => new(ObpSourceGame.Rac1, ExternalTravel && _step > 0 ? 8 : Current, null, 1, false);
        public IDisposable LimitFrameRate(int rate) => new Release(() => { });
    }
}
