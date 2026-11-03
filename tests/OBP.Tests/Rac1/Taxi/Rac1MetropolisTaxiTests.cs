using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using static OBP.Tests.Rac1MetropolisTaxiFixtures;

namespace OBP.Tests;

/// <summary>
/// Synthetic Kerwan taxi behaviour: boarding, flight legs, traffic phases, interruption and reset.
/// Retail-gated checks of the same system live in <see cref="Rac1MetropolisTaxiRetailTests"/>.
/// </summary>
public sealed class Rac1MetropolisTaxiTests
{
    [Theory]
    [InlineData(-1d, 0d)]
    [InlineData(1d, 0d)]
    [InlineData(0d, -1d)]
    [InlineData(0d, 1d)]
    [InlineData(2d, 1d)]
    [InlineData(-2d, -1d)]
    public void KerwanHostVisualYawRotatesImportedLocalNegativeXIntoTravel(double dx, double dz)
    {
        double yaw = Rac1TaxiHostPresentationPolicy.SceneVisualYaw(dx, dz);
        double length = Math.Sqrt(dx * dx + dz * dz);
        // Godot yaw rotates imported model front -X to (-cos, sin) in XZ.
        Assert.Equal(dx / length, -Math.Cos(yaw), 10);
        Assert.Equal(dz / length, Math.Sin(yaw), 10);
    }

    [Fact]
    public void KerwanHostVisualYawRejectsUndefinedTangent()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1TaxiHostPresentationPolicy.SceneVisualYaw(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1TaxiHostPresentationPolicy.SceneVisualYaw(double.NaN, 1));
    }

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
        var session = Rac1MetropolisTaxiSession.TryCreate(KerwanWithApproachTraffic919())!;
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
    public void PathlessClass816ModeReturnsToIdleAfterFivePlanarUnitsWithoutDepartureFlight()
    {
        var pvar = new byte[0xd0];
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0xbc, 4), -1);
        var session = Rac1MetropolisTaxiSession.TryCreate(
            KerwanWithApproachTraffic919(new RuntimeOpaquePayload("rac1-pvar", pvar)))!;
        var taxi = session.Stops.Single(s => s.InstanceIndex == 919);
        Assert.Equal(-1, taxi.NativeFirstDepartureSelector);
        Assert.Equal(-1, taxi.NativeDepartureSelectors![0]);
        var trigger = taxi.ApproachTrigger!.Center;
        session.TickTraffic(trigger, 0);
        for (int frame = 0; frame < 24000 &&
             session.PhaseOf(919) != Rac1MetropolisTaxiSession.TrafficPhase.Docked; frame++)
            session.TickTraffic(trigger, 1d / 60);
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.Docked, session.PhaseOf(919));

        Assert.True(session.TryBoard(taxi.Position));
        Rac1TaxiFlight? flight = null;
        for (int frame = 0; frame < 24000; frame++)
        {
            flight = session.Tick(1d / 60);
            if (flight?.JustArrived == true) break;
        }
        Assert.True(flight?.JustArrived);
        session.CompleteArrival();

        var destination = taxi.OutboundPath[^1];
        session.TickTraffic(destination with { Y = destination.Y + 100 }, 0);
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.ParkedAtDestination,
            session.PhaseOf(919));
        session.TickTraffic(destination with { X = destination.X + 5 }, 0);
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.ParkedAtDestination,
            session.PhaseOf(919));
        var frames = session.TickTraffic(
            destination with { X = destination.X + 5.01 }, 0);
        Assert.Equal(Rac1MetropolisTaxiSession.TrafficPhase.WaitingForHero,
            session.PhaseOf(919));
        Assert.Equal(destination,
            frames.Single(frame => frame.InstanceIndex == 919).Position);
        var next = session.TickTraffic(new Rac1TaxiPoint(0, 0, 0), 0);
        Assert.Equal(taxi.ApproachPath![0],
            next.Single(frame => frame.InstanceIndex == 919).Position);
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
}
