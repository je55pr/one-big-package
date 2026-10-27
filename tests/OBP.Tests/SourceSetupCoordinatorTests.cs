using OBP.Core;
using OBP.Game;
using OBP.IO;
using OBP.PS2;
using OBP.Tests.Helpers;

namespace OBP.Tests;

public sealed class SourceSetupCoordinatorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void OrdinaryAdmissionRequiresAllThreeAuthorities(int count, bool ready)
    {
        using var f = new Fixture(); var setup = f.Create();
        Assert.True(setup.ResolveStartup(f.Arguments(count), false));
        Assert.Equal(ready, setup.RequireOrdinary(out var state));
        Assert.Equal(count, state.Entries.Count(entry => entry.Ready));
        Assert.Equal(!ready, setup.ProfileSuspended);
    }
    [Fact]
    public void OrdinaryModeCannotBeConstructedWithAPartialRequiredSet()
    {
        using var f = new Fixture();
        Assert.Throws<ArgumentException>(() => new SourceSetupCoordinator(f.Definitions.Take(2), f.Path, SourceAccessMode.OrdinaryPlay,
            f.Files, f.Consumers, _ => { }, _ => { }));
    }
    [Fact]
    public void ExplicitDiagnosticAdmissionAndItsScopedLibraryRemainSeparateFromOrdinaryPlay()
    {
        using var f = new Fixture(); var setup = f.Create(SourceAccessMode.ExplicitDiagnostic);
        Assert.True(setup.ResolveStartup([], false)); Assert.True(setup.RequireOrdinary(out _));
        var original = setup.Library; var isolated = new ObpSourceLibrary(f.Definitions);
        setup.ReplaceDiagnosticLibrary(isolated); Assert.Same(isolated, setup.Library);
        setup.ReplaceDiagnosticLibrary(original); Assert.Same(original, setup.Library);
        Assert.False(setup.Due(60, true));
        Assert.Throws<InvalidOperationException>(() => f.Create().ReplaceDiagnosticLibrary(isolated));
    }
    [Fact]
    public void RepeatedChecksAndLaunchesNeverSaveOrRepeatUnchangedBoundedProbes()
    {
        using var f = new Fixture(); var setup = f.Create();
        Assert.True(setup.ResolveStartup(f.Arguments(3), true));
        Assert.True(setup.RequireOrdinary(out _));
        int opened = f.Files.Opened, saved = f.Files.Saved;
        for (int i = 0; i < 100; i++) { setup.Initialize(); Assert.True(setup.RequireOrdinary(out _)); }
        Assert.Equal(opened, f.Files.Opened); Assert.Equal(saved, f.Files.Saved); Assert.Equal(1, f.Files.Loaded);
        var restarted = f.Create(); restarted.Initialize(); Assert.True(restarted.RequireOrdinary(out _));
        Assert.Equal(saved, f.Files.Saved); Assert.False(restarted.RepairNeeded);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectedConfigurationRetainsBothCopiesUntilExplicitReadyRepair(bool validBackup)
    {
        using var f = new Fixture();
        var writer = f.Create(); writer.ResolveStartup(f.Arguments(3), true);
        string good = File.ReadAllText(f.Path);
        File.WriteAllText(f.Path, "{truncated"); File.WriteAllText(f.Path + ".bak", validBackup ? good : "broken backup");
        int saved = f.Files.Saved;
        var setup = f.Create(); setup.Initialize();
        Assert.True(setup.RepairNeeded); Assert.False(setup.RequireOrdinary(out _));
        Assert.Equal("{truncated", File.ReadAllText(f.Path)); Assert.Equal(saved, f.Files.Saved);
        if (!validBackup)
        {
            Assert.False(setup.Complete().Accepted); Assert.Equal(saved, f.Files.Saved); return;
        }
        Assert.Equal(3, setup.Library.Attached.Count);
        Assert.True(setup.Complete().Accepted); Assert.Equal(saved + 1, f.Files.Saved);
        Assert.False(setup.RepairNeeded); Assert.True(setup.RequireOrdinary(out _));
        Assert.Equal(good, File.ReadAllText(f.Path + ".bak"));
    }
    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("wrong-game")]
    public void SourceVersionChangesInvalidateConsumersBeforeOrdinaryAdmission(string change)
    {
        using var f = new Fixture(); var setup = f.Create(); setup.ResolveStartup(f.Arguments(3), false);
        Assert.True(setup.RequireOrdinary(out _)); int invalidated = f.Consumers.Invalidated.Count;
        var old = f.Files.Entries["Rac2.iso"];
        if (change == "missing") f.Files.Entries.Remove("Rac2.iso");
        else f.Files.Entries["Rac2.iso"] = old with
        {
            Bytes = change == "wrong-game" ? f.Files.Entries["Rac3.iso"].Bytes : old.Bytes,
            Identity = old.Identity with { Written = 2 }
        };
        Assert.False(setup.RequireOrdinary(out var state)); Assert.True(setup.ProfileSuspended);
        Assert.Equal(invalidated + 1, f.Consumers.Invalidated.Count); Assert.Equal(ObpSourceGame.Rac2, f.Consumers.Invalidated.Last());
        Assert.Equal(change == "changed", state.Ready); Assert.Equal(3, setup.Library.Attached.Count);
        setup.Readiness(); Assert.Equal(invalidated + 1, f.Consumers.Invalidated.Count);
        if (change == "changed")
        {
            int prepared = f.Consumers.Prepared;
            Assert.True(setup.Complete().Accepted); Assert.Equal(prepared + 1, f.Consumers.Prepared);
            Assert.True(setup.RequireOrdinary(out _)); Assert.False(setup.ProfileSuspended);
        }
        else { Assert.False(setup.Complete().Accepted); Assert.True(setup.ProfileSuspended); }
    }
    [Fact]
    public void WrongGameAndInvalidStartupCannotOverwriteTheAdmittedSource()
    {
        using var f = new Fixture(); var setup = f.Create(); setup.ResolveStartup(f.Arguments(3), false);
        var admitted = setup.Library.Get(ObpSourceGame.Rac1); int changed = f.Consumers.Invalidated.Count;
        Assert.False(setup.Attach(ObpSourceGame.Rac1, "Rac2.iso").Accepted);
        Assert.Same(admitted, setup.Library.Get(ObpSourceGame.Rac1)); Assert.Equal(changed, f.Consumers.Invalidated.Count);
        var invalid = f.Create();
        Assert.False(invalid.ResolveStartup([(ObpSourceGame.Rac1, "missing.iso")], true));
        Assert.False(invalid.StartupResolved); Assert.True(invalid.ProfileSuspended);
    }
    [Fact]
    public void ReplacementAndForgetKeepSessionStateExplicitWhenPersistenceFails()
    {
        using var f = new Fixture(); var setup = f.Create(); setup.ResolveStartup(f.Arguments(3), true);
        Assert.True(setup.RequireOrdinary(out _));
        f.Files.Entries["replacement.iso"] = f.Files.Entries["Rac2.iso"];
        Assert.True(setup.Attach(ObpSourceGame.Rac2, "replacement.iso").Accepted);
        Assert.True(setup.ProfileSuspended); Assert.True(setup.ConsumersInvalidated);
        Assert.True(setup.Complete().Accepted); Assert.True(setup.RequireOrdinary(out _));
        f.Files.SaveFailure = new IOException("Read-only document."); string saved = File.ReadAllText(f.Path);
        var result = setup.Forget(ObpSourceGame.Rac2);
        Assert.False(result.Accepted); Assert.Null(setup.Library.Get(ObpSourceGame.Rac2));
        Assert.True(setup.ProfileSuspended); Assert.Equal(saved, File.ReadAllText(f.Path));
    }
    [Theory]
    [InlineData("io")]
    [InlineData("denied")]
    [InlineData("invalid-data")]
    [InlineData("invalid-path")]
    public void AttachPersistenceFailureRetainsAnExplicitSessionAttachmentWithoutRepeatedInvalidation(string failure)
    {
        using var f = new Fixture(); var setup = f.Create(); setup.ResolveStartup(f.Arguments(3), true);
        Assert.True(setup.RequireOrdinary(out _));
        string saved = File.ReadAllText(f.Path); int invalidated = f.Consumers.Invalidated.Count;
        f.Files.Entries["replacement.iso"] = f.Files.Entries["Rac2.iso"];
        f.Files.SaveFailure = failure switch
        {
            "io" => new IOException("Read-only document."),
            "denied" => new UnauthorizedAccessException("Access denied."),
            "invalid-data" => new InvalidDataException("Invalid document."),
            _ => new ArgumentException("Invalid config path."),
        };
        var result = setup.Attach(ObpSourceGame.Rac2, "replacement.iso");
        Assert.True(result.Accepted); Assert.True(result.PersistenceFailed);
        Assert.Contains("Attached for this session", result.Warning);
        Assert.Equal(result.Warning, setup.RestoreWarnings[ObpSourceGame.Rac2]);
        Assert.Equal("replacement.iso", setup.Library.Get(ObpSourceGame.Rac2)!.Path);
        Assert.True(setup.ProfileSuspended); Assert.True(setup.ConsumersInvalidated);
        Assert.True(setup.Readiness().Ready);
        Assert.Equal(invalidated + 1, f.Consumers.Invalidated.Count);
        Assert.Equal(saved, File.ReadAllText(f.Path));
    }
    [Theory]
    [InlineData(SourceAccessMode.OrdinaryPlay)]
    [InlineData(SourceAccessMode.ExplicitDiagnostic)]
    public void StartupPersistenceFailureStillRejectsTheInvocationAndRetainsCurrentObservation(SourceAccessMode mode)
    {
        using var f = new Fixture(); var setup = f.Create(mode);
        f.Files.SaveFailure = new IOException("Read-only document.");
        Assert.False(setup.ResolveStartup(f.Arguments(3), true));
        Assert.False(setup.StartupResolved); Assert.True(setup.ProfileSuspended);
        Assert.NotNull(setup.Library.Get(ObpSourceGame.Rac1));
        int invalidated = f.Consumers.Invalidated.Count;
        setup.Readiness(); Assert.Equal(invalidated, f.Consumers.Invalidated.Count);
    }
    [Fact]
    public void OnlyResolvedOrdinarySurfacesAccumulatePeriodicChecks()
    {
        using var f = new Fixture(); var setup = f.Create();
        Assert.False(setup.Due(10, true)); setup.ResolveStartup(f.Arguments(3), false);
        Assert.False(setup.Due(10, false)); Assert.False(setup.Due(.75, true));
        Assert.True(setup.Due(.25, true)); Assert.False(setup.Due(.25, true));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "obp-source-owner-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "sources.json");
        public Storage Files { get; } = new();
        public Consumers Consumers { get; } = new();
        public ObpSourceDefinition[] Definitions { get; }
        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            var serials = new[] { "SCUS_971.99", "SCUS_972.68", "SCUS_973.53" };
            Definitions = new[] { ObpSourceGame.Rac1, ObpSourceGame.Rac2, ObpSourceGame.Rac3 }.Select((game, index) =>
            {
                var bytes = SyntheticPs2.Build(serials[index]).Bytes;
                Files.Entries[game + ".iso"] = new(bytes, new(bytes.Length, 1, 1));
                return new ObpSourceDefinition(game.ToString(), new(game, "fixture", Serial: serials[index].Replace('_', '-').Replace(".", "")), bytes.Length);
            }).ToArray();
        }
        public SourceSetupCoordinator Create(SourceAccessMode access = SourceAccessMode.OrdinaryPlay) => new(Definitions, Path, access, Files, Consumers, _ => { }, _ => { });
        public IEnumerable<(ObpSourceGame, string?)> Arguments(int count) => Definitions.Take(count).Select(source => (source.Game, (string?)(source.Game + ".iso")));
        public void Dispose() => Directory.Delete(_directory, true);
    }
    private sealed record Entry(byte[] Bytes, SourceFileIdentity Identity);
    private sealed class Storage : ISourceSetupStorage
    {
        public Dictionary<string, Entry> Entries { get; } = [];
        public int Opened, Saved, Loaded;
        public Exception? SaveFailure;
        public bool Exists(string path) => Entries.ContainsKey(path);
        public SourceFileIdentity Inspect(string path) => Entries.TryGetValue(path, out var entry) ? entry.Identity : throw new FileNotFoundException("Source missing.");
        public IRandomAccessReader Open(string path) { Opened++; return new InMemoryReader(path, Entries[path].Bytes); }
        public DocumentRead<ObpSavedSource[]> ReadConfiguration(string path) { Loaded++; return ObpSourceLibrary.ReadConfig(path); }
        public void SaveConfiguration(ObpSourceLibrary library, string path) { if (SaveFailure is { } failure) throw failure; Saved++; library.SaveConfig(path); }
    }
    private sealed class Consumers : ISourceSetupConsumers
    {
        public List<ObpSourceGame> Invalidated { get; } = [];
        public int Prepared;
        public string? RefreshPresentation(ObpAttachedSource? source, IRandomAccessReader? reader) => null;
        public void Invalidate(ObpSourceGame game) => Invalidated.Add(game);
        public void Prepare(bool force) => Prepared++;
    }
}
