using System.Buffers.Binary;
using OBP.PS2.Geometry;

namespace OBP.Tests;

public sealed class MobyBangleBoundsTests
{
    [Fact]
    public void RejectsMissingDirectoryAndPacketsThatEscapeTheOriginalClass()
    {
        Assert.Throws<InvalidDataException>(() => GcUyaMoby.ReadBangle(new byte[0x47], 0));
        var bytes = new byte[0x80];
        Assert.Throws<InvalidDataException>(() => GcUyaMoby.ReadBangle(bytes, 0));
        bytes[0x2c] = 7;
        bytes[0x74] = 1;
        bytes[0x75] = 2;
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 0x60);
        Assert.Throws<InvalidDataException>(() => GcUyaMoby.ReadBangle(bytes, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GcUyaMoby.ReadBangle(bytes, 15));
    }
}
