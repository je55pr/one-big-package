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
    private static byte[] TinyNativePath(double x, double y, double z, bool returning = false)
    {
        var result = new byte[4 + 3 * 12];
        BinaryPrimitives.WriteInt32LittleEndian(result, 3);
        for (int i = 0; i < 3; i++)
        {
            int at = 4 + 12 * i;
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(at, 4), (float)(x + (returning ? 2 - i : i) * 5));
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
                : p.Class == 1012
                    ? [
                        new RuntimeOpaquePayload(Rac1MetropolisTaxiSession.OutboundPathFormat,
                            TinyNativePath(p.X, p.Y, p.Z)),
                        new RuntimeOpaquePayload(Rac1MetropolisTaxiSession.ReturnPathFormat,
                            TinyNativePath(p.X, p.Y, p.Z, returning: true)),
                    ]
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
        // Synthetic fixtures without original PVars intentionally do not
        // attach native hover phases to their route animations.
        Assert.All(session.Stops, stop => Assert.Null(stop.NativeHoverPhases));
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
        var stop = session.Stops.Single(s => s.InstanceIndex == 919);
        Assert.True(session.TryBoard(stop.Position));
        Assert.Equal(919, session.Riding!.InstanceIndex);
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
    public void Class816ApproachesOnlyInsideNativeTriggerAndLeavesAfterPassengerClears()
    {
        var world = Kerwan();
        var objects = world.DynamicObjects!.ToArray();
        int slot = Array.FindIndex(objects, o => o.InstanceIndex == 919);
        var stop = Placements.Single(p => p.Index == 919);
        var volume = new byte[24];
        float[] fields = [(float)stop.X - 12, (float)stop.Z, (float)stop.Y, 4f, 4f, 4f];
        for (int i = 0; i < fields.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(volume.AsSpan(i * 4, 4), fields[i]);
        objects[slot] = objects[slot] with
        {
            NativePayloads = objects[slot].NativePayloads!.Concat([
                new RuntimeOpaquePayload(Rac1MetropolisTaxiSession.ApproachPathFormat,
                    TinyNativePath(stop.X - 10, stop.Y, stop.Z)),
                new RuntimeOpaquePayload(Rac1MetropolisTaxiSession.DeparturePathFormat,
                    TinyNativePath(stop.X + 10, stop.Y, stop.Z)),
                new RuntimeOpaquePayload(Rac1MetropolisTaxiSession.TriggerVolumeFormat, volume),
            ]).ToArray(),
        };
        var session = Rac1MetropolisTaxiSession.TryCreate(world with { DynamicObjects = objects })!;
        var taxi = session.Stops.Single(s => s.InstanceIndex == 919);
        Assert.Equal(3, taxi.ApproachPath!.Count);
        Assert.NotNull(taxi.ApproachTrigger);
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.WaitingForHero, session.PhaseOf(919));
        Assert.False(session.TryBoard(taxi.Position));
        var waiting = session.TickTraffic(new Rac1TaxiPoint(0, 0, 0), 1);
        Assert.Equal(taxi.ApproachPath[0], waiting.Single(f => f.InstanceIndex == 919).Position);

        var trigger = taxi.ApproachTrigger.Center;
        session.TickTraffic(trigger, 0);
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.Approaching, session.PhaseOf(919));
        double tick = 1 / Rac1MetropolisTaxiSession.ProvisionalPathSamplesPerSecond;
        session.TickTraffic(trigger, tick);
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.Approaching, session.PhaseOf(919));
        session.TickTraffic(trigger, tick);
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.Docked, session.PhaseOf(919));
        // Cancelling a different cab must not rewind this already docked actor.
        var shuttle = session.Stops.Single(s => s.InstanceIndex == 962);
        Assert.True(session.TryBoard(shuttle.Position));
        Assert.NotNull(session.Tick(tick));
        session.AbortRide();
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.Docked, session.PhaseOf(919));
        Assert.True(session.TryBoard(taxi.Position));
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.Carrying, session.PhaseOf(919));
        var reached = session.Tick(2 * tick)!;
        Assert.True(reached.JustArrived);
        session.CompleteArrival();
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.ParkedAtDestination, session.PhaseOf(919));
        session.TickTraffic(reached.Arrival, 2);
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.ParkedAtDestination, session.PhaseOf(919));
        session.TickTraffic(new Rac1TaxiPoint(
            reached.Arrival.X + 15, reached.Arrival.Y, reached.Arrival.Z), 0);
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.ParkedAtDestination, session.PhaseOf(919));
        session.TickTraffic(new Rac1TaxiPoint(0, 0, 0), 0);
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.Departing, session.PhaseOf(919));
        session.TickTraffic(new Rac1TaxiPoint(0, 0, 0), 2 * tick);
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.WaitingForHero, session.PhaseOf(919));
    }

    [Fact]
    public void Class1012CompletesBothNativeLegsBeforeDisembarking()
    {
        var session = Rac1MetropolisTaxiSession.TryCreate(Kerwan())!;
        var stop = session.Stops.Single(s => s.InstanceIndex == 962);
        Assert.Equal(3, stop.ReturnPath!.Count);
        Assert.True(session.TryBoard(stop.Position));
        double tick = 1d / Rac1MetropolisTaxiSession.ProvisionalPathSamplesPerSecond;
        var outward = session.Tick(2 * tick)!;
        Assert.False(outward.JustArrived);
        Assert.Equal(stop.Position.X + 10, outward.Position.X, 3);
        var halfwayHome = session.Tick(tick)!;
        Assert.False(halfwayHome.JustArrived);
        Assert.Equal(stop.Position.X + 5, halfwayHome.Position.X, 3);
        var home = session.Tick(tick)!;
        Assert.True(home.JustArrived);
        Assert.Equal(stop.Position.X, home.Position.X, 3);
        Assert.Equal(stop.Position.X, home.Arrival.X, 3);
        Assert.Equal(1d, home.Progress, 5);
        Assert.False(session.Tick(0)!.JustArrived);
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
    public void InterruptedTaxiRideRearmsAuthoredStopAndRejectsInvalidPositions()
    {
        var session = Rac1MetropolisTaxiSession.TryCreate(Kerwan())!;
        var stop = session.Stops.Single(s => s.InstanceIndex == 919);

        Assert.Null(session.FindBoardable(new Rac1TaxiPoint(double.NaN, 0, 0)));
        Assert.Null(session.FindBoardable(new Rac1TaxiPoint(0, double.PositiveInfinity, 0)));
        Assert.Null(session.FindBoardable(new Rac1TaxiPoint(0, 0, double.NegativeInfinity)));
        Assert.True(session.TryBoard(stop.Position));
        Assert.NotNull(session.Tick(0.01));
        Assert.True(session.IsRiding);

        session.AbortRide();
        Assert.False(session.IsRiding);
        Assert.Null(session.Tick(0));
        Assert.True(session.TryBoard(stop.Position));
        Assert.Equal(0, session.Tick(0)!.Progress);
        session.Reset();
        Assert.False(session.IsRiding);
        Assert.True(session.TryBoard(stop.Position));
    }

    [Fact]
    public void Class1012NativeFifteenFrameResetDebouncesReboarding()
    {
        var session = Rac1MetropolisTaxiSession.TryCreate(Kerwan())!;
        var stop = session.Stops.Single(s => s.InstanceIndex == 961);
        Assert.True(session.TryBoard(stop.Position));
        Assert.True(session.Tick(10)!.JustArrived);
        session.CompleteArrival();
        Assert.False(session.IsRiding);

        session.Tick(14d / 60d);
        Assert.Null(session.FindBoardable(stop.Position));
        session.Tick(1d / 60d);
        Assert.Same(stop, session.FindBoardable(stop.Position));
    }

    [Fact]
    public void RetailKerwanTaxisUseNativeSeekingAndCompleteBothFlightFamilies()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        if (string.IsNullOrWhiteSpace(iso) || !File.Exists(iso)) return;

        using var reader = new FileRandomAccessReader(iso);
        var session = Rac1MetropolisTaxiSession.TryCreate(Rac1WorldImport.Build(reader, 3))!;
        Assert.All(session.Stops.Where(s => s.OutboundPath.Count > 1),
            stop => Assert.True(stop.NativeSeekEvidenced));

        var inbound = session.Stops.Single(s => s.InstanceIndex == 919);
        Assert.NotNull(inbound.ApproachTrigger);
        session.TickTraffic(inbound.ApproachTrigger!.Center, 0);
        int frames = 0;
        Rac1TaxiPoint last = inbound.ApproachPath![0];
        while (session.PhaseOf(919) != Rac1MetropolisTaxiSession.TrafficPhase.Docked &&
            frames++ < 24000)
            last = session.TickTraffic(inbound.ApproachTrigger.Center, 1d / 60)
                .Single(f => f.InstanceIndex == 919).Position;
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.Docked, session.PhaseOf(919));
        Assert.InRange(frames, 151, 24000);

        Assert.True(session.TryBoard(inbound.Position));
        frames = 0;
        Rac1TaxiFlight? flight = null;
        while (frames++ < 24000)
        {
            flight = session.Tick(1d / 60);
            if (flight?.JustArrived == true) break;
        }
        Assert.True(flight?.JustArrived);
        Assert.Equal(inbound.OutboundPath[^1], flight!.Position);
        session.CompleteArrival();

        session.Reset();
        var shuttle = session.Stops.Single(s => s.InstanceIndex == 962);
        Assert.True(session.TryBoard(shuttle.Position));
        frames = 0;
        while (frames++ < 24000)
        {
            flight = session.Tick(1d / 60);
            if (flight?.JustArrived == true) break;
        }
        Assert.True(flight?.JustArrived);
        Assert.Equal(shuttle.ReturnPath![^1], flight!.Position);
        Assert.InRange(frames, 301, 24000);
    }

    [Theory]
    [InlineData(961, 1, 2, 0x0bce, 0x0bcf)]
    [InlineData(962, 3, 4, 0x0bcb, 0x0bcd)]
    public void RetailClass1012HudMessagesFollowAuthoredNextSideSelectors(
        int instance, int sideZero, int sideOne,
        int sourceDockMessage, int otherEndMessage)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        if (string.IsNullOrWhiteSpace(iso) || !File.Exists(iso)) return;
        using var reader = new FileRandomAccessReader(iso);
        var world = Rac1WorldImport.Build(reader, 3);
        var obj = world.DynamicObjects!.Single(o => o.InstanceIndex == instance);
        var pvar = obj.NativePayloads!.Single(p => p.Format == "rac1-pvar").Data;
        Assert.Equal(sideZero, BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x98, 4)));
        Assert.Equal(sideOne, BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x9c, 4)));

        var stop = Rac1MetropolisTaxiSession.TryCreate(world)!.Stops
            .Single(s => s.InstanceIndex == instance);
        Assert.Equal(sideZero, stop.NativeHudSelectors!.Value.SideZeroSelector);
        Assert.Equal(sideOne, stop.NativeHudSelectors.Value.SideOneSelector);
        Assert.Equal(sourceDockMessage,
            stop.NativeHudSelectors.Value.MessageForCurrentSide(1));
        Assert.Equal(otherEndMessage,
            stop.NativeHudSelectors.Value.MessageForCurrentSide(0));
    }

    [Fact]
    public void InvalidNativeHudSelectorOrSideDoesNotInventRetailMessage()
    {
        var selectors = new Rac1TaxiNativeHud(1, 4);
        Assert.Null(selectors.MessageForCurrentSide(-1));
        Assert.Null(selectors.MessageForCurrentSide(2));
        Assert.Equal(0x0bce, selectors.MessageForCurrentSide(1));
        Assert.Equal(0x0bcd, selectors.MessageForCurrentSide(0));
        Assert.Null(new Rac1TaxiNativeHud(-1, 9).MessageForCurrentSide(1));
        Assert.All(Rac1MetropolisTaxiSession.TryCreate(Kerwan())!.Stops,
            stop => Assert.Null(stop.NativeHudSelectors));
    }

    [Fact]
    public void RetailTaxiAdmissionSelectorsPreserveNativeMinusOneSentinels()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        if (string.IsNullOrWhiteSpace(iso) || !File.Exists(iso)) return;
        using var reader = new FileRandomAccessReader(iso);
        var world = Rac1WorldImport.Build(reader, 3);
        foreach (var (index, activeCuboid) in new[] { (919, 10), (920, 28) })
        {
            var obj = world.DynamicObjects!.Single(o => o.InstanceIndex == index);
            byte[] pvar = obj.NativePayloads!.Single(p => p.Format == "rac1-pvar").Data;
            Assert.Equal(activeCuboid, BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x80, 4)));
            for (int i = 1; i < 5; i++)
                Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x80 + i * 4, 4)));
        }

        // Class-1012 only enters point_in_box when its optional +0xa0
        // selector is armed; both retail shuttles leave it at -1.
        foreach (int index in new[] { 961, 962 })
        {
            var obj = world.DynamicObjects!.Single(o => o.InstanceIndex == index);
            byte[] pvar = obj.NativePayloads!.Single(p => p.Format == "rac1-pvar").Data;
            Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0xa0, 4)));
        }
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

        // Native state-0 +0x80 side: initial path selector
        // comes from PVar+0x90+(side*4), not always the authored Moby origin.
        Assert.Equal(0, session.Stops.Single(s => s.InstanceIndex == 961).NativeInitialSide);
        Assert.Equal(1, session.Stops.Single(s => s.InstanceIndex == 962).NativeInitialSide);
        Assert.All(session.Stops, stop =>
        {
            Assert.True(stop.NativeHoverPhases.HasValue);
            var phases = stop.NativeHoverPhases!.Value;
            Assert.True(double.IsFinite(phases.Height) &&
                double.IsFinite(phases.Tilt) && double.IsFinite(phases.Spin));
        });

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

        foreach (var (instance, approachId, approachCount, cuboidId) in new[]
        {
            (919, 62, 200, 10),
            (920, 70, 50, 28),
        })
        {
            var obj = world.DynamicObjects!.Single(o => o.InstanceIndex == instance);
            var pvar = obj.NativePayloads!.Single(p => p.Format == "rac1-pvar").Data;
            Assert.Equal(approachId,
                BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x94, 4)));
            Assert.Equal(61,
                BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0xbc, 4)));
            Assert.Equal(cuboidId,
                BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x80, 4)));
            var stop = session.Stops.Single(s => s.InstanceIndex == instance);
            Assert.Equal(approachCount, stop.ApproachPath!.Count);
            Assert.Equal(75, stop.DeparturePath!.Count);
            Assert.NotNull(stop.ApproachTrigger);
            Assert.InRange(stop.ApproachPath[^1].DistanceSquared(stop.Position), 0, .15 * .15);
            Assert.InRange(stop.ApproachTrigger.HalfExtents.X, 1, 30);
        }

        foreach (var (instance, nativePath, points) in new[]
        {
            (961, 65, 150),
            (962, 58, 75),
        })
        {
            var obj = world.DynamicObjects!.Single(o => o.InstanceIndex == instance);
            var pvar = obj.NativePayloads!.Single(p => p.Format == "rac1-pvar").Data;
            Assert.Equal(nativePath,
                BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x90, 4)));
            var stop = session.Stops.Single(s => s.InstanceIndex == instance);
            Assert.NotNull(stop.ReturnPath);
            Assert.Equal(points, stop.ReturnPath.Count);
            Assert.InRange(stop.ReturnPath[0].DistanceSquared(stop.OutboundPath[^1]),
                0, 0.15 * 0.15);
            Assert.InRange(stop.ReturnPath[^1].DistanceSquared(stop.Position),
                0, 0.15 * 0.15);
        }
    }
}
