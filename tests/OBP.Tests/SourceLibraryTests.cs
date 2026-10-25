using OBP.Core;
using OBP.IO;
using OBP.PS2;
using OBP.RAC1;
using OBP.RAC2;
using OBP.RAC3;
using OBP.Tests.Helpers;

namespace OBP.Tests;

public sealed class SourceLibraryTests
{
    private static readonly (ObpSourceGame Game, string Display, string SerialExe, string Serial)[] Games =
    {
        (ObpSourceGame.Rac1, "Ratchet & Clank", "SCUS_971.99", "SCUS-97199"),
        (ObpSourceGame.Rac2, "Going Commando", "SCUS_972.68", "SCUS-97268"),
        (ObpSourceGame.Rac3, "Up Your Arsenal", "SCUS_973.53", "SCUS-97353"),
    };

    [Fact]
    public void ProbeRecognizesConfiguredTrilogySerials()
    {
        foreach (var game in Games)
        {
            var fixture = SyntheticPs2.Build(game.SerialExe);
            var library = new ObpSourceLibrary(new[] { Definition(game, fixture.Bytes.Length) });
            var probe = library.Probe(new InMemoryReader("synthetic.iso", fixture.Bytes));

            Assert.True(probe.Recognized);
            Assert.True(probe.Supported);
            Assert.True(probe.SizeMatches);
            Assert.Equal(game.Game, probe.Definition!.Game);
            Assert.Equal(game.Serial, probe.DiscSerial);
            Assert.Null(probe.Problem);
        }
    }

    [Fact]
    public void ProbeRejectsUnknownSerialAndKnownSerialWrongSize()
    {
        var fixture = SyntheticPs2.Build("SCUS_972.68");
        var gc = Games[1];

        var unknownLibrary = new ObpSourceLibrary(new[]
        {
            Definition(Games[0], fixture.Bytes.Length),
        });
        var unknown = unknownLibrary.Probe(new InMemoryReader("unknown.iso", fixture.Bytes));
        Assert.False(unknown.Recognized);
        Assert.False(unknown.Supported);
        Assert.Contains("SCUS-97268", unknown.Problem);

        var wrongSizeLibrary = new ObpSourceLibrary(new[] { Definition(gc, fixture.Bytes.Length + 1) });
        var wrongSize = wrongSizeLibrary.Probe(new InMemoryReader("wrong-size.iso", fixture.Bytes));
        Assert.True(wrongSize.Recognized);
        Assert.False(wrongSize.Supported);
        Assert.False(wrongSize.SizeMatches);
        Assert.Contains("payload size", wrongSize.Problem);
    }

