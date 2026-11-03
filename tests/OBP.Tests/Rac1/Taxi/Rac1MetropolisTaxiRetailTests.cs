using OBP.Core.Math;
using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using Xunit.Abstractions;
using static OBP.Tests.Rac1MetropolisTaxiFixtures;

namespace OBP.Tests;

/// <summary>
/// Retail Kerwan world for the taxi tests, built once per test class. Follows the other R&amp;C1 retail suites:
/// the disc is opened through <see cref="Rac1VerifiedDisc"/> (authority size and SHA-256 are checked), and tests
/// skip when <c>OBP_RAC1_ISO</c> is not set.
/// </summary>
public sealed class Rac1KerwanRetailFixture : IDisposable
{
    private readonly FileRandomAccessReader? _reader;

    public Rac1KerwanRetailFixture()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        if (string.IsNullOrWhiteSpace(iso)) return;

        _reader = Rac1VerifiedDisc.Open(iso);
        try
        {
            World = Rac1WorldImport.Build(_reader, 3);
        }
        catch
        {
            _reader.Dispose();
            throw;
        }
    }

    /// <summary>The imported Kerwan world, or <see langword="null"/> when <c>OBP_RAC1_ISO</c> is not set.</summary>
    public RuntimeWorld? World { get; }

    public void Dispose() => _reader?.Dispose();
}

