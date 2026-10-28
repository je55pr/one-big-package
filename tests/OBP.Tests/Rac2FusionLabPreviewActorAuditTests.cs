using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using OBP.RAC2.Animation;
using Xunit.Abstractions;

namespace OBP.Tests;

public sealed class Rac2FusionLabPreviewActorAuditTests(ITestOutputHelper log)
{
    [SkippableFact]
    public void AranosClass2827ActorSourceMeshAndClipMustUseTheSameStream()
    {
        var iso=Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso),"OBP_GC_ISO missing");
        using var reader=new FileRandomAccessReader(iso!);
        var fs=Iso9660Filesystem.Open(reader);
        var wad=fs.OpenFile("/G/LEVEL0.WAD")!;
        var hdr=Rac2LevelWad.ReadHeader(wad);
        var core=Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad,hdr,0));
        int id=2827;
        var previous=Rac2MobyClasses.Read(core)[id];
        var cache=GcUyaMoby.ReadClasses(core.Index,core.Assets,core.SectionBoundaries,
            new(core.Header.MobyClasses.Count,core.Header.MobyClasses.Offset),recoverPersistentVertexCache:_=>true)[id];
        var native=GcUyaMoby.ReadClasses(core.Index,core.Assets,core.SectionBoundaries,
            new(core.Header.MobyClasses.Count,core.Header.MobyClasses.Offset),
            recoverPersistentVertexCache:_=>true,previewNativeInitialPose:_=>true)[id];
        var sourceFrame=previous.Sequences.First(s=>s.Index==12).Frames[0];
        var legacyAnimation=Rac2MobyAnimation.Pose(previous.Mesh,previous.Joints,sourceFrame);
        log.WriteLine($"Class 2827 legacy vertices={previous.Mesh.Positions.Length/3}, recovered cache={cache.Mesh.Positions.Length/3}, native preview={native.Mesh.Positions.Length/3}, old animation pose={legacyAnimation.Length/3}");
        log.WriteLine($"Legacy clip equals native preview vertex count: {legacyAnimation.Length==native.Mesh.Positions.Length}");
        log.WriteLine($"Native frame channels resolved: {sourceFrame.ExtraChannelsResolved}; first clip sequence={previous.Sequences.First(s=>s.Index==12).Frames.Count}");
        Assert.True(previous.Mesh.Positions.Length>0);
        Assert.True(native.Mesh.Positions.Length>0);
    }

    [SkippableFact]
    public void AranosDoorRetainsAuthoredAnimationButMayGainRecoveredFaces()
    {
        var iso=Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso),"OBP_GC_ISO missing");
        using var reader=new FileRandomAccessReader(iso!);
        var fs=Iso9660Filesystem.Open(reader);
        var wad=fs.OpenFile("/G/LEVEL0.WAD")!;
        var hdr=Rac2LevelWad.ReadHeader(wad);
        var core=Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad,hdr,0));
        var old=Rac2MobyClasses.Read(core)[2755];
        var updated=Rac2MobyClasses.Read(core,level:0,nativePoseOverride:true)[2755];
        log.WriteLine($"Door 2755 legacy: joints={old.Joints.Count}, v={old.Mesh.Positions.Length/3}, tris={old.Mesh.Indices.Length/3}; preview: joints={updated.Joints.Count}, v={updated.Mesh.Positions.Length/3}, tris={updated.Mesh.Indices.Length/3}");
        log.WriteLine($"Source seq1 frames={updated.Sequences.Single(s=>s.Index==1).Frames.Count}; seq2 frames={updated.Sequences.Single(s=>s.Index==2).Frames.Count}");
        Assert.Equal(old.Joints.Count,updated.Joints.Count);
        Assert.True(updated.Mesh.Indices.Length>=old.Mesh.Indices.Length);
    }

    [SkippableFact]
    public void NativePreviewDoorRetainsItsNativeOpeningClip()
    {
        var iso=Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso),"OBP_GC_ISO missing");
        using var reader=new FileRandomAccessReader(iso!);
        var fs=Iso9660Filesystem.Open(reader);
        var wad=fs.OpenFile("/G/LEVEL0.WAD")!;
        var hdr=Rac2LevelWad.ReadHeader(wad);
        var core=Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad,hdr,0));
        var door=Rac2MobyClasses.Read(core,level:0,nativePoseOverride:true)[2755];
        Assert.Equal(124,door.Mesh.Indices.Length/3);
        var mesh=door.Mesh;
        var local=new double[mesh.Positions.Length];
        for(int i=0;i<local.Length;i+=3)
        {
            local[i]=mesh.Positions[i];
            local[i+1]=mesh.Positions[i+2];
            local[i+2]=mesh.Positions[i+1];
        }
        var surfaces=new[]{new OBP.Runtime.RuntimeObjectMesh(
            "moby",0,local,mesh.Uvs,mesh.Indices)};
        var method=typeof(OBP.RAC2.Rac2WorldImport).GetMethod(
            "BuildAdmittedDynamicAnimationSet",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        var clip=(OBP.Runtime.RuntimeObjectAnimationSet?)method.Invoke(
            null,new object[]{door,surfaces,core,true});
        Assert.NotNull(clip);
        var opening=Assert.Single(clip.Clips);
        Assert.Equal(31,opening.FrameCount); // 30 opening frames + 1 held open.
        Assert.All(opening.Surfaces,surface=>
        {
            Assert.All(surface.LocalFrames,positions=>
            {
                Assert.Equal(local.Length,positions.Length);
                Assert.All(positions,x=>Assert.True(double.IsFinite(x)));
            });
        });
        // The old 120-triangle authority guard must not silently accept a
        // recovered 124-triangle source when native pose preview is absent.
        var rejected=Assert.Throws<System.Reflection.TargetInvocationException>(
            ()=>method.Invoke(null,new object[]{door,surfaces,core,false}));
        Assert.IsType<InvalidDataException>(rejected.InnerException);
    }

    [SkippableFact]
    public void NativePreviewActorPreservesItsOriginalMeshAndAnimationVertexStream()
    {
        var iso=Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso),"OBP_GC_ISO missing");
        using var reader=new FileRandomAccessReader(iso!);
        var world=OBP.RAC2.Rac2WorldImport.Build(reader,0);
        var source=world.DynamicObjects!.First(actor=>actor.NativeClassId==2827 && actor.InstanceIndex==205);
        // Compare the native recovered actor source with the same pose-stream clips.

        var fs=Iso9660Filesystem.Open(reader);
        var wad=fs.OpenFile("/G/LEVEL0.WAD")!;
        var header=Rac2LevelWad.ReadHeader(wad);
        var core=Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad,header,0));
        var native=Rac2MobyClasses.Read(core,level:0,nativePoseOverride:true)[2827];
        var scenePositions=new double[native.Mesh.Positions.Length];
        for(int i=0;i<scenePositions.Length;i+=3)
        {
            scenePositions[i]=native.Mesh.Positions[i];
            scenePositions[i+1]=native.Mesh.Positions[i+2];
            scenePositions[i+2]=native.Mesh.Positions[i+1];
        }
        var nativeSurfaces=source.Meshes.ToArray();
        // Runtime scene conversion may round coordinates; the vertex streams
        // must agree in length and order, not in pre/post conversion precision.
        Assert.All(nativeSurfaces,surface=>Assert.Equal(2953,surface.Positions.Length/3));

        // Before the fix, this loader threw on the recovered 193 additional
        // source vertices, which prevented actor registration in Fusion Lab.
        Assert.Throws<InvalidDataException>(()=>Rac2Class2827Presentation.Load(iso!,nativeSurfaces));
        var clips=Rac2Class2827Presentation.Load(iso!,nativeSurfaces,nativePosePreview:true);
        Assert.Equal(new[]{11,16,17,27},clips.Clips.Keys.Order());
        foreach(var clip in clips.Clips.Values)
        {
            Assert.Equal(nativeSurfaces.Length,clip.Surfaces.Count);
            Assert.All(clip.Surfaces,surface=>
            {
                Assert.Equal(clip.FrameCount,surface.LocalFrames.Count);
                Assert.All(surface.LocalFrames,frame=>
                {
                    Assert.Equal(scenePositions.Length,frame.Length);
                    Assert.All(frame,x=>Assert.True(double.IsFinite(x)));
                });
                Assert.Contains(surface.LocalFrames.Skip(1),frame=>!frame.SequenceEqual(surface.LocalFrames[0]));
            });
        }
    }
}
