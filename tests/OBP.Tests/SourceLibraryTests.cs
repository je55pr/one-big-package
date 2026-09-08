using OBP.Core;
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
