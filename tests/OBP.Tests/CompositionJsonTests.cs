using OBP.Composition;
using OBP.Core.Math;

namespace OBP.Tests;

public class CompositionJsonTests
{
    private static WorldComposition Sample() => new()
    {
        Name = "R&C1 Veldin vs UYA Veldin",
        Notes = "first alignment pass",
        Comparison = new ComparisonState { ActiveWorldId = "uya-veldin", FitScale = false, OverlayMode = "both" },
        Worlds = new[]
        {
            new WorldPlacement
            {
                Id = "rc1-veldin",
                DestinationId = "rac1:veldin",
                SourceGame = "R&C1",
                BuildId = "rac1-ntscu",
                LevelId = 3,
                Label = "R&C1 Veldin",
            },
            new WorldPlacement
            {
                Id = "uya-veldin",
                DestinationId = "rac3:veldin",
                SourceGame = "Up Your Arsenal",
                BuildId = "rac3-ntscu",
                LevelId = 42,
                Label = "UYA Veldin",
                Transform = new CompositionTransform(123.456789, 0, -77.2, 42.1, 1.0),
                Opacity = 0.5,
                DebugTint = new[] { 1.0, 0.2, 0.2 },
                CategoryVisibility = new Dictionary<string, bool> { ["collision"] = false, ["sky"] = false },
            },
        },
        Anchors = new[]
        {
            new AnchorPair
            {
                WorldAId = "rc1-veldin", WorldBId = "uya-veldin", Label = "garage doorway",
                LocalA = new Vec3(10, 1, 20), LocalB = new Vec3(-5, 2, 8),
            },
            new AnchorPair
            {
                WorldAId = "rc1-veldin", WorldBId = "uya-veldin", Label = "start-area frog",
                LocalA = new Vec3(40, 0, -3), LocalB = new Vec3(25, 1, -15),
            },
        },
    };

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var original = Sample();
        var restored = CompositionJson.Deserialize(CompositionJson.Serialize(original));

        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Notes, restored.Notes);
        Assert.Equal(2, restored.Worlds.Count);
        Assert.Equal(2, restored.Anchors.Count);

        var uya = restored.World("uya-veldin")!;
        Assert.Equal("rac3:veldin", uya.DestinationId);
        Assert.Equal(42, uya.LevelId);
        Assert.Equal(42.1, uya.Transform.RotationYDegrees, 6);
        Assert.Equal(123.456789, uya.Transform.TranslationX, 6);
        Assert.Equal(0.5, uya.Opacity, 9);
        Assert.False(uya.CategoryVisible("collision"));
        Assert.True(uya.CategoryVisible("tfrag"));
        Assert.Equal(new[] { 1.0, 0.2, 0.2 }, uya.DebugTint);

        var anchor = restored.Anchors[0];
        Assert.Equal("rc1-veldin", anchor.WorldAId);
        Assert.Equal(new Vec3(10, 1, 20), anchor.LocalA);
    }

    [Fact]
    public void Serialize_IsDeterministic_RegardlessOfEditOrder()
    {
        var a = Sample();

        // Same logical composition, worlds + anchors added in reverse.
        var b = new WorldComposition
        {
            Name = a.Name,
            Notes = a.Notes,
            Comparison = a.Comparison,
            Worlds = a.Worlds.Reverse().ToList(),
            Anchors = a.Anchors.Reverse().ToList(),
        };

        Assert.Equal(CompositionJson.Serialize(a), CompositionJson.Serialize(b));
    }

    [Fact]
    public void Serialize_UsesLfLineEndings_AndTrailingNewline()
    {
        string json = CompositionJson.Serialize(Sample());
        Assert.DoesNotContain("\r\n", json);
        Assert.EndsWith("\n", json);
    }

    [Fact]
    public void Deserialize_ToleratesMinimalDocument()
    {
        var c = CompositionJson.Deserialize("""{ "version": 1, "worlds": [ { "id": "w1", "levelId": 1 } ] }""");
        Assert.Single(c.Worlds);
        Assert.Equal("w1", c.Worlds[0].Id);
        Assert.True(c.Worlds[0].Transform.IsIdentity);
        Assert.True(c.Worlds[0].Visible);
        Assert.Equal(1.0, c.Worlds[0].Opacity, 9);
        Assert.Empty(c.Anchors);
    }

    [Fact]
    public void NoGeometryTokensInOutput()
    {
        string json = CompositionJson.Serialize(Sample());
        foreach (var banned in new[] { "positions", "indices", "vertices", "meshes", "rgba", "triangles" })
        {
            Assert.DoesNotContain(banned, json, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SaveLoad_FileRoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"obp-comp-{System.Guid.NewGuid():N}.json");
        try
        {
            CompositionJson.Save(Sample(), path);
            var restored = CompositionJson.Load(path);
            Assert.Equal(CompositionJson.Serialize(Sample()), CompositionJson.Serialize(restored));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
