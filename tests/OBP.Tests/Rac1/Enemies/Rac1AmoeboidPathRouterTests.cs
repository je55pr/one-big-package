using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1AmoeboidPathRouterTests
{
    [Fact]
    public void OriginalDirectUnblockedTraceUsesExactTarget()
    {
        var router = new Rac1AmoeboidPathRouter(
            [new([new(3, 4, 0), new(8, 4, 0)], [0, 0])]);
        int probes = 0;
        var target = new Vector3(10, 3, 0);
        bool result = router.TryFollowPath(7, 0, 0.8f, Vector3.Zero, target,
            (region, from, to) =>
            {
                Assert.Equal(7, region);
                probes++;
                Assert.Equal(target, to);
                return false;
            }, out var waypoint);
        Assert.True(result);
        Assert.Equal(target, waypoint);
        Assert.Equal(2, probes); // Native 2-sided width check.
    }

    [Fact]
    public void SourceConnectionMaskExpandsToReachableWaypointWhenDirectRouteIsBlocked()
    {
        var router = new Rac1AmoeboidPathRouter(
            [new([new(2, 0, 0), new(8, 0, 0)], [2, 1])]);
        var start = Vector3.Zero;
        var target = new Vector3(10, 0, 0);
        bool Intersects(int _, Vector3 from, Vector3 to) =>
            (from.X < 3 && to.X > 6) || (from.X > 9 && to.X < 3);
        Assert.True(router.TryFollowPath(9, 0, 0, start, target, Intersects,
            out var waypoint));
        Assert.Equal(new Vector3(2, 0, 0), waypoint);
        Assert.Equal(1u, Rac1AmoeboidPathRouter.ExpandConnections(1, 2, [2, 1]));
        Assert.Equal(0u, Rac1AmoeboidPathRouter.ExpandConnections(1, 2, [0, 0]));
    }

    [Fact]
    public void DisconnectedSourceWaypointsDoNotInventPassageThroughObstacles()
    {
        var router = new Rac1AmoeboidPathRouter(
            [new([new(2, 0, 0), new(8, 0, 0)], [0, 0])]);
        Assert.False(router.TryFollowPath(9, 0, 0, Vector3.Zero, new(10, 0, 0),
            (_, from, to) => (from.X < 3 && to.X > 6) || (from.X > 9 && to.X < 3),
            out _));
        Assert.False(router.TryFollowPath(9, 0, 0, Vector3.Zero, new(10, 0, 0),
            (_, _, _) => true, out _));
    }

    [SkippableFact]
    public void RetailAll227AmoeboidsReferenceValidPathAndObstacleRegions()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO is not available.");
        using var reader = new FileRandomAccessReader(iso!);
        int count = 0;
        foreach (var lv in Rac1DiscIndex.Read(reader).Levels.Where(x => x.LevelId is 1 or 5 or 11))
        {
            byte[] data = Rac1LevelSettings.ReadGameplay(reader, lv);
            var paths = Rac1AmoeboidPathRouter.Read(data);
            foreach (var moby in Rac1Instances.Parse(data).MobyInstances
                .Where(x => Rac1AmoeboidAuthored.IsFamily(x.OClass)))
            {
                var facts = Rac1AmoeboidAuthored.Read(moby.OClass, moby.PVar!);
                Assert.True(facts.PathIndex >= 0);
                Assert.InRange(facts.NextPathIndex, 0, paths.Count - 1);
                Assert.InRange(paths.PointCount(facts.NextPathIndex), 1, 32);
                Assert.True(paths.TryFollowPath(facts.PathIndex, facts.NextPathIndex,
                    .8f, new(moby.Position.X, moby.Position.Y, moby.Position.Z),
                    new(moby.Position.X + 7, moby.Position.Y, moby.Position.Z),
                    (_, _, _) => false, out _));
                count++;
            }
        }
        Assert.Equal(227, count);
    }
}
