using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Gameplay;

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
