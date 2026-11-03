using System.Buffers.Binary;
using OBP.PS2.Animation;

namespace OBP.Tests;

public sealed class Ps2SkeletalAnimationTests
{
    [Fact]
    public void PackedFramesDecodeEveryRotationAndAuxiliaryEncodingAndTheWSign()
    {
        byte[] bytes = Packed();
        var frame = Assert.Single(Ps2SkeletalAnimation.ReadSequence(bytes, 0, 1, 0, true).Frames);
        Assert.Equal(.25f, frame.TransitionRate); Assert.Equal(32, frame.TimelineUnits);
        var rotation = Assert.Single(frame.Rotations);
        Assert.Equal(41, rotation.X); Assert.Equal(-10, rotation.Y); Assert.Equal(16384, rotation.Z);
        Assert.InRange(rotation.W, -28378, -28376);
        var scale = Assert.Single(frame.Scales);
        Assert.Equal(4092, scale.X); Assert.Equal(4096, scale.Y); Assert.Equal(0x8000, scale.JointTag);
        var translation = Assert.Single(frame.Translations);
        Assert.Equal(-100, translation.X); Assert.Equal(-12, translation.Y); Assert.Equal(32, translation.Z);
    }

    [Fact]
    public void UnknownTagsTruncationAndOverlappingPartitionsFailAtTheCodecBoundary()
    {
        var unknown = Packed(); U32(unknown, 0x1c, 0xe0000060);
        Assert.Throws<NotSupportedException>(() => Ps2SkeletalAnimation.ReadSequence(unknown, 0, 1, 0, true));
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(Packed()[..0x69], 0, 1, 0, true));
        var overlap = Packed(); U32(overlap, 0x20, 6 | (4u << 10) | (7u << 21));
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(overlap, 0, 1, 0, true));
        var missingJoint = Packed(); U16(missingJoint, 0x20 + 26, 1);
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(missingJoint, 0, 1, 0, true));
    }

    [Fact]
    public void AnimationOnlyArmVectorsRetainTheirFullSkeletonJointTags()
    {
        byte[] b = new byte[0x80]; b[0x10] = 1; b[0x13] = 0xff; U32(b, 0x1c, 0x40);
        U32(b, 0x40, BitConverter.SingleToUInt32Bits(.25f)); U16(b, 0x48, 8); U16(b, 0x4e, 1);
        U16(b, 0x56, 32767); U16(b, 0x5e, 52);
        var clip = Ps2SkeletalAnimation.ReadSequence(b, 0, 1, 0, false, 111);
        Assert.Equal(52, Assert.Single(Assert.Single(clip.Frames).Translations).Joint);
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(b, 0, 1, 0, false));
    }

    private static byte[] Packed()
    {
        byte[] b = new byte[0x90]; b[0x10] = 1; U32(b, 0x1c, 0xf0000060);
        U32(b, 0x20, 4 | (6u << 10) | (7u << 21));
        U16(b, 0x24, 1); U16(b, 0x26, 2); U16(b, 0x28, 3); b[0x2a] = b[0x2b] = 1;
        U16(b, 0x2c, 4096); U16(b, 0x2e, 4096); U16(b, 0x30, 4096); U16(b, 0x32, 0x8000);
        U16(b, 0x34, 12); U16(b, 0x36, unchecked((ushort)-20)); U16(b, 0x38, 32);
        U16(b, 0x60, 4); U16(b, 0x62, 32); U32(b, 0x64, 1021);
        U16(b, 0x68, 16384); b[0x6a] = 10; b[0x6b] = 1;
        b[0x6c] = 1; b[0x6d] = 0xff; b[0x6e] = 6; U16(b, 0x6f, unchecked((ushort)-100)); b[0x71] = 2;
        return b;
    }

    private static void U16(byte[] b, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), value);
    private static void U32(byte[] b, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), value);
}
