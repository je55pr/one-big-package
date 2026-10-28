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
            int m=0x48+j*64;
            for (int i=0;i<3;i++)
                BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(m+i*20),1f);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(m+48),j==0?-5f:-6f);
            int tr=0xc8+j*16;
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(tr),j==0?5f:1f);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(tr+12),j==0?0u:0x70000000u);
        }
        return buf;
    }

    private static GcUyaMoby.Mesh Mesh() =>
        new([6d,0d,0d],[0f,0f],[],[],1024f,true,true)
        {
            SkinLocalPositions=[6d,0d,0d],
            VertexJoints=[1,0,0],
            VertexWeights=[1,0,0],
        };

    private static GcUyaMoby.MobyFrame Frame(
        GcUyaMoby.MobyScaleOverride[]? scales=null,
        GcUyaMoby.MobyTranslationOverride[]? trans=null) =>
        new(1f,[Quaternion.Identity,Quaternion.Identity])
        {
            ExtraChannelsResolved=true,
            ScaleOverrides=scales??[],
            TranslationOverrides=trans??[],
        };

    [Fact]
    public void InverseBindAndForwardPoseCancelAtSyntheticRest()
    {
        var source=Skeleton();
        Assert.Equal([6d,0d,0d],GcUyaMobyNativePose.Pose(source,Mesh(),Frame()));
        Assert.Throws<InvalidDataException>(()=>GcUyaMobyNativePose.Pose(source,Mesh(),
            Frame() with {ExtraChannelsResolved=false}));
    }

    [Fact]
    public void InheritedAndPostScalesHaveDifferentEffectsOnChildren()
    {
        var source=Skeleton();
        var m=Mesh();
        // Parent root scales its child offset from 1 to 2.
        Assert.Equal([7d,0d,0d],GcUyaMobyNativePose.Pose(source,m,Frame(
            [new GcUyaMoby.MobyScaleOverride(0,2,1,1,true)])));
        // Post-chain root scale leaves its child's global transform intact.
        Assert.Equal([6d,0d,0d],GcUyaMobyNativePose.Pose(source,m,Frame(
            [new GcUyaMoby.MobyScaleOverride(0,2,1,1,false)])));
        // Overriding the child's local translation moves it by +1.
        Assert.Equal([7d,0d,0d],GcUyaMobyNativePose.Pose(source,m,Frame(
            trans:[new GcUyaMoby.MobyTranslationOverride(1,2f/1024f,0,0)])));
        // Child's own post-scale acts in joint space, before its inverse bind.
        Assert.Equal([6d,0d,0d],GcUyaMobyNativePose.Pose(source,m,Frame(
            [new GcUyaMoby.MobyScaleOverride(1,2,1,1,false)])));
    }

    [Fact]
    public void InvalidNativeHierarchyFailsClosed()
    {
        var source=Skeleton();
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0xc8+16+12),0x70000040u);
        Assert.Throws<InvalidDataException>(()=>GcUyaMobyNativePose.Pose(source,Mesh(),Frame()));
    }

    [SkippableFact]
    public void AllOriginalOozlaCreatureFramesCanBePosedWithoutCorruptData()
    {
        var iso=Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso),"OBP_GC_ISO missing");
        using var reader=new FileRandomAccessReader(iso!);
        var fs=Iso9660Filesystem.Open(reader);
        var wad=fs.OpenFile("/G/LEVEL1.WAD")!;
        var header=Rac2LevelWad.ReadHeader(wad);
        var core=Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad,header,0));
        var table=core.Header.MobyClasses;
        var classes=Rac2MobyClasses.Read(core,level:1);
        int checkedFrames=0;
        foreach (int id in new[]{2466,2859})
        {
            int at=Enumerable.Range(0,table.Count)
                .Select(i=>table.Offset+i*GcUyaMoby.ClassEntrySize)
                .Single(p=>BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(p+4))==id);
            int begin=BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int end=core.SectionBoundaries.FirstOrDefault(x=>x>begin,core.Assets.Length);
            byte[] source=core.Assets.AsSpan(begin,end-begin).ToArray();
            var moby=classes[id];
            foreach (var frame in moby.Sequences.SelectMany(s=>s.Frames))
            {
                var result=GcUyaMobyNativePose.Pose(source,moby.Mesh,frame);
                Assert.Equal(moby.Mesh.Positions.Length,result.Length);
                Assert.All(result,coordinate=>Assert.True(double.IsFinite(coordinate)));
                checkedFrames++;
            }
        }
        Assert.Equal(833,checkedFrames);
    }
}
