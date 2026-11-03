using System.Buffers.Binary;
using System.Numerics;
using OBP.Core;
using OBP.PS2.Presentation;

namespace OBP.Tests;

public sealed class Ps2RatchetSkeletalDecoderTests
{
    [Theory]
    [InlineData(ObpSourceGame.Rac2, "GC")]
    [InlineData(ObpSourceGame.Rac3, "UYA")]
    public void FixturePreservesRigArmBanksSparseChannelsAndProvenance(ObpSourceGame game, string label)
    {
        var fixture = Fixture.Create();
        var result = fixture.Decode(game, label);
        Assert.Equal(111, result.Skeleton.Joints.Count);
        Assert.Equal(31, result.Skeleton.Attachments.Count);
        Assert.Equal(56, result.Skeleton.Attachments[0].Joint);
        Assert.Equal(Enumerable.Range(52, 20), result.Skeleton.Attachments[12].LayerJoints);
        Assert.Equal(Enumerable.Range(72, 20), result.Skeleton.Attachments[13].LayerJoints);
        Assert.Equal(-1, result.Skeleton.Joints[0].Parent);
        Assert.Equal(0, result.Skeleton.Joints[52].Parent);
        Assert.Equal(new Vector3(26, 0, 0), result.Skeleton.Joints[52].Translation);
        Assert.Equal(2, result.Skeleton.Joints[52].InverseBind.M41);

        var body = Assert.Single(result.Body).Value;
        Assert.Equal(game, body.SourceGame);
        Assert.Equal("fixture-build", body.BuildId);
        Assert.Equal(0, body.CarrierId);
        Assert.Equal(0, body.SequenceId);
        Assert.Equal(Enumerable.Range(0, 111), body.JointIndices);
        var bodyFrame = Assert.Single(body.Frames);
        Assert.Equal(1d / 30, bodyFrame.DurationSeconds);
        Assert.Equal(1d, bodyFrame.TimelinePosition);
        Assert.Equal(new Vector3(.5f, 1, .75f), bodyFrame.Joints[7].Scale);
        Assert.Equal(new Vector3(4, -6, 8), bodyFrame.Joints[8].Translation);
        Assert.Equal(new Vector3(12, 0, 0), bodyFrame.Joints[24].Translation);

        Assert.Equal(2, result.Arms.Count);
        for (int side = 0; side < 2; side++)
        {
            var arm = Assert.Single(result.Arms[side]).Value;
            Assert.Equal(game, arm.SourceGame);
            Assert.Equal("fixture-build", arm.BuildId);
            Assert.Equal(side + 1, arm.CarrierId);
            int start = side == 0 ? 52 : 72;
            Assert.Equal(Enumerable.Range(start, 20), arm.JointIndices);
            var frame = Assert.Single(arm.Frames);
            Assert.Equal(1d / 15, frame.DurationSeconds);
            Assert.Equal(1d, frame.TimelinePosition);
            Assert.Equal(new Vector3(.5f, 1, .75f), frame.Joints[0].Scale);
            Assert.Equal(new Vector3(4, -6, 8), frame.Joints[2].Translation);
            Assert.Equal(new Vector3((start + 1) / 2f, 0, 0), frame.Joints[1].Translation);
            Assert.Equal(16384 / 32768f, frame.Joints[0].Rotation.W);
        }
    }

    [Theory]
    [InlineData(ObpSourceGame.Rac2, "GC")]
    [InlineData(ObpSourceGame.Rac3, "UYA")]
    public void RejectsMalformedDirectoriesAndLayerMismatchWithGameSpecificDiagnostics(ObpSourceGame game, string label)
    {
        var fixture = Fixture.Create();
        U32(fixture.Index, 0x100, unchecked((uint)-1));
        Assert.Equal($"{label} player sequence escapes its asset section.",
            Assert.Throws<InvalidDataException>(() => fixture.Decode(game, label)).Message);

        fixture = Fixture.Create();
        fixture.Arms[0][0x64] = 51; // Valid joint, but not the original arm chain.
        Assert.Equal($"{label} arm channels disagree with the native layer list.",
            Assert.Throws<InvalidDataException>(() => fixture.Decode(game, label)).Message);

        fixture = Fixture.Create();
        fixture.Index = new byte[0x100]; // First sequence-directory read is out of bounds.
        Assert.Equal($"{label} skeletal directory exceeds its source.",
            Assert.Throws<InvalidDataException>(() => fixture.Decode(game, label)).Message);

        fixture = Fixture.Create();
        fixture.Arms[0][0x180 + 0x10 + 20 * 8 + 6] = 95; // Sparse scale targets a non-arm joint.
        Assert.Equal($"{label} scale escapes its layer.",
            Assert.Throws<InvalidDataException>(() => fixture.Decode(game, label)).Message);

        fixture = Fixture.Create();
        fixture.Arms[0][0x180 + 0x10 + 21 * 8 + 6] = 95; // Sparse translation outside arm.
        Assert.Equal($"{label} translation escapes its layer.",
            Assert.Throws<InvalidDataException>(() => fixture.Decode(game, label)).Message);
    }

    [Theory]
    [InlineData(ObpSourceGame.Rac2, "GC", 12)]
    [InlineData(ObpSourceGame.Rac2, "GC", 13)]
    [InlineData(ObpSourceGame.Rac3, "UYA", 12)]
    [InlineData(ObpSourceGame.Rac3, "UYA", 13)]
    public void MissingArmAttachmentReportsInvalidNativeData(ObpSourceGame game, string label, int missing)
    {
        var fixture = Fixture.Create();
        U32(fixture.Skeleton, 0x2400, (uint)missing);
        Assert.Equal($"{label} arm attachment {missing} is missing.",
            Assert.Throws<InvalidDataException>(() => fixture.Decode(game, label)).Message);
    }

