using System.Numerics;
using OBP.RAC2.Gameplay;

namespace OBP.Tests;

public sealed class Rac2BoltMotionTests
{
    private static Rac2BoltMotionSession Create() => new(new(0, 0, 1), (min, max) => (min + max) / 2);

    [Fact]
    public void LaunchUsesNativeSpeedAndGravityRatherThanFixedScatterPositions()
    {
        var bolt = Create();
        Assert.Equal(.5f, bolt.Position.X, 5);
        Assert.Equal(1f, bolt.Position.Z);
        Assert.InRange(bolt.Velocity.X, 0, 3f / 60);
        Assert.InRange(bolt.Velocity.Z, 3.7f / 60, 6f / 60);
        float before = bolt.Velocity.Z;
        bolt.Step(new(100, 100, 100), Vector3.Zero, 3, 1.75f, true);
        Assert.Equal(before - .003f, bolt.Velocity.Z, 6);
        Assert.Equal(1, bolt.NativeState);
    }

    [Fact]
    public void DistantPickupBouncesAndRestsUntilPlayerEntersAttractionVolume()
    {
        var bolt = Create();
        var far = new Vector3(100, 100, 1);
        for (int tick = 0; tick < 40; tick++) bolt.Step(far, far, 3, 1.75f, false);
        bolt.Step(far, far, 3, 1.75f, false, new(new(bolt.Position.X, bolt.Position.Y, 0), Vector3.UnitZ));
        Assert.Equal(2, bolt.NativeState);
        for (int tick = 0; tick < 60; tick++) bolt.Step(far, far, 3, 1.75f, true);
        Assert.Equal(3, bolt.NativeState);
        Assert.False(bolt.Collected);
        var player = bolt.Position;
        var socket = player + Vector3.UnitZ * .7f;
        bolt.Step(player, socket, 3, 1.75f, true);
        Assert.Equal(4, bolt.NativeState);
        Assert.False(bolt.Collected);
        for (int tick = 0; tick < 120; tick++) bolt.Step(player, socket, 3, 1.75f, true);
        Assert.True(bolt.Collected);
        Assert.Equal(socket, bolt.Position);
    }

    [Fact]
    public void AttractionHonoursPlayerHeightAndDeathAdmission()
    {
        var bolt = Create();
        var far = new Vector3(100, 100, 1);
        for (int tick = 0; tick < 40; tick++) bolt.Step(far, far, 3, 1.75f, false);
        bolt.Step(far, far, 3, 1.75f, false, new(Vector3.Zero, Vector3.UnitZ));
        for (int tick = 0; tick < 60; tick++) bolt.Step(far, far, 3, 1.75f, false);
        var high = bolt.Position + Vector3.UnitZ * 1.75f;
        bolt.Step(high, high, 3, 1.75f, true);
        Assert.Equal(3, bolt.NativeState);
        bolt.Step(bolt.Position, bolt.Position, 3, 1.75f, false);
        bolt.Step(far, far, 3, 1.75f, true);
        Assert.Equal(3, bolt.NativeState);
        Assert.False(bolt.Collected);
        bolt.Step(bolt.Position, bolt.Position + Vector3.UnitZ * .7f, 3, 1.75f, true);
        Assert.Equal(4, bolt.NativeState);
        var frozen = bolt.Position;
        for (int tick = 0; tick < 120; tick++) bolt.Step(frozen, frozen, 3, 1.75f, false);
        Assert.Equal(frozen, bolt.Position);
        Assert.False(bolt.Collected);
    }

    [Fact]
    public void SlopedContactClearsTheSurfaceAndFirstHopRetainsItsLastSample()
    {
        var bolt = Create();
        var far = new Vector3(100, 100, 1);
        for (int tick = 0; tick < 40; tick++) bolt.Step(far, far, 3, 1.75f, false);
        var hit = new Rac2BoltContact(Vector3.Zero, new(.6f, 0, .8f));
        bolt.Step(far, far, 3, 1.75f, false, hit);
        var anchor = hit.Position + hit.Normal * Rac2BoltMotionSession.ContactRadius;
        Assert.Equal(anchor, bolt.Position);
        for (int tick = 0; tick < 35; tick++) bolt.Step(far, far, 3, 1.75f, false);
        Assert.Equal(3, bolt.NativeState);
        Assert.True(bolt.Position.Z > anchor.Z);
    }

    [Fact]
    public void RetailActivationSurvivesLeavingTheVolumeBeforeLanding()
    {
        var bolt = Create();
        bolt.Step(bolt.Position, bolt.Position, 3, 1.75f, true);
        var far = new Vector3(100, 100, 1);
        for (int tick = 0; tick < 40; tick++) bolt.Step(far, far, 3, 1.75f, true);
        bolt.Step(far, far, 3, 1.75f, true, new(Vector3.Zero, Vector3.UnitZ));
        Assert.Equal(4, bolt.NativeState);
        Assert.False(bolt.Collected);
    }
}
