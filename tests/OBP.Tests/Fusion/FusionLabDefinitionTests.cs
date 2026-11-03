using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Player;
using OBP.Game;

namespace OBP.Tests;

public sealed class FusionLabDefinitionTests
{
    [Fact]
    public void AuthoredLabCollisionDoesNotInventNativeWaterOrOtherSurfaceCodes()
    {
        var world = FusionLabWorldFactory.Build(Fixture());
        Assert.NotEmpty(world.CollisionMeshes);
        foreach (var blob in world.CollisionMeshes)
        {
            Assert.True(blob.Triangles > 0);
            Assert.Empty(blob.TriangleMaterialIds);
            var material = RuntimeWorldScene.MaterialIdForFace(blob, [0], 0);
            Assert.Null(material);
            var contact = Rac1PlayerContactResult.StaticWorld(true, rawFaceType: material);
            Assert.True(contact.IsGrounded);
            Assert.Equal(Rac1SurfaceInteractionKind.None, contact.SurfaceInteraction);
        }
    }

    private static FusionLabDefinition Fixture() =>
        FusionLabDefinition.Parse(File.ReadAllText(
            Path.Combine(RepoPaths.Root, "game", "data", "fusion_lab.json")));

    [Fact]
    public void AuthoredFixtureHasConnectedWalkableEntranceHubAndTestRoom()
    {
        var lab = Fixture();
        Assert.Equal("obp:fusion-lab", lab.Id);
        Assert.Equal(5, lab.Rooms.Count);
        Assert.Equal(4, lab.Connections.Count);
        Assert.Equal("entrance", lab.Rooms[0].Id);
        Assert.Equal("test-room", lab.Rooms[^1].Id);
        Assert.Contains(lab.Rooms, r => r.Id == "hub");
        Assert.Equal(4, lab.Stations.Count);
        Assert.Contains(lab.Stations, station => station.AdapterId == "obp/effects-experiment/v1" && station.ResetKey == "native-effects");
        Assert.All(lab.Stations, s => Assert.False(string.IsNullOrWhiteSpace(s.ResetKey)));

        // Each portal meets both floor edges. Following the connections gives
        // a contiguous route to the chamber and the same route in reverse.
        foreach (var c in lab.Connections)
        {
            var a = lab.Rooms.Single(r => r.Id == c.From);
            var b = lab.Rooms.Single(r => r.Id == c.To);
            Assert.Equal(a.CenterZ + a.Depth / 2, b.CenterZ - b.Depth / 2, 8);
            Assert.True(c.OpeningWidth >= 3);
        }
    }

    [Fact]
    public void RejectsDuplicateRoomsDisconnectedOrMisalignedPortals()
    {
        var lab = Fixture();
        Assert.Throws<ArgumentException>(() => (lab with
        {
            Rooms = [.. lab.Rooms, lab.Rooms[0]]
        }).Validate());
        Assert.Throws<ArgumentException>(() => (lab with
        {
            Connections = lab.Connections.Skip(1).ToArray()
        }).Validate());
        Assert.Throws<ArgumentException>(() => (lab with
        {
            Rooms = lab.Rooms.Select(r => r.Id == "test-room" ? r with { CenterZ = 100 } : r).ToArray()
        }).Validate());
    }

    [Fact]
    public void FutureStationSourceNeedsRealQualifiedIdentityAndExplicitAdapter()
    {
        var lab = Fixture();
        var station = lab.Stations[0];
        Assert.Throws<ArgumentException>(() => (lab with
        {
            Stations = [station with
            {
                Source = new FusionSourceIdentity(ObpSourceGame.Rac1, "", "moby:123"),
                AdapterId = "native-rac1",
            }]
        }).Validate());
        Assert.Throws<ArgumentException>(() => (lab with
        {
            Stations = [station with
            {
                Source = new FusionSourceIdentity(ObpSourceGame.Rac1, "ntscu", "moby:123"),
                AdapterId = null,
            }]
        }).Validate());
    }
}
