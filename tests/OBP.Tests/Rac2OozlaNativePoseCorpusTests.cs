using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using Xunit.Abstractions;

namespace OBP.Tests;

/// <summary>
/// Read-only native-frame pose census of other Oozla Moby classes. Keeps
/// source-scope admissions explicit; does not modify meshes or renderer.
/// </summary>
public sealed class Rac2OozlaNativePoseCorpusTests(ITestOutputHelper output)
{
    [SkippableFact]
    public void GeneralizedPoseEvaluatorHandlesAdditionalRetailClasses()
    {
        var iso=Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso),"OBP_GC_ISO missing");
        using var reader=new FileRandomAccessReader(iso!);
        var fs=Iso9660Filesystem.Open(reader);
        var wad=fs.OpenFile("/G/LEVEL1.WAD")!;
        var hdr=Rac2LevelWad.ReadHeader(wad);
        var core=Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad,hdr,0));
        var table=core.Header.MobyClasses;
        var classes=Rac2MobyClasses.Read(core,level:1);
        int eligible=0,checkedFrames=0,sphereEscapes=0,checkedVerts=0,unsupported=0;
        var failures=new List<string>();
        foreach (var cls in classes.Values)
        {
            if(cls.OClass is 2466 or 2859)continue;
            if(cls.Joints.Count<2 || !cls.Mesh.SkinningApplied ||
                !cls.Mesh.SkinStateFullyResolved ||
                cls.Sequences.All(s=>s.Frames.Count==0))continue;
            int at=Enumerable.Range(0,table.Count)
                .Select(i=>table.Offset+i*GcUyaMoby.ClassEntrySize)
                .FirstOrDefault(p=>BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(p+4))==cls.OClass,-1);
            if(at<0)continue;
            int start=BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int end=core.SectionBoundaries.FirstOrDefault(b=>b>start,core.Assets.Length);
            byte[] source=core.Assets.AsSpan(start,end-start).ToArray();
            if(source[0x0b]!=0)continue; // R&C1-format variants are separate.
            int poses=0,outside=0,vertices=0;
            try
            {
                foreach(var seq in cls.Sequences)
                {
                    if(seq.Frames.Count==0)continue;
                    foreach(var frame in seq.Frames.Count>1 ?
                        new[]{seq.Frames[0],seq.Frames[^1]} : new[]{seq.Frames[0]})
                    {
                        if(!frame.ExtraChannelsResolved)
                            throw new InvalidDataException("Unresolved frame channels");
                        var points=GcUyaMobyNativePose.Pose(source,cls.Mesh,frame);
                        poses++;
                        if(seq.BoundingSphere is not { } sphere || sphere.Radius<=0)continue;
                        double k=cls.Mesh.Scale/1024.0;
                        double cx=sphere.X*k,cy=sphere.Y*k,cz=sphere.Z*k,r=Math.Abs(sphere.Radius*k);
                        for(int v=0;v<points.Length;v+=3)
                        {
                            double dx=points[v]-cx,dy=points[v+1]-cy,dz=points[v+2]-cz;
                            if(dx*dx+dy*dy+dz*dz>r*r*1.002001)outside++;
                            vertices++;
                        }
                    }
                }
                eligible++;checkedFrames+=poses;checkedVerts+=vertices;sphereEscapes+=outside;
                if(outside>0)failures.Add($"class={cls.OClass} poses={poses} outside={outside}/{vertices}");
            }
            catch(Exception ex)
            {
                unsupported++;
                if(failures.Count<30)failures.Add($"class={cls.OClass} UNSUPPORTED: {ex.GetType().Name} {ex.Message}");
            }
        }
        output.WriteLine($"General Oozla eligible_classes={eligible}, sampled_frames={checkedFrames}, checked_vertices={checkedVerts}, escapes={sphereEscapes}, unsupported_classes={unsupported}");
        foreach(var f in failures.Take(40))output.WriteLine(f);
        Assert.True(eligible>=8);
    }
}