    [Theory]
    [InlineData(ObpSourceGame.Rac2, "GC")]
    [InlineData(ObpSourceGame.Rac3, "UYA")]
    public void RejectsRigWithUnsupportedJointCount(ObpSourceGame game, string label)
    {
        var fixture = Fixture.Create();
        fixture.Skeleton[8] = 110;
        Assert.Equal($"{label} player rig differs from the supported authority.",
            Assert.Throws<InvalidDataException>(() => fixture.Decode(game, label)).Message);
    }

    [Fact]
    public void MultiFrameClipWithoutTransitionRateRetainsNativeRejection()
    {
        var fixture = Fixture.Create();
        var arm = fixture.Arms[0];
        arm[0x110] = 2;
        U32(arm, 0x120, 0x300);
        Array.Copy(arm, 0x180, arm, 0x300, 0xc0);
        F32(arm, 0x180, 0);
        F32(arm, 0x300, 0);
        Assert.Equal("Unsupported stopped GC player clip.",
            Assert.Throws<InvalidDataException>(() => fixture.Decode(ObpSourceGame.Rac2, "GC")).Message);
    }

    private sealed class Fixture
    {
        public byte[] Index { get; set; } = new byte[0x600];
        public byte[] Assets { get; } = new byte[0x800];
        public byte[] Skeleton { get; } = new byte[0x3000];
        public byte[][] Arms { get; } = [new byte[0x500], new byte[0x500]];

        public Ps2RatchetSkeletalDecoder.Decoded Decode(ObpSourceGame game, string label) =>
            Ps2RatchetSkeletalDecoder.Read(Index, Assets, [0x800], 0x100,
                id => id == 0 ? Skeleton : Arms[id - 1], game, "fixture-build", label);

        public static Fixture Create()
        {
            var result = new Fixture();
            byte[] b = result.Skeleton;
            b[8] = 111;
            U32(b, 0x14, 0x80);
            U32(b, 0x18, 0x1c40);
            U32(b, 0x1c, 0x2400);
            F32(b, 0x24, 2048); // Native coordinate scale of 2.
            for (int j = 0; j < 111; j++)
            {
                int bind = 0x80 + j * 64, common = 0x1c40 + j * 16;
                F32(b, bind, 1); F32(b, bind + 20, 1); F32(b, bind + 40, 1);
                F32(b, bind + 48, 1); // Scaled inverse-bind translation.
                F32(b, common, j / 4f);
                U16(b, common + 12, 0); // Root self-parent; all others use root.
            }
            U32(b, 0x2400, 31);
            int pointer = 0x2480;
            for (int i = 0; i < 31; i++)
            {
                U32(b, 0x2404 + i * 4, (uint)pointer);
                U16(b, pointer, 1);
                int start = i == 12 ? 52 : i == 13 ? 72 : -1;
                b[pointer + 2] = (byte)(start < 0 ? 0 : 20);
                b[pointer + 4] = 56; // Chain target.
                if (start >= 0) for (int j = 0; j < 20; j++) b[pointer + 5 + j] = (byte)(start + j);
                pointer += start < 0 ? 5 : 25;
            }
            U32(result.Index, 0x100, 0x200);
            Sequence(result.Assets, 0x200, 0x240, 111, 7, 8, .5f, 1);
            for (int side = 0; side < 2; side++)
            {
                var arm = result.Arms[side];
                arm[8] = 20; arm[0xc] = 1;
                U32(arm, 0x1c, 0x60); U32(arm, 0x48, 0x100);
                for (int j = 0; j < 20; j++) arm[0x64 + j] = (byte)((side == 0 ? 52 : 72) + j);
                arm[0x64 + 20] = 0xff;
                Sequence(arm, 0x100, 0x180, 20, (side == 0 ? 52 : 72),
                    (side == 0 ? 54 : 74), 0, .25f);
            }
            return result;
        }
    }

    private static void Sequence(byte[] bytes, int start, int frameAt, int joints,
        int scaleJoint, int translationJoint, float constantRate, float frameRate)
    {
        bytes[start + 0x10] = 1;
        U32(bytes, start + 0x1c, (uint)(joints == 111 ? frameAt - start : frameAt));
        F32(bytes, start + 0x18, constantRate);
        F32(bytes, frameAt, frameRate);
        U16(bytes, frameAt + 4, 16);
        U16(bytes, frameAt + 8, (ushort)(joints * 8));
        U16(bytes, frameAt + 0xa, 1);
        U16(bytes, frameAt + 0xe, 1);
        int channels = frameAt + 0x10;
        for (int j = 0; j < joints; j++) U16(bytes, channels + j * 8 + 6, 16384);
        int aux = channels + joints * 8;
        U16(bytes, aux, 2048); U16(bytes, aux + 2, 4096); U16(bytes, aux + 4, 3072);
        U16(bytes, aux + 6, (ushort)scaleJoint);
        U16(bytes, aux + 8, 2); U16(bytes, aux + 10, unchecked((ushort)-3)); U16(bytes, aux + 12, 4);
        U16(bytes, aux + 14, (ushort)translationJoint);
    }

    private static void U16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
    private static void U32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void F32(byte[] bytes, int offset, float value) =>
        U32(bytes, offset, BitConverter.SingleToUInt32Bits(value));
}
