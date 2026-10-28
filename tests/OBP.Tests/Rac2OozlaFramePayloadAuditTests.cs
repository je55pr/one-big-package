using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using Xunit.Abstractions;

namespace OBP.Tests;

/// <summary>
/// Read-only original-frame-header and scale/translation payload census for
/// the two Moby classes in Jess's saved Oozla view. Does not select a pose.
/// </summary>
public sealed class Rac2OozlaFramePayloadAuditTests(ITestOutputHelper output)
{
    private static int I32(byte[] b,int p)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(p));
    private static ushort U16(byte[] b,int p)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p));
    private static short S16(byte[] b,int p)=>BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(p));

    [SkippableFact]
    public void ExactNativeFrameExtraChannelsCensus()
    {
        string? iso=Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso),"OBP_GC_ISO is absent");
        using var reader=new FileRandomAccessReader(iso!);
        var fs=Iso9660Filesystem.Open(reader);
        var wad=fs.OpenFile("/G/LEVEL1.WAD")!;
        var hdr=Rac2LevelWad.ReadHeader(wad);
        var core=Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad,hdr,0));
        var table=core.Header.MobyClasses;
        var decodedClasses=Rac2MobyClasses.Read(core,level:1);
        foreach(int id in new[]{2466,2859})
        {
            int at=Enumerable.Range(0,table.Count)
                .Select(i=>table.Offset+i*GcUyaMoby.ClassEntrySize)
                .Single(p=>I32(core.Index,p+4)==id);
            int begin=I32(core.Index,at);
            int end=core.SectionBoundaries.FirstOrDefault(x=>x>begin,core.Assets.Length);
            byte[] source=core.Assets.AsSpan(begin,end-begin).ToArray();
            int joints=source[8],sequences=source[0x0c];
            int totalFrames=0,framesWithScales=0,framesWithTranslations=0,scaleRecords=0,
                translationRecords=0,nonUnitScales=0,nonzeroTranslationOverrides=0,
                hierarchyScales=0,postScales=0;
            var seqCounts=new List<string>();
            for(int s=0;s<sequences;s++)
            {
                int seq=I32(source,0x48+s*4);
                if(seq==0)continue;
                Assert.InRange(seq,0,source.Length-0x1c);
                int frames=source[seq+0x10],seqS=0,seqT=0,seqScales=0,seqTrans=0;
                var decodedSequence=decodedClasses[id].Sequences.Single(q=>q.Index==s);
                Assert.Equal(source[seq+0x11],decodedSequence.LoopSound);
                Assert.Equal(source[seq+0x12],decodedSequence.TriggerCount);
                Assert.Equal(I32(source,seq+0x14),decodedSequence.TriggerDataOffset);
                Assert.Equal(BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(seq+0x18)),
                    decodedSequence.RateOverride);
                for(int f=0;f<frames;f++)
                {
                    uint raw=(uint)I32(source,seq+0x1c+f*4);
                    Assert.Equal(0u,raw&0xf0000000u);
                    int atFrame=(int)(raw&0x0fffffffu);
                    Assert.InRange(atFrame,0,source.Length-16);
                    int qwc=U16(source,atFrame+6);
                    int quatBytes=U16(source,atFrame+8);
                    int scales=U16(source,atFrame+10);
                    int trAt=U16(source,atFrame+12);
                    int trans=U16(source,atFrame+14);
                    Assert.Equal(joints*8,quatBytes);
                    Assert.Equal(quatBytes+scales*8,trAt);
                    Assert.Equal((trAt+trans*8+15)/16,qwc);
                    Assert.InRange(atFrame+16+qwc*16,0,source.Length);
                    var decodedFrame=decodedClasses[id].Sequences.Single(q=>q.Index==s).Frames[f];
                    Assert.True(decodedFrame.ExtraChannelsResolved);
                    Assert.Equal(S16(source,atFrame+4),decodedFrame.TimeEighthTicks);
                    Assert.Equal(scales,decodedFrame.ScaleOverrides.Count);
                    Assert.Equal(trans,decodedFrame.TranslationOverrides.Count);
                    if(scales>0){framesWithScales++;seqS++;}
                    if(trans>0){framesWithTranslations++;seqT++;}
                    totalFrames++;scaleRecords+=scales;translationRecords+=trans;
                    seqScales+=scales;seqTrans+=trans;
                    for(int i=0;i<scales;i++)
                    {
                        int p=atFrame+16+quatBytes+i*8;
                        int joint=source[p+6],flag=source[p+7];
                        Assert.InRange(joint,0,joints-1);
                        Assert.Equal(0,flag&0x7f);
                        if((flag&0x80)!=0)hierarchyScales++;else postScales++;
                        var actualScale=decodedFrame.ScaleOverrides[i];
                        Assert.Equal(joint,actualScale.Joint);
                        Assert.Equal((flag&0x80)!=0,actualScale.Inherited);
                        Assert.Equal(U16(source,p)/4096f,actualScale.X);
                        Assert.Equal(U16(source,p+2)/4096f,actualScale.Y);
                        Assert.Equal(U16(source,p+4)/4096f,actualScale.Z);
                        if(U16(source,p)!=4096||U16(source,p+2)!=4096||U16(source,p+4)!=4096)nonUnitScales++;
                    }
                    for(int i=0;i<trans;i++)
                    {
                        int p=atFrame+16+trAt+i*8;
                        int joint=source[p+6],pad=source[p+7];
                        Assert.InRange(joint,0,joints-1);
                        Assert.Equal(0,pad);
                        var actualTranslation=decodedFrame.TranslationOverrides[i];
                        Assert.Equal(joint,actualTranslation.Joint);
                        Assert.Equal(S16(source,p)/1024f,actualTranslation.X);
                        Assert.Equal(S16(source,p+2)/1024f,actualTranslation.Y);
                        Assert.Equal(S16(source,p+4)/1024f,actualTranslation.Z);
                        if(S16(source,p)!=0||S16(source,p+2)!=0||S16(source,p+4)!=0)nonzeroTranslationOverrides++;
                    }
                }
                seqCounts.Add($"{s}:{frames}f/{seqS}sf/{seqT}tf/{seqScales}s/{seqTrans}t");
            }
            output.WriteLine($"class={id} joints={joints} sequences={seqCounts.Count} frames={totalFrames} " +
                $"framesWithScales={framesWithScales} framesWithTranslations={framesWithTranslations} " +
                $"scaleRecords={scaleRecords} nonUnitScaleRecords={nonUnitScales} hierarchyScales={hierarchyScales} postScales={postScales} " +
                $"translationRecords={translationRecords} nonzeroTranslations={nonzeroTranslationOverrides}");
            output.WriteLine("  sequence counts: "+string.Join(" ",seqCounts));
            Assert.Equal(id==2466?628:205,totalFrames);
            var decodedFrames=decodedClasses[id].Sequences.SelectMany(s=>s.Frames).ToArray();
            Assert.Equal(totalFrames,decodedFrames.Length);
            Assert.All(decodedFrames,f=>Assert.True(f.ExtraChannelsResolved));
            Assert.Equal(scaleRecords,decodedFrames.Sum(f=>f.ScaleOverrides.Count));
            Assert.Equal(translationRecords,decodedFrames.Sum(f=>f.TranslationOverrides.Count));
            Assert.Equal(hierarchyScales,
                decodedFrames.Sum(f=>f.ScaleOverrides.Count(s=>s.Inherited)));
            Assert.Equal(postScales,
                decodedFrames.Sum(f=>f.ScaleOverrides.Count(s=>!s.Inherited)));
        }
    }
}