/// <summary>
/// Metropolis taxi checks against the retail Kerwan level (<c>OBP_RAC1_ISO</c>). Tests only read the shared world;
/// every test builds its own <see cref="Rac1MetropolisTaxiSession"/>.
/// </summary>
public sealed class Rac1MetropolisTaxiRetailTests : IClassFixture<Rac1KerwanRetailFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly Rac1KerwanRetailFixture _kerwan;

    public Rac1MetropolisTaxiRetailTests(ITestOutputHelper output, Rac1KerwanRetailFixture kerwan)
    {
        _output = output;
        _kerwan = kerwan;
    }

    private RuntimeWorld KerwanWorld()
    {
        Skip.If(_kerwan.World is null, "OBP_RAC1_ISO not set");
        return _kerwan.World!;
    }

    private static Rac1MetropolisTaxiSession NewSession(RuntimeWorld world)
    {
        var session = Rac1MetropolisTaxiSession.TryCreate(world);
        Assert.NotNull(session);
        return session;
    }
    private static byte[] Pvar(RuntimeWorld world, int instance) =>
        world.DynamicObjects!.Single(o => o.InstanceIndex == instance)
            .NativePayloads!.Single(p => p.Format == "rac1-pvar").Data;

    private static int PvarInt(byte[] pvar, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(offset, 4));

    [SkippableFact]
    public void RetailTaxisLogNormalSpeedPathDurationsAndMeshDirection()
    {
        var world = KerwanWorld();
        var session = NewSession(world);
        foreach (var stop in session.Stops.Where(s => s.OutboundPath.Count > 1))
        {
            var actor = world.DynamicObjects!.Single(o => o.InstanceIndex == stop.InstanceIndex);
            var m = actor.Transform.Matrix;
            _output.WriteLine($"i{stop.InstanceIndex} class={stop.NativeClassId} initial Moby basis X=({m[0]:F2},{m[1]:F2},{m[2]:F2}) Y=({m[4]:F2},{m[5]:F2},{m[6]:F2})");
            if (actor.Meshes.Count > 0)
            {
                var verts = actor.Meshes.SelectMany(mesh => mesh.Positions)
                    .ToArray();
                _output.WriteLine($"    raw vertex coordinate extents: {verts.Length}");
                foreach (var mesh in actor.Meshes.Take(1))
                {
                    var v = mesh.Positions;
                    _output.WriteLine($"    local bounds xyz: ({Enumerable.Range(0, v.Length / 3).Min(i => v[i * 3]):F2},{Enumerable.Range(0, v.Length / 3).Max(i => v[i * 3]):F2}) ({Enumerable.Range(0, v.Length / 3).Min(i => v[i * 3 + 1]):F2},{Enumerable.Range(0, v.Length / 3).Max(i => v[i * 3 + 1]):F2}) ({Enumerable.Range(0, v.Length / 3).Min(i => v[i * 3 + 2]):F2},{Enumerable.Range(0, v.Length / 3).Max(i => v[i * 3 + 2]):F2})");
                }
            }
            foreach (var (leg, path) in new[] { ("out", stop.OutboundPath), ("back", stop.ReturnPath) })
            {
                if (path is not { Count: > 1 }) continue;
                var motion = new Rac1TaxiNativeMotion(path);
                double len = 0;
                for (int i = 1; i < path.Count; i++)
                    len += Math.Sqrt(path[i].DistanceSquared(path[i - 1]));
                int frames = 0;
                double peak = 0;
                while (!motion.Arrived && frames < 24000)
                {
                    motion.Advance(1d / 60);
                    frames++;
                    peak = Math.Max(peak, motion.SpeedPerFrame * 60);
                }
                Assert.True(motion.Arrived);
                double presentationSeconds = frames / 60d /
                    Rac1TaxiHostPresentationPolicy.RouteClockMultiplier;
                // Every Kerwan route must remain under the retail 18u/s
                // maximum even with our explicitly provisional host pacing.
                Assert.InRange(peak * Rac1TaxiHostPresentationPolicy.RouteClockMultiplier,
                    0, Rac1TaxiNativeMotion.MaxUnitsPerSecond);
                Assert.InRange(presentationSeconds, 8d, 35d);
                _output.WriteLine($"    {leg}: points={path.Count} length={len:F1}u nativeClock={frames / 60d:F1}s hostClock={presentationSeconds:F1}s avg={len / presentationSeconds:F2}u/s peak={peak * Rac1TaxiHostPresentationPolicy.RouteClockMultiplier:F2}u/s");
            }
        }
    }

    [SkippableFact]
    public void RetailKerwanTaxisUseNativeSeekingAndCompleteBothFlightFamilies()
    {
        var session = NewSession(KerwanWorld());
        Assert.All(session.Stops.Where(s => s.OutboundPath.Count > 1),
            stop => Assert.True(stop.NativeSeekEvidenced));

        var inbound = session.Stops.Single(s => s.InstanceIndex == 919);
        Assert.NotNull(inbound.ApproachTrigger);
        session.TickTraffic(inbound.ApproachTrigger!.Center, 0);
        int frames = 0;
        Assert.NotEmpty(inbound.ApproachPath!);
        while (session.PhaseOf(919) != Rac1MetropolisTaxiSession.TrafficPhase.Docked &&
            frames++ < 24000)
            _ = session.TickTraffic(inbound.ApproachTrigger.Center, 1d / 60)
                .Single(f => f.InstanceIndex == 919);
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

    [SkippableTheory]
    [InlineData(961, 1, 2, 0x0bce, 0x0bcf)]
    [InlineData(962, 3, 4, 0x0bcb, 0x0bcd)]
    public void RetailClass1012HudMessagesFollowAuthoredNextSideSelectors(
        int instance, int sideZero, int sideOne,
        int sourceDockMessage, int otherEndMessage)
    {
        var world = KerwanWorld();
        var pvar = Pvar(world, instance);
        Assert.Equal(sideZero, PvarInt(pvar, 0x98));
        Assert.Equal(sideOne, PvarInt(pvar, 0x9c));

        var taxis = NewSession(world);
        var stop = taxis.Stops.Single(s => s.InstanceIndex == instance);
        var lease = Assert.IsType<Rac1TaxiNativeHudElementLease>(stop.NativeHudElementLease);
        Assert.Equal(
            Rac1TaxiNativeHudElementLease.HudCommandKind.Request,
            lease.FocusCommand().Kind);
        lease.AcceptRequestResult(47 + instance);
        Assert.Equal(
            Rac1TaxiNativeHudElementLease.HudCommandKind.ExtendVisibility,
            lease.FocusCommand().Kind);
        Assert.Equal(10, lease.FocusCommand().MinimumVisibleTicks);
        taxis.Reset();
        Assert.Equal(-1, lease.NativePvarHudSerial);
        Assert.Equal(
            Rac1TaxiNativeHudElementLease.HudCommandKind.Request,
            lease.FocusCommand().Kind);
        Assert.Equal(sideZero, stop.NativeHudSelectors!.Value.SideZeroSelector);
        Assert.Equal(sideOne, stop.NativeHudSelectors.Value.SideOneSelector);
        Assert.Equal(sourceDockMessage,
            stop.NativeHudSelectors.Value.MessageForCurrentSide(1));
        Assert.Equal(otherEndMessage,
            stop.NativeHudSelectors.Value.MessageForCurrentSide(0));
    }

    [SkippableTheory]
    [InlineData(961, 0)]
    [InlineData(962, 1)]
    public void RetailClass1012RepositionsAlongCorrectUnoccupiedNativeLeg(
        int instance, int initialSide)
    {
        var session = NewSession(KerwanWorld());
        var stop = session.Stops.Single(s => s.InstanceIndex == instance);
        var dock = Assert.IsType<Rac1TaxiNativeShuttleDock>(stop.NativeShuttleDock);
        var autonomous = new Rac1TaxiNativeShuttleTraffic(dock);

        Assert.Equal(initialSide, autonomous.CurrentSide);
        Assert.Equal(dock.NativeInitialWaypoint, autonomous.Position);
        Rac1TaxiPoint playerOtherEnd = initialSide == 0
            ? dock.SideOnePath[0] : dock.SideZeroPath[0];
        Assert.True(autonomous.TryStartUnoccupiedReposition(playerOtherEnd));
        Assert.Equal(3, autonomous.NativeState);
        for (int i = 0; i < 24000 && autonomous.NativeState == 3; i++)
            autonomous.Advance(1d / 60);
        Assert.Equal(1, autonomous.NativeState);
        Assert.Equal(initialSide == 0
                ? dock.SideZeroPath[^1] : dock.SideOnePath[^1],
            autonomous.Position);
        Assert.Equal(15d / 60, autonomous.FocusDelayRemaining, 10);
    }

    [SkippableFact]
    public void RetailTaxiAdmissionSelectorsPreserveNativeMinusOneSentinels()
    {
        var world = KerwanWorld();
        foreach (var (index, activeCuboid) in new[] { (919, 10), (920, 28) })
        {
            byte[] pvar = Pvar(world, index);
            Assert.Equal(activeCuboid, PvarInt(pvar, 0x80));
            for (int i = 1; i < 5; i++)
                Assert.Equal(-1, PvarInt(pvar, 0x80 + i * 4));
        }

        // Class-1012 only enters point_in_box when its optional +0xa0
        // selector is armed; both retail shuttles leave it at -1.
        foreach (int index in new[] { 961, 962 })
        {
            byte[] pvar = Pvar(world, index);
            Assert.Equal(-1, PvarInt(pvar, 0xa0));
        }
    }

    [SkippableFact]
    public void RetailClass1012ShuttleDocksStartOnNativeSideWithoutForcedStartOrCuboid()
    {
        var world = KerwanWorld();
        var session = NewSession(world);

        // Native state-0 +0x80 side: initial path selector
        // comes from PVar+0x90+(side*4), not always the authored Moby origin.
        Assert.Equal(0, session.Stops.Single(s => s.InstanceIndex == 961).NativeInitialSide);
        Assert.Equal(1, session.Stops.Single(s => s.InstanceIndex == 962).NativeInitialSide);
        foreach (var (index, nativeSide) in new[] { (961, 0), (962, 1) })
        {
            var shuttle = session.Stops.Single(s => s.InstanceIndex == index);
            var pvar = Pvar(world, index);
            // Retail instances have neither the optional cuboid trigger
            // nor the separate forced-start / exit flags armed.
            Assert.Equal(-1, PvarInt(pvar, 0xa0));
            Assert.Equal(0, PvarInt(pvar, 0x7c));
            Assert.Equal(0, PvarInt(pvar, 0x8c));
            var dock = Assert.IsType<Rac1TaxiNativeShuttleDock>(shuttle.NativeShuttleDock);
            Assert.Equal(nativeSide, dock.InitialSide);
            Assert.Equal(-1, dock.OptionalCuboidSelector);
            Assert.Equal(shuttle.ReturnPath![0], dock.SideZeroPath[0]);
            Assert.Equal(shuttle.OutboundPath[0], dock.SideOnePath[0]);
            Assert.Equal(nativeSide == 0 ? dock.SideZeroPath[0] : dock.SideOnePath[0],
                dock.NativeInitialWaypoint);
            if (index == 961)
                Assert.True(dock.NativeInitialWaypoint.DistanceSquared(shuttle.Position) > 25);
            else
                Assert.True(dock.NativeInitialWaypoint.DistanceSquared(shuttle.Position) < 0.01);
            Assert.Equal(1, dock.RepositionSide(dock.SideZeroPath[0], dock.SideOnePath[0]));
            Assert.Equal(0, dock.RepositionSide(dock.SideOnePath[0], dock.SideZeroPath[0]));
            Assert.Null(dock.RepositionSide(dock.SideOnePath[0], dock.SideOnePath[0]));
        }
    }

    [SkippableFact]
    public void RetailKerwanTaxisCarryFiniteNativeHoverPhases()
    {
        var world = KerwanWorld();
        var session = NewSession(world);

        Assert.All(session.Stops, stop =>
        {
            Assert.True(stop.NativeHoverPhases.HasValue);
            var phases = stop.NativeHoverPhases!.Value;
            Assert.True(double.IsFinite(phases.Height) &&
                double.IsFinite(phases.Tilt) && double.IsFinite(phases.Spin));
        });
    }

    [SkippableFact]
    public void RetailKerwanPvarsSelectExactOutboundSplines()
    {
        var world = KerwanWorld();
        var session = NewSession(world);

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
            var pvar = Pvar(world, route.Index);
            Assert.Equal(route.NativePath, PvarInt(pvar, route.PvarOffset));
            var stop = session.Stops.Single(s => s.InstanceIndex == route.Index);
            Assert.Equal(route.Points, stop.OutboundPath.Count);
            var endpoint = stop.OutboundPath[^1];
            Assert.InRange(endpoint.DistanceSquared(route.End), 0, 0.0002);
        }
    }

    [SkippableFact]
    public void RetailClass816PvarsSelectApproachDepartureSelectorsAndTriggerCuboid()
    {
        var world = KerwanWorld();
        var session = NewSession(world);

        foreach (var (instance, approachId, approachCount, cuboidId) in new[]
        {
            (919, 62, 200, 10),
            (920, 70, 50, 28),
        })
        {
            var pvar = Pvar(world, instance);
            Assert.Equal(approachId, PvarInt(pvar, 0x94));
            Assert.Equal(61, PvarInt(pvar, 0xbc));
            var nativeExitSelectors = session.Stops.Single(s =>
                s.InstanceIndex == instance).NativeDepartureSelectors;
            Assert.NotNull(nativeExitSelectors);
            Assert.Equal(5, nativeExitSelectors.Count);
            for (int slot = 0; slot < 5; slot++)
                Assert.Equal(
                    PvarInt(pvar, 0xbc + slot * 4),
                    nativeExitSelectors[slot]);
            Assert.Equal(61, session.Stops.Single(s =>
                s.InstanceIndex == instance).NativeFirstDepartureSelector);
            Assert.Equal(cuboidId, PvarInt(pvar, 0x80));
            var stop = session.Stops.Single(s => s.InstanceIndex == instance);
            Assert.Equal(approachCount, stop.ApproachPath!.Count);
            Assert.Equal(75, stop.DeparturePath!.Count);
            Assert.NotNull(stop.ApproachTrigger);
            Assert.InRange(stop.ApproachPath[^1].DistanceSquared(stop.Position), 0, .15 * .15);
            Assert.InRange(stop.ApproachTrigger.HalfExtents.X, 1, 30);
        }
    }

    [SkippableFact]
    public void RetailClass1012ReturnSplinesChainOutboundEndToHome()
    {
        var world = KerwanWorld();
        var session = NewSession(world);

        foreach (var (instance, nativePath, points) in new[]
        {
            (961, 65, 150),
            (962, 58, 75),
        })
        {
            var pvar = Pvar(world, instance);
            Assert.Equal(nativePath, PvarInt(pvar, 0x90));
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