    [Fact]
    public void AttachKeepsOneSourcePerGameAndConfigRoundTrips()
    {
        var fixtures = Games.ToDictionary(g => g.Game, g => SyntheticPs2.Build(g.SerialExe));
        var library = new ObpSourceLibrary(Games.Select(g => Definition(g, fixtures[g.Game].Bytes.Length)));

        foreach (var game in Games)
        {
            var bytes = fixtures[game.Game].Bytes;
            library.Attach($"C:/games/{game.Game}.iso", new InMemoryReader(game.Display, bytes));
        }

        Assert.Equal(3, library.Attached.Count);
        Assert.Equal("C:/games/Rac2.iso", library.Get(ObpSourceGame.Rac2)!.Path);

        string temp = Path.Combine(Path.GetTempPath(), $"obp-sources-{Guid.NewGuid():N}.json");
        try
        {
            library.SaveConfig(temp);
            var saved = ObpSourceLibrary.LoadConfig(temp);
            Assert.Equal(3, saved.Count);
            Assert.Equal(new[] { ObpSourceGame.Rac1, ObpSourceGame.Rac2, ObpSourceGame.Rac3 },
                saved.Select(s => s.Game).ToArray());

            var restored = new ObpSourceLibrary(Games.Select(g => Definition(g, fixtures[g.Game].Bytes.Length)));
            foreach (var entry in saved)
            {
                restored.Restore(entry, new InMemoryReader(entry.Path, fixtures[entry.Game].Bytes));
            }

            Assert.Equal(3, restored.Attached.Count);
            Assert.Equal("rac3-test", restored.Get(ObpSourceGame.Rac3)!.Identity.BuildId);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void RestoreDoesNotTrustStalePersistedIdentity()
    {
        var rac1Fixture = SyntheticPs2.Build(Games[0].SerialExe);
        var rac2Fixture = SyntheticPs2.Build(Games[1].SerialExe);
        var library = new ObpSourceLibrary(new[]
        {
            Definition(Games[0], rac1Fixture.Bytes.Length),
            Definition(Games[1], rac2Fixture.Bytes.Length),
        });

        var stale = new ObpSavedSource(ObpSourceGame.Rac1, "rac1-test", "C:/games/replaced.iso");
        var ex = Assert.Throws<InvalidDataException>(() =>
            library.Restore(stale, new InMemoryReader(stale.Path, rac2Fixture.Bytes)));
        Assert.Contains("expected Rac1/rac1-test", ex.Message);
        Assert.Empty(library.Attached);
    }

    [Fact]
    public void ConfigBackupRecoversTheValidatedTrilogyWithoutRewritingEitherCopy()
    {
        using var fixture = new ConfigFixture();
        fixture.Library.SaveConfig(fixture.Path);
        fixture.Library.Remove(ObpSourceGame.Rac3);
        fixture.Library.SaveConfig(fixture.Path);
        Assert.Equal(2, ObpSourceLibrary.LoadConfig(fixture.Path).Count);
        string backup = File.ReadAllText(fixture.Path + ".bak");
        File.WriteAllText(fixture.Path, "{truncated");
        var read = ObpSourceLibrary.ReadConfig(fixture.Path);
        Assert.Equal(DocumentReadKind.Backup, read.Kind);
        Assert.NotNull(read.Problem);
        Assert.Equal(3, read.Value!.Length);
        Assert.Equal("{truncated", File.ReadAllText(fixture.Path));
        Assert.Equal(backup, File.ReadAllText(fixture.Path + ".bak"));
        var restored = new ObpSourceLibrary(fixture.Library.Definitions);
        foreach (var entry in read.Value)
            restored.Restore(entry, new InMemoryReader(entry.Path, fixture.Discs[entry.Game].Bytes));
        Assert.Equal(Games.Select(game => game.Game), restored.Attached.Keys.Order());
    }

    [Fact]
    public void BothBadCopiesReportRepairAndExplicitSaveRetainsTheOriginalDocuments()
    {
        using var fixture = new ConfigFixture();
        File.WriteAllText(fixture.Path, "broken primary");
        File.WriteAllText(fixture.Path + ".bak", "broken backup");
        var read = ObpSourceLibrary.ReadConfig(fixture.Path);
        Assert.Equal(DocumentReadKind.Rejected, read.Kind);
        Assert.Null(read.Value); Assert.NotNull(read.Problem);
        Assert.Throws<InvalidDataException>(() => ObpSourceLibrary.LoadConfig(fixture.Path));
        Assert.Equal("broken primary", File.ReadAllText(fixture.Path));
        fixture.Library.SaveConfig(fixture.Path); // An explicit repair, not a side effect of reading.
        Assert.Equal(3, ObpSourceLibrary.LoadConfig(fixture.Path).Count);
        Assert.Equal("broken backup", File.ReadAllText(fixture.Path + ".bak"));
        Assert.Equal("broken primary", File.ReadAllText(Assert.Single(Directory.GetFiles(fixture.Directory, "sources.json.rejected-*"))));
    }

    [Fact]
    public void InterruptedTemporaryWriteAndOversizedReplacementLeaveTheGoodConfigIntact()
    {
        using var fixture = new ConfigFixture();
        fixture.Library.SaveConfig(fixture.Path);
        byte[] before = File.ReadAllBytes(fixture.Path);
        File.WriteAllText(fixture.Path + ".tmp-interrupted", "{");
        Assert.Equal(DocumentReadKind.Primary, ObpSourceLibrary.ReadConfig(fixture.Path).Kind);
        fixture.Library.Attach(new string('x', ObpSourceLibrary.MaximumConfigBytes),
            new InMemoryReader("oversized-path.iso", fixture.Discs[ObpSourceGame.Rac1].Bytes));
        Assert.Throws<InvalidDataException>(() => fixture.Library.SaveConfig(fixture.Path));
        Assert.Equal(before, File.ReadAllBytes(fixture.Path));
        Assert.Equal("{", File.ReadAllText(fixture.Path + ".tmp-interrupted"));
    }

    [Theory]
    [InlineData("{\"Version\":1,\"Sources\":null}")]
    [InlineData("{\"Version\":1,\"Sources\":[null]}")]
    [InlineData("{\"Version\":1,\"Sources\":[{\"Game\":99,\"BuildId\":\"x\",\"Path\":\"fixture.iso\"}]}")]
    [InlineData("{\"Version\":99,\"Sources\":[]}")]
    public void MalformedConfigSchemasAreRejectedWithoutReplacingBytes(string json)
    {
        using var fixture = new ConfigFixture();
        Assert.Equal(DocumentReadKind.Missing, ObpSourceLibrary.ReadConfig(fixture.Path).Kind);
        File.WriteAllText(fixture.Path, json);
        Assert.Equal(DocumentReadKind.Rejected, ObpSourceLibrary.ReadConfig(fixture.Path).Kind);
        Assert.Equal(json, File.ReadAllText(fixture.Path));
    }

    private sealed class ConfigFixture : IDisposable
    {
        internal readonly string Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "obp-source-config-" + Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(Directory, "sources.json");
        internal readonly Dictionary<ObpSourceGame, SyntheticPs2.Fixture> Discs;
        internal readonly ObpSourceLibrary Library;
        internal ConfigFixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Discs = Games.ToDictionary(game => game.Game, game => SyntheticPs2.Build(game.SerialExe));
            Library = new(Games.Select(game => Definition(game, Discs[game.Game].Bytes.Length)));
            foreach (var game in Games) Library.Attach(game.Game + ".iso", new InMemoryReader("fixture.iso", Discs[game.Game].Bytes));
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    /// <summary>
    /// Bounded real-input acceptance check for the self-hosted runner. Unlike
    /// exact verification this only exercises ISO-9660/SYSTEM.CNF/ELF + serial
    /// + payload-size matching, so it does not stream/hash ~12.4 GB every run.
    /// </summary>
    [SkippableFact]
    public void RetailTrilogySources_QuickProbeAuthorityFiles()
    {
        string? rac1 = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        string? rac2 = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        string? rac3 = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(rac1) || string.IsNullOrEmpty(rac2) || string.IsNullOrEmpty(rac3),
            "OBP_RAC1_ISO / OBP_GC_ISO / OBP_UYA_ISO are not all set");

        var library = new ObpSourceLibrary(new[]
        {
            new ObpSourceDefinition("Ratchet & Clank", Rac1Authority.Primary, Rac1Authority.PrimaryIsoSizeBytes),
            new ObpSourceDefinition("Going Commando", Rac2Authority.Primary, Rac2Authority.PrimaryIsoSizeBytes),
            new ObpSourceDefinition("Up Your Arsenal", Rac3Authority.Primary, Rac3Authority.PrimaryIsoSizeBytes),
        });

        foreach (var expected in new[]
        {
            (ObpSourceGame.Rac1, rac1!),
            (ObpSourceGame.Rac2, rac2!),
            (ObpSourceGame.Rac3, rac3!),
        })
        {
            using var reader = new OBP.IO.FileRandomAccessReader(expected.Item2);
            var probe = library.Probe(reader);
            Assert.True(probe.Recognized, probe.Problem);
            Assert.True(probe.Supported, probe.Problem);
            Assert.True(probe.SizeMatches);
            Assert.Equal(expected.Item1, probe.Definition!.Game);
        }
    }

    private static ObpSourceDefinition Definition(
        (ObpSourceGame Game, string Display, string SerialExe, string Serial) game,
        long size) =>
        new(
            game.Display,
            new ObpBuildIdentity(game.Game, $"{game.Game.ToString().ToLowerInvariant()}-test", "test", game.Serial, "test", null),
            size);
}
