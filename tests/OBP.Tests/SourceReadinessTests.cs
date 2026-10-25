using OBP.Core;
using OBP.IO;
using OBP.PS2;
using OBP.Tests.Helpers;
using OneBigPackage;

namespace OBP.Tests;

public sealed class SourceReadinessTests
{
    private sealed class Files : ISourceValidationFiles
    {
        internal readonly Dictionary<string, (byte[] Bytes, SourceFileIdentity Stamp)> Entries = [];
        internal int Opens;
        public SourceFileIdentity Inspect(string path) => Entries.TryGetValue(path, out var entry) ? entry.Stamp : throw new FileNotFoundException("Disc missing.");
        public IRandomAccessReader Open(string path) { Opens++; return new InMemoryReader(path, Entries[path].Bytes); }
    }

    private sealed class Fixture
    {
        internal readonly Files Files = new();
        internal readonly ObpSourceDefinition[] Required;
        internal readonly ObpSourceLibrary Library;
        internal readonly SourceReadinessEvaluator Check;
        internal Fixture(int attached = 3, bool wrongBuild = false)
        {
            var serials = new[] { "SCUS_971.99", "SCUS_972.68", "SCUS_973.53" };
            Required = new[] { ObpSourceGame.Rac1, ObpSourceGame.Rac2, ObpSourceGame.Rac3 }.Select((game, index) =>
            {
                var fixture = SyntheticPs2.Build(serials[index]);
                Files.Entries[game + ".iso"] = (fixture.Bytes, new(fixture.Bytes.Length, 1, 1));
                return new ObpSourceDefinition(game.ToString(), new(game, "expected", Serial: serials[index].Replace('_', '-').Replace(".", "")), fixture.Bytes.Length);
            }).ToArray();
            Library = new(Required.Select(required => wrongBuild ? required with { Authority = required.Authority with { BuildId = "wrong" } } : required));
            foreach (var required in Required.Take(attached))
                Library.Attach(required.Game + ".iso", new InMemoryReader("fixture.iso", Files.Entries[required.Game + ".iso"].Bytes));
            Check = new(Library, Required, Files);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void APartialTrilogyNeverBecomesReady(int attached)
    {
        var fixture = new Fixture(attached);
        var result = fixture.Check.Evaluate();
        Assert.False(result.Ready);
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(attached, result.Entries.Count(entry => entry.Ready));
        Assert.All(result.Entries.Where(entry => !entry.Ready), entry => Assert.NotNull(entry.Problem));
    }

    [Fact]
    public void AllExactAuthoritiesAreRequiredAndUnchangedFilesAreNotReprobed()
    {
        var fixture = new Fixture();
        for (int check = 0; check < 100; check++) Assert.True(fixture.Check.Evaluate().Ready);
        Assert.Equal(3, fixture.Files.Opens);
        var wrong = new Fixture(wrongBuild: true);
        Assert.False(wrong.Check.Evaluate().Ready);
        Assert.All(wrong.Check.Evaluate().Entries, entry => Assert.Equal(SourceReadinessKind.Unsupported, entry.Kind));
    }

    [Fact]
    public void MissingAndReplacedFilesInvalidateReadinessWithoutDroppingRememberedAttachments()
    {
        var fixture = new Fixture();
        Assert.True(fixture.Check.Evaluate().Ready);
        fixture.Files.Entries.Remove("Rac1.iso");
        Assert.Equal(SourceReadinessKind.Unavailable, fixture.Check.Evaluate().Entries[0].Kind);
        var replacement = fixture.Files.Entries["Rac3.iso"];
        fixture.Files.Entries["Rac2.iso"] = (replacement.Bytes, replacement.Stamp with { Written = 2 });
        var result = fixture.Check.Evaluate();
        Assert.False(result.Ready);
        Assert.Equal(SourceReadinessKind.Unsupported, result.Entries[1].Kind);
        Assert.Equal(4, fixture.Files.Opens);
        Assert.Equal(3, fixture.Library.Attached.Count);
        fixture.Library.Remove(ObpSourceGame.Rac3);
        Assert.Equal(SourceReadinessKind.Missing, fixture.Check.Evaluate().Entries[2].Kind);
    }

    [SkippableTheory]
    [InlineData(FileAccess.Read, FileShare.None)]
    [InlineData(FileAccess.ReadWrite, FileShare.ReadWrite)]
    public void AConflictingHandleCannotReuseAReadyResultWithUnchangedFileMetadata(FileAccess access, FileShare sharing)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows exclusive file-sharing semantics.");
        var fixture = SyntheticPs2.Build("SCUS_971.99");
        string path = Path.Combine(Path.GetTempPath(), "obp-source-readiness-" + Guid.NewGuid().ToString("N") + ".iso");
        try
        {
            File.WriteAllBytes(path, fixture.Bytes);
            var definition = new ObpSourceDefinition("Fixture", new(ObpSourceGame.Rac1, "fixture", Serial: "SCUS-97199"), fixture.Bytes.Length);
            var library = new ObpSourceLibrary([definition]);
            using (var reader = new FileRandomAccessReader(path)) library.Attach(path, reader);
            var readiness = new SourceReadinessEvaluator(library, [definition]);
            Assert.True(readiness.Evaluate().Ready);
            using (var locked = File.Open(path, FileMode.Open, access, sharing))
                Assert.Equal(SourceReadinessKind.Unavailable, readiness.Evaluate().Entries[0].Kind);
            Assert.True(readiness.Evaluate().Ready);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("--skip-title", false)]
    [InlineData("--automation", false)]
    [InlineData("--destination obp:fusion-lab", false)]
    [InlineData("--record-playtest run.gz", false)]
    [InlineData("--reconciliation-smoke", false)]
    [InlineData("--source-setup-check", false)]
    [InlineData("--ordinary-ui-smoke fresh", false)]
    [InlineData("--diagnostic-sources", true)]
    [InlineData("--retail-parity", true)]
    [InlineData("--test-scene picker", true)]
    [InlineData("--test-scene fusion-lab", true)]
    [InlineData("--player-capabilities-smoke", true)]
    public void OnlyExplicitDiagnosticsExemptTheTrilogyContract(string options, bool diagnostic)
    {
        string[] tokens = options.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(diagnostic, SourceSetupPolicy.IsDiagnostic(CommandLineArgs.Parse(tokens), tokens, ["--headless"]));
        Assert.True(SourceSetupPolicy.IsDiagnostic(CommandLineArgs.Parse([]), [], ["--script", "res://tests/ci_bootstrap_smoke.gd"]));
    }
}
