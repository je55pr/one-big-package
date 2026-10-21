using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Gameplay;
using OBP.IO;
using OBP.RAC1;

namespace OBP.Tests;

public sealed class Rac1NovalisCrankDoorTests
{
    [Fact]
    public void AuthoredDoorPvarLinksCorrectCrankAndOpenTarget()
    {
        Span<byte> raw = stackalloc byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(raw, 296);
        BinaryPrimitives.WriteSingleLittleEndian(raw.Slice(12), 173f);
        BinaryPrimitives.WriteSingleLittleEndian(raw.Slice(16), 87f);
        var door = Rac1NovalisCrankDoorSession.DecodeDoor(683, raw,
            new Vector3(174.562f, 87.79f, 62f));

        Assert.Equal(296, door.CrankInstanceIndex);
        Assert.Equal(new Vector3(173, 87, 62), door.OpenNative);
        Assert.Equal(new Vector3(174.562f, 87.79f, 62f), door.Interpolate(0));
        Assert.Equal(new Vector3(173, 87, 62), door.Interpolate(1));
        Assert.Equal(62f, door.Interpolate(.5).Z);
    }

    [Fact]
    public void CaveDoorPvarRotatesWithCrank297ByShortestWrappedAngle()
    {
        byte[] pvar = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(pvar, 297);
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(8), 75.745f);
        var session = new Rac1NovalisCrankDoorSession();
        session.RegisterCrank(296);
        session.RegisterCrank(297);
        session.RegisterCaveDoor(Rac1NovalisCrankDoorSession.DecodeCaveDoor(672, pvar, 0.25f));
        session.ValidateLinks();
        Assert.Equal(0.25f, session.CaveDoorYaw(672));
        session.Turn(297, 1);
        Assert.InRange(session.CaveDoorYaw(672), 1.321f, 1.323f);
        Assert.Throws<InvalidDataException>(() =>
            Rac1NovalisCrankDoorSession.DecodeCaveDoor(672, pvar[..12], 0));
    }

    [SkippableFact]
    public void RetailNovalisCaveDoorPointsToCrank297()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, 1);
        var dynamicObjects = world.DynamicObjects ?? throw new InvalidDataException("Novalis has no objects");
        var caveSource = Assert.Single(dynamicObjects,
            x => x.NativeClassId == Rac1NovalisCrankDoorSession.CaveDoorClass);
        Assert.Equal(672, caveSource.InstanceIndex);
        byte[] payload = Assert.Single(caveSource.NativePayloads!,
            x => x.Format == "rac1-pvar").Data;
        var cave = Rac1NovalisCrankDoorSession.DecodeCaveDoor(caveSource.InstanceIndex, payload, 0f);
        Assert.Equal(297, cave.CrankInstanceIndex);
        Assert.InRange(cave.OpenNativeYaw, 1.321f, 1.323f);
        Assert.Equal(2, dynamicObjects.Count(x => x.NativeClassId == 280));
        Assert.Equal(4, dynamicObjects.Count(x => x.NativeClassId == 665));
    }

    [Fact]
    public void CaveDoorReferenceRequiresItsRealCrank()
    {
        var session = new Rac1NovalisCrankDoorSession();
        session.RegisterCrank(296);
        session.RegisterCaveDoor(new(672, 297, 0, 1));
        Assert.Throws<InvalidDataException>(session.ValidateLinks);
    }

    [Fact]
    public void EachCrankMovesOnlyItsAuthoredDoorPair()
    {
        var session = new Rac1NovalisCrankDoorSession();
        session.RegisterCrank(296);
        session.RegisterCrank(297);
        session.RegisterDoor(new(683, 296, new(10, 10, 62), new(9, 10, 62)));
        session.RegisterDoor(new(684, 296, new(11, 10, 62), new(12, 10, 62)));
        session.RegisterDoor(new(685, 297, new(20, 10, 57), new(19, 10, 57)));
        session.RegisterDoor(new(686, 297, new(21, 10, 57), new(22, 10, 57)));
        session.ValidateLinks();

        Assert.Equal(.5d, session.Turn(296, .5d));
        Assert.Equal(new Vector3(9.5f, 10, 62), session.DoorPosition(683));
        Assert.Equal(new Vector3(11.5f, 10, 62), session.DoorPosition(684));
        Assert.Equal(new Vector3(20, 10, 57), session.DoorPosition(685));
        Assert.Equal(1d, session.Turn(296, 4));
        Assert.Equal(new Vector3(9, 10, 62), session.DoorPosition(683));
        Assert.Equal(0d, session.Progress(297));
    }

    [Fact]
    public void InvalidLinksAndHostProgressAreRejected()
    {
        var session = new Rac1NovalisCrankDoorSession();
        session.RegisterCrank(296);
        session.RegisterDoor(new(685, 297, Vector3.One, Vector3.Zero));
        Assert.Throws<InvalidDataException>(session.ValidateLinks);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Turn(296, -1d));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Turn(296, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Turn(297, .5d));
        Assert.Throws<InvalidOperationException>(() => session.RegisterCrank(296));
    }
}
