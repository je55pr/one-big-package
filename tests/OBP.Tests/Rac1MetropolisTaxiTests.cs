using System.Buffers.Binary;
using OBP.Core.Math;
using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1MetropolisTaxiTests
{
    private static readonly (int Index, int Class, double X, double Y, double Z, int OutboundPoints)[] Placements =
    [
        (919, 816, 179.77748, 50, 244.59235, 150),
        (920, 816, 264.25, 34, 93.75, 150),
        (921, 816, 264, 56, 85, 0),
        (961, 1012, 280.74213, 48.165154, 149.90324, 75),
        (962, 1012, 207.35722, 61.993126, 133.20839, 150),
    ];

    // Fixture includes the source-game payload contract without an ISO.
    private static byte[] TinyNativePath(double x, double y, double z)
    {
        var result = new byte[4 + 3 * 12];
        BinaryPrimitives.WriteInt32LittleEndian(result, 3);
        for (int i = 0; i < 3; i++)
        {
            int at = 4 + 12 * i;
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(at, 4), (float)(x + i * 5));
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(at + 4, 4), (float)z);
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(at + 8, 4), (float)y);
        }
        return result;
    }

    private static RuntimeWorld Kerwan(bool wrongClass = false, bool missingPath = false) => new(
        Game: "rac1",
        BuildId: "rac1-ntscu-original",
        LevelId: 3,
        PlanetName: "Kerwan",
        LocationName: null,
        Meshes: [],
        Textures: [],
        MaterialCount: 0,
        CollisionMeshes: [],
        Bounds: new ObpBounds(new Vec3(0, 0, 0), new Vec3(500, 500, 500)),
        Environment: null,
        Ship: new RuntimeSpawn(248.46194, 55.5, 98.921234, 0),
        DynamicObjects: Placements.Select(p => new RuntimeDynamicObject(
            "rac1", wrongClass && p.Index == 961 ? 999 : p.Class, p.Index, null,
            $"moby_{p.Class}", $"moby_i{p.Index}",
            new RuntimeObjectTransform(
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                p.X, p.Y, p.Z, 1,
            ]),
            [],
            NativePayloads: p.OutboundPoints == 0 || (missingPath && p.Index == 919)
                ? []
                : [new RuntimeOpaquePayload(
                    Rac1MetropolisTaxiSession.OutboundPathFormat,
                    TinyNativePath(p.X, p.Y, p.Z))])).ToArray());

    [Fact]
    public void KerwanHasFiveNativeTaxiModelsAndFourBoardablePaths()
    {
        var session = Rac1MetropolisTaxiSession.TryCreate(Kerwan());
        Assert.NotNull(session);
        Assert.Equal(5, session.Stops.Count);
        Assert.Equal(3, session.Stops.Count(x => x.NativeClassId == 816));
        Assert.Equal(2, session.Stops.Count(x => x.NativeClassId == 1012));
        Assert.Equal(4, session.Stops.Count(x => x.OutboundPath.Count > 0));
        var parked = session.Stops.Single(x => x.InstanceIndex == 921);
        Assert.Empty(parked.OutboundPath);
        Assert.Null(session.FindBoardable(parked.Position));
        Assert.Null(Rac1MetropolisTaxiSession.TryCreate(Kerwan() with { LevelId = 2 }));
        Assert.Null(Rac1MetropolisTaxiSession.TryCreate(Kerwan(wrongClass: true)));
        Assert.Null(Rac1MetropolisTaxiSession.TryCreate(Kerwan(missingPath: true)));
    }

    [Fact]
    public void BoardingFliesAlongNativeWaypointsWithoutInventedArc()
    {
        var session = Rac1MetropolisTaxiSession.TryCreate(Kerwan())!;
        Assert.Null(session.FindBoardable(new Rac1TaxiPoint(0, 0, 0)));
        var stop = session.Stops.Single(s => s.InstanceIndex == 962);
        Assert.True(session.TryBoard(stop.Position));
        Assert.Equal(962, session.Riding!.InstanceIndex);
        Assert.False(session.TryBoard(stop.Position));
        Assert.Null(session.FindBoardable(stop.Position));
        var start = session.Tick(0)!;
        var middle = session.Tick(1d / Rac1MetropolisTaxiSession.ProvisionalPathSamplesPerSecond)!;
        Assert.Equal(stop.Position.Y, start.Position.Y, 3);
        Assert.Equal(stop.Position.Y, middle.Position.Y, 3);
        Assert.Equal(stop.Position.X + 5, middle.Position.X, 3);
        Assert.False(middle.JustArrived);
        var end = session.Tick(1d / Rac1MetropolisTaxiSession.ProvisionalPathSamplesPerSecond)!;
        Assert.True(end.JustArrived);
        Assert.Equal(stop.Position.X + 10, end.Arrival.X, 3);
        Assert.Equal(1, end.Progress, 5);
        Assert.False(session.Tick(0)!.JustArrived);
        session.CompleteArrival();
        Assert.False(session.TryBoard(stop.Position));
        session.Tick(1.25);
        Assert.True(session.TryBoard(stop.Position));
    }

    [Fact]
    public void InvalidElapsedTimeDoesNotCorruptFlight()
    {
        var session = Rac1MetropolisTaxiSession.TryCreate(Kerwan())!;
        Assert.True(session.TryBoard(session.Stops[0].Position));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Tick(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Tick(double.NaN));
        Assert.Equal(0, session.Tick(0)!.Progress);
        session.Reset();
        Assert.False(session.IsRiding);
    }

    [Fact]
    public void RetailKerwanPvarsSelectExactOutboundSplines()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        if (string.IsNullOrWhiteSpace(iso) || !File.Exists(iso)) return;

        using var reader = new FileRandomAccessReader(iso);
        var world = Rac1WorldImport.Build(reader, 3);
        var session = Rac1MetropolisTaxiSession.TryCreate(world);
        Assert.NotNull(session);
        var expected = new (int Index, int PvarOffset, int NativePath, int Points, Rac1TaxiPoint End)[]
        {
            (919, 0xa8, 66, 150, new(263.95618, 55.99836, 84.99897)),
            (920, 0xa8, 71, 150, new(263.98114, 56.00007, 85.00012)),
            (961, 0x94, 59, 75, new(263.27188, 34.271873, 237.42558)),
            (962, 0x94, 67, 150, new(118.35907, 65.84171, 78.461815)),
        };

        foreach (var p in Placements)
        {
            var stop = session.Stops.Single(s => s.InstanceIndex == p.Index);
            Assert.Equal(p.Class, stop.NativeClassId);
            Assert.Equal(p.OutboundPoints, stop.OutboundPath.Count);
            Assert.InRange(Math.Abs(stop.Position.X - p.X), 0, 0.0002);
            Assert.InRange(Math.Abs(stop.Position.Y - p.Y), 0, 0.0002);
            Assert.InRange(Math.Abs(stop.Position.Z - p.Z), 0, 0.0002);
        }
        foreach (var route in expected)
        {
            var obj = world.DynamicObjects!.Single(o => o.InstanceIndex == route.Index);
            var pvar = obj.NativePayloads!.Single(p => p.Format == "rac1-pvar").Data;
            Assert.Equal(route.NativePath,
                BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(route.PvarOffset, 4)));
            var stop = session.Stops.Single(s => s.InstanceIndex == route.Index);
            Assert.Equal(route.Points, stop.OutboundPath.Count);
            var endpoint = stop.OutboundPath[^1];
            Assert.InRange(endpoint.DistanceSquared(route.End), 0, 0.0002);
        }
    }
}
