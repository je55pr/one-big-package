using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC2.Level;

namespace OBP.Tests;

public sealed class Rac2MobyNativePoseTests
{
    private static byte[] Skeleton()
    {
        byte[] buf = new byte[0x180];
        buf[8] = 2;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0x14), 0x48);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0x18), 0xc8);
        for (int j = 0; j < 2; j++)
        {
            int m = 0x48 + j * 64;
            for (int i = 0; i < 3; i++)
                BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(m + i * 20), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(m + 48), j == 0 ? -5f : -6f);
            int tr = 0xc8 + j * 16;
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(tr), j == 0 ? 5f : 1f);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(tr + 12), j == 0 ? 0u : 0x70000000u);
        }
        return buf;
    }

    private static GcUyaMoby.Mesh Mesh() =>
        new([6d, 0d, 0d], [0f, 0f], [], [], 1024f, true, true)
        {
            SkinLocalPositions = [6d, 0d, 0d],
            VertexJoints = [1, 0, 0],
            VertexWeights = [1, 0, 0],
        };

    private static GcUyaMoby.MobyFrame Frame(
        GcUyaMoby.MobyScaleOverride[]? scales = null,
        GcUyaMoby.MobyTranslationOverride[]? trans = null) =>
        new(1f, [Quaternion.Identity, Quaternion.Identity])
        {
            ExtraChannelsResolved = true,
            ScaleOverrides = scales ?? [],
            TranslationOverrides = trans ?? [],
        };

    [Fact]
    public void InverseBindAndForwardPoseCancelAtSyntheticRest()
    {
        var source = Skeleton();
        Assert.Equal([6d, 0d, 0d], Ps2MobyNativePose.Pose(source, Mesh(), Frame()));
        Assert.Throws<InvalidDataException>(() => Ps2MobyNativePose.Pose(source, Mesh(),
            Frame() with { ExtraChannelsResolved = false }));
    }

    [Fact]
    public void InheritedAndPostScalesHaveDifferentEffectsOnChildren()
    {
        var source = Skeleton();
        var m = Mesh();
        // Parent root scales its child offset from 1 to 2.
        Assert.Equal([7d, 0d, 0d], Ps2MobyNativePose.Pose(source, m, Frame(
            [new GcUyaMoby.MobyScaleOverride(0, 2, 1, 1, true)])));
        // Post-chain root scale leaves its child's global transform intact.
        Assert.Equal([6d, 0d, 0d], Ps2MobyNativePose.Pose(source, m, Frame(
            [new GcUyaMoby.MobyScaleOverride(0, 2, 1, 1, false)])));
        // Overriding the child's local translation moves it by +1.
        Assert.Equal([7d, 0d, 0d], Ps2MobyNativePose.Pose(source, m, Frame(
            trans: [new GcUyaMoby.MobyTranslationOverride(1, 2f / 1024f, 0, 0)])));
        // Child's own post-scale acts in joint space, before its inverse bind.
        Assert.Equal([6d, 0d, 0d], Ps2MobyNativePose.Pose(source, m, Frame(
            [new GcUyaMoby.MobyScaleOverride(1, 2, 1, 1, false)])));
    }

    [Fact]
    public void NonUniformScaleKeepsSkinnedNormalsPerpendicularToSurfaceTangents()
    {
        var source = Skeleton();
        const double rootHalf = 0.7071067811865476;
        var mesh = Mesh() with { Normals = [(float)rootHalf, (float)rootHalf, 0f] };
        var frame = Frame([new GcUyaMoby.MobyScaleOverride(0, 2, 1, 1, true)]);
        var posed = Ps2MobyNativePose.Pose(source, mesh, frame);
        var normal = Ps2MobyNativePose.PoseNormals(source, mesh, frame, posed);
        var displaced = Ps2MobyNativePose.Pose(source, mesh with
        {
            SkinLocalPositions = [6d + rootHalf, -rootHalf, 0d]
        }, frame);

        double dot = normal[0] * (displaced[0] - posed[0]) +
            normal[1] * (displaced[1] - posed[1]) +
            normal[2] * (displaced[2] - posed[2]);
        Assert.InRange(Math.Abs(dot), 0, 1e-5);
        Assert.InRange(Math.Abs(normal[0] / normal[1] - 0.5f), 0, 1e-5);
    }

    [Fact]
    public void InvalidNativeHierarchyFailsClosed()
    {
        var source = Skeleton();
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0xc8 + 16 + 12), 0x70000040u);
        Assert.Throws<InvalidDataException>(() => Ps2MobyNativePose.Pose(source, Mesh(), Frame()));
    }

    // One joint, one scale and one translation in a regular frame. Offsets
    // and slot IDs are explicit so malformed near-misses need no retail data.
    private static byte[] FrameClass()
    {
        var source = new byte[0xb0];
        source[8] = 1;
        source[0x0c] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x48), 0x50);
        source[0x60] = 1; // Sequence frame count.
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x6c), 0x80);
        BinaryPrimitives.WriteSingleLittleEndian(source.AsSpan(0x80), 0.5f);
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(0x86), 2); // Payload qwords.
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(0x88), 8); // Quaternion bytes.
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(0x8a), 1); // Scale count.
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(0x8c), 16); // Translation offset.
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(0x8e), 1); // Translation count.
        BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(0x96), 32767);
        for (int i = 0; i < 3; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(0x98 + i * 2), 4096);
        source[0x9f] = 0x80; // Inherited scale, joint 0.
        BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(0xa0), -1024);
        return source;
    }

    private static GcUyaMoby.MobyFrame ReadFrame(byte[] source) =>
        Assert.Single(Assert.Single(GcUyaMoby.ReadSequences(source, 1)).Frames);

    [Fact]
    public void RegularFrameChannelsDecodeValidatedValues()
    {
        var frame = ReadFrame(FrameClass());
        Assert.True(frame.ExtraChannelsResolved);
        Assert.Equal(new GcUyaMoby.MobyScaleOverride(0, 1, 1, 1, true), Assert.Single(frame.ScaleOverrides));
        Assert.Equal(new GcUyaMoby.MobyTranslationOverride(0, -1, 0, 0), Assert.Single(frame.TranslationOverrides));
    }

    [Theory]
    [InlineData(0x86, 1)] // Qwords off by one in either direction.
    [InlineData(0x86, 3)]
    [InlineData(0x88, 0)]
    [InlineData(0x88, 16)]
    [InlineData(0x8a, 2)]
    [InlineData(0x8c, 8)]
    [InlineData(0x8c, 24)]
    [InlineData(0x8e, 3)]
    public void InvalidFrameHeaderLeavesChannelsUnresolved(int offset, ushort value)
    {
        var source = FrameClass();
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(offset), value);
        var frame = ReadFrame(source);
        Assert.False(frame.ExtraChannelsResolved);
        Assert.Empty(frame.ScaleOverrides);
        Assert.Empty(frame.TranslationOverrides);
    }

    [Theory]
    [InlineData(0x8f)] // Truncated header, no complete quaternion.
    [InlineData(0x97)] // Truncated quaternion.
    public void TruncatedFrameCannotProduceAFrame(int length)
    {
        Assert.Empty(Assert.Single(GcUyaMoby.ReadSequences(FrameClass()[..length], 1)).Frames);
    }

    [Theory]
    [InlineData(0xa7)] // Truncated translation.
    [InlineData(0xaf)] // Truncated declared qword payload.
    public void TruncatedFramePayloadLeavesChannelsUnresolved(int length)
    {
        var frame = ReadFrame(FrameClass()[..length]);
        Assert.False(frame.ExtraChannelsResolved);
        Assert.Empty(frame.ScaleOverrides);
        Assert.Empty(frame.TranslationOverrides);
    }

    [Theory]
    [InlineData(0x9f, 0x01)] // Reserved scale bit, without inherited bit.
    [InlineData(0x9f, 0x81)] // Reserved scale bit, with inherited bit.
    [InlineData(0xa7, 0x01)] // Nonzero translation padding.
    [InlineData(0x9e, 1)] // Scale joint outside the one-joint skeleton.
    [InlineData(0xa6, 1)] // Translation joint outside the skeleton.
    public void InvalidChannelTagsLeaveAllChannelsUnresolved(int offset, byte value)
    {
        var source = FrameClass();
        source[offset] = value;
        var frame = ReadFrame(source);
        Assert.False(frame.ExtraChannelsResolved);
        Assert.Empty(frame.ScaleOverrides);
        Assert.Empty(frame.TranslationOverrides);
    }

    [Fact]
    public void SpecialFrameDoesNotAdmitRegularChannels()
    {
        var source = FrameClass();
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0x6c), 0x10000080);
        var frame = ReadFrame(source);
        Assert.False(frame.ExtraChannelsResolved);
        Assert.Empty(frame.ScaleOverrides);
        Assert.Empty(frame.TranslationOverrides);
    }

    private static byte[] PacketClass()
    {
        var source = new byte[0x140];
        BinaryPrimitives.WriteInt32LittleEndian(source, 0x50);
        source[4] = 2;
        BinaryPrimitives.WriteSingleLittleEndian(source.AsSpan(0x24), 1024);
        // Two UNPACKs: two ST pairs, then an empty strip index stream.
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0x80), 0x65020000);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0x8c), 0x62080000);
        for (int packet = 0; packet < 2; packet++)
        {
            int entry = 0x50 + packet * 0x10, vh = 0xa0 + packet * 0x60;
            BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(entry), 0x80);
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(entry + 4), 2);
            BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(entry + 8), vh);
            source[entry + 12] = 4; // Header 2 qwords + vertex 1 + epilogue 1.
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 6), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 8), (ushort)packet);
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 12), 0x20);
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 16), 17 << 7);
            for (int axis = 0; axis < 3; axis++)
                BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(vh + 0x2a + axis * 2),
                    (short)(4 + packet * 3 + axis));
            // The single vertex's slot is the final pipelined halfword.
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 0x3e),
                (ushort)(packet == 0 ? 17 : 31));
        }
        return source;
    }

    [Fact]
    public void UnresolvedRigidMatrixSlotCannotMasqueradeAsAValidJointBinding()
    {
        // A rigid packet has no blended skinning, but its matrix-slot lookup
        // still determines the source joint. A missing slot falls back to
        // joint zero for legacy rendering and must not admit native posing.
        var source = new byte[0x600];
        PacketClass().CopyTo(source, 0);
        source[8] = 8;
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x14), 0x200);
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x18), 0x400);
        foreach (int vertex in new[] { 0xc0, 0x120 })
        {
            source[vertex + 2] = 4; // Resolve slot 1 (address 4).
            source[vertex + 3] = 4; // Install the rigid joint before lookup.
        }

        var resolved = GcUyaMoby.ReadClass(source, recoverPersistentVertexCache: true);
        Assert.False(resolved.Skinned);
        Assert.True(resolved.SkinStateFullyResolved);
        Assert.True(Ps2MobyNativePose.HasRigidJointBinding(resolved, 8));

        source[0xc2] = 8; // Slot 2 was never written: decoder substitutes J0.
        var unresolved = GcUyaMoby.ReadClass(source, recoverPersistentVertexCache: true);
        Assert.False(unresolved.Skinned);
        Assert.False(unresolved.SkinStateFullyResolved);
        Assert.False(Ps2MobyNativePose.HasRigidJointBinding(unresolved, 8));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(7)]
    public void PersistentCacheRejectsInvalidEpilogueRecordCounts(int epilogueRecords)
    {
        var source = PacketClass();
        source[0x5c] = (byte)(3 + epilogueRecords);
        var error = Assert.Throws<InvalidDataException>(() =>
            GcUyaMoby.ReadClass(source, recoverPersistentVertexCache: true));
        Assert.Contains("epilogue", error.Message);
    }

    [Fact]
    public void PersistentCacheRejectsTruncatedEpilogue()
    {
        var source = PacketClass()[..0x13f];
        var error = Assert.Throws<InvalidDataException>(() =>
            GcUyaMoby.ReadClass(source, recoverPersistentVertexCache: true));
        Assert.Contains("epilogue", error.Message);
    }

    [Fact]
    public void PersistentDuplicateUsesThePriorPacketSlotAndRejectsAnUnknownSlot()
    {
        var source = PacketClass();
        var mesh = GcUyaMoby.ReadClass(source, recoverPersistentVertexCache: true);
        Assert.Equal(new double[] { 4, 5, 6, 7, 8, 9, 4, 5, 6 }, mesh.Positions);
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(0x110), 63 << 7);
        var error = Assert.Throws<InvalidDataException>(() =>
            GcUyaMoby.ReadClass(source, recoverPersistentVertexCache: true));
        Assert.Contains("duplicate VU1 vertex-cache slot", error.Message);
    }

    [Fact]
    public void ClassTableFallsBackToLegacyMeshWhenStrictCacheRecoveryRejectsTheClass()
    {
        var source = PacketClass();
        source[0x5c] = 3; // Invalid epilogue, before native pose admission.
        var assets = new byte[source.Length + 16];
        source.CopyTo(assets, 16);
        var index = new byte[GcUyaMoby.ClassEntrySize];
        BinaryPrimitives.WriteInt32LittleEndian(index, 16);
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(4), 2827);
        int previewCalls = 0;
        var classes = GcUyaMoby.ReadClasses(index, assets, [], new(1, 0),
            recoverPersistentVertexCache: _ => true,
            previewNativeInitialPose: id => { Assert.Equal(2827, id); previewCalls++; return true; });
        Assert.True(classes.TryGetValue(2827, out var recovered));
        Assert.Equal(GcUyaMoby.ReadClass(source).Positions, recovered.Mesh.Positions);
        Assert.False(recovered.Mesh.NativeInitialPoseApplied);
        Assert.Equal(0, previewCalls);
    }

    [Theory]
    [InlineData(1)] // Missing VIF stream: truncated VIF list pointer.
    [InlineData(2)] // Vertex table header outside source asset.
    [InlineData(3)] // No VIF unpack records.
    [InlineData(4)] // Declared vertex table is implausibly large.
    public void RecoveredPacketSkipFailsClosedAndRetainsLegacyClass(int invalidPacket)
    {
        var source = new byte[0x1c0];
        PacketClass().CopyTo(source, 0);
        // Third packet deliberately reuses slot 17 from the first packet.
        // A skipped second packet breaks cache continuity: retaining its old
        // slots would silently fabricate valid-looking source geometry.
        source[4] = 3;
        source.AsSpan(0x60, 16).CopyTo(source.AsSpan(0x70));
        source.AsSpan(0x100, 0x40).CopyTo(source.AsSpan(0x160));
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x78), 0x160);
        switch (invalidPacket)
        {
            case 1:
                BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x60), 0x400);
                break;
            case 2:
                BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x68), 0x400);
                break;
            case 3:
                BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(0x64), 0);
                break;
            case 4:
                BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(0x106), 4097);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidPacket));
        }

        var error = Assert.Throws<InvalidDataException>(() =>
            GcUyaMoby.ReadClass(source, recoverPersistentVertexCache: true));
        Assert.Contains("duplicate VU1 vertex-cache slot", error.Message);
        var legacy = GcUyaMoby.ReadClass(source);

        var assets = new byte[source.Length + 16];
        source.CopyTo(assets, 16);
        var index = new byte[GcUyaMoby.ClassEntrySize];
        BinaryPrimitives.WriteInt32LittleEndian(index, 16);
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(4), 2827);
        var classes = GcUyaMoby.ReadClasses(index, assets, [], new(1, 0),
            recoverPersistentVertexCache: _ => true,
            previewNativeInitialPose: _ => true);
        Assert.Equal(legacy.Positions, classes[2827].Mesh.Positions);
        Assert.Equal(legacy.Indices, classes[2827].Mesh.Indices);
    }

    [Fact]
    public void ClassTableDoesNotInvokePreviewPredicateWithoutStrictRecovery()
    {
        var source = PacketClass();
        var assets = new byte[source.Length + 16];
        source.CopyTo(assets, 16);
        var index = new byte[GcUyaMoby.ClassEntrySize];
        BinaryPrimitives.WriteInt32LittleEndian(index, 16);
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(4), 2827);
        int previewCalls = 0;
        var classes = GcUyaMoby.ReadClasses(index, assets, [], new(1, 0),
            recoverPersistentVertexCache: null, previewNativeInitialPose: _ => { previewCalls++; throw new InvalidDataException("Predicate failure"); });
        Assert.True(classes.ContainsKey(2827));
        Assert.False(classes[2827].Mesh.NativeInitialPoseApplied);
        Assert.Equal(0, previewCalls);
    }

    [Fact]
    public void ClassTableRethrowsPoseFailuresAfterAdmissionWithoutRecheckingThePredicate()
    {
        var source = new byte[0x200];
        PacketClass().CopyTo(source, 0);
        source[8] = 1;
        source[0x0c] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x14), 0x140);
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x18), 0x180);
        for (int axis = 0; axis < 3; axis++)
            BinaryPrimitives.WriteSingleLittleEndian(source.AsSpan(0x140 + axis * 20), 1);
        // The legacy joint reader accepts this self-parent, but native pose
        // evaluation must reject it once the complete frame/skin is admitted.
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0x18c), 0x70000000);
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x48), 0x1a0);
        source[0x1b0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x1bc), 0x1d0);
        FrameClass().AsSpan(0x80).CopyTo(source.AsSpan(0x1d0));
        for (int packet = 0; packet < 2; packet++)
        {
            int vh = 0xa0 + packet * 0x60;
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 6), 0);
            source[vh + 0x24] = 128;
            source[vh + 0x25] = 128;
            source[vh + 0x27] = 4;
        }
        var mesh = GcUyaMoby.ReadClass(source, recoverPersistentVertexCache: true);
        Assert.True(mesh.SkinningApplied);
        Assert.True(mesh.SkinStateFullyResolved);
        Assert.True(Assert.Single(Assert.Single(GcUyaMoby.ReadSequences(source, 1)).Frames).ExtraChannelsResolved);

        var assets = new byte[source.Length + 16];
        source.CopyTo(assets, 16);
        var index = new byte[GcUyaMoby.ClassEntrySize];
        BinaryPrimitives.WriteInt32LittleEndian(index, 16);
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(4), 2827);
        int previewCalls = 0;
        var error = Assert.Throws<InvalidDataException>(() =>
            GcUyaMoby.ReadClasses(index, assets, [], new(1, 0),
                recoverPersistentVertexCache: _ => true,
                previewNativeInitialPose: _ => ++previewCalls == 1));
        Assert.Contains("parent address", error.Message);
        Assert.Equal(1, previewCalls);
    }

    [SkippableFact]
    public void AllOriginalOozlaCreatureFramesCanBePosedWithoutCorruptData()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_GC_ISO missing");
        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile("/G/LEVEL1.WAD")!;
        var header = Rac2LevelWad.ReadHeader(wad);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var table = core.Header.MobyClasses;
        var classes = Rac2MobyClasses.Read(core, level: 1);
        int checkedFrames = 0;
        foreach (int id in new[] { 2466, 2859 })
        {
            int at = Enumerable.Range(0, table.Count)
                .Select(i => table.Offset + i * GcUyaMoby.ClassEntrySize)
                .Single(p => BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(p + 4)) == id);
            int begin = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int end = core.SectionBoundaries.FirstOrDefault(x => x > begin, core.Assets.Length);
            byte[] source = core.Assets.AsSpan(begin, end - begin).ToArray();
            var moby = classes[id];
            foreach (var frame in moby.Sequences.SelectMany(s => s.Frames))
            {
                var result = Ps2MobyNativePose.Pose(source, moby.Mesh, frame);
                Assert.Equal(moby.Mesh.Positions.Length, result.Length);
                Assert.All(result, coordinate => Assert.True(double.IsFinite(coordinate)));
                checkedFrames++;
            }
        }
        Assert.Equal(833, checkedFrames);
    }
}
