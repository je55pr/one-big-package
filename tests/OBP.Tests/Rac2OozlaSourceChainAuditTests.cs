using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.PS2.Vif;
using OBP.RAC2.Level;
using Xunit.Abstractions;

namespace OBP.Tests;

/// <summary>
/// Audit the original ISO -> WAD -> core -> class packet -> raw vertex chain.
/// Read raw vertex bytes independently of the production geometry decoder.
/// </summary>
public sealed class Rac2OozlaSourceChainAuditTests(ITestOutputHelper output)
{
    private readonly record struct RawVertex(short X, short Y, short Z);
    private static int I32(byte[] b,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset));
    private static int U16(byte[] b,int offset)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset));
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [SkippableFact]
    public void BothBookmarkedClassesHaveExactRawSourceVertexProvenance()
    {
        var iso=Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso),"OBP_GC_ISO not set");
        using var reader=new FileRandomAccessReader(iso!);
        var fs=Iso9660Filesystem.Open(reader);
        var wad=fs.OpenFile("/G/LEVEL1.WAD")!;
        var header=Rac2LevelWad.ReadHeader(wad);
        Assert.Equal(0x60,header.HeaderSizeField);
        var lump=Rac2LevelWad.RequireLump(wad,header,0);
        var core=Rac2LevelCore.Open(lump);
        var overlay=core.DataHeader.Overlay;
        byte[] elf=lump.Read(overlay.Offset,overlay.Size);
        output.WriteLine($"Native LEVEL1 overlay: offset=0x{overlay.Offset:x}, bytes={elf.Length}, start={Convert.ToHexString(elf.AsSpan(0,Math.Min(32,elf.Length)))}");
        Assert.Equal(core.Header.AssetsDecompressedSize,core.Assets.Length);
        output.WriteLine($"WAD bytes={wad.Length}, core lump bytes={lump.Length}, index={core.Index.Length}, decompressed assets={core.Assets.Length}, index_sha256={Hash(core.Index)}, assets_sha256={Hash(core.Assets)}");

        var gameplay=Rac2Instances.Read(Rac2LevelWad.RequireLump(wad,header,2));
        foreach(int id in new[]{2466,2859})
        {
            var chosen=gameplay.MobyInstances.Where(x=>x.OClass==id).ToArray();
            var reference=chosen.First(x=>x.Index==(id==2466?311:568));
            var pvarSizes=chosen.GroupBy(x=>x.PVarData?.Length??-1)
                .Select(g=>$"{g.Key}:{g.Count()}");
            output.WriteLine($"instance id={id}, placed={chosen.Length}, target_index={reference.Index}, mode_bits=0x{reference.ModeBits:x}, pvar_index={reference.PVarIndex}, pvar_size={reference.PVarData?.Length??-1}, PVar sizes=[{string.Join(",",pvarSizes)}], pvar_head={Convert.ToHexString((reference.PVarData??[]).Take(32).ToArray())}");
            var uniquePvars=chosen.Select(x=>Hash(x.PVarData??[])).Distinct().Count();
            var first=reference.PVarData??[];
            var distinctPositions=new List<int>();
            for(int i=0;i<first.Length;i++)
            {
                if(chosen.Any(x=>x.PVarData is null || x.PVarData.Length<=i || x.PVarData[i]!=first[i]))
                    distinctPositions.Add(i);
            }
            output.WriteLine($"  unique instance PVars={uniquePvars}/{chosen.Length}; varying bytes={distinctPositions.Count}; initial varying offsets=[{string.Join(",",distinctPositions.Take(45).Select(x=>$"0x{x:x}"))}]");
            Assert.Equal(41,chosen.Length);
        }

        var formatVariants=new List<(int Id,byte Selector)>();
        var bangleClasses=new List<int>();
        var tableRange=core.Header.MobyClasses;
        for(int slot=0;slot<tableRange.Count;slot++)
        {
            int at=tableRange.Offset+slot*GcUyaMoby.ClassEntrySize;
            int asset=I32(core.Index,at);
            if(asset<=0||asset+GcUyaMoby.ClassHeaderSize>core.Assets.Length)continue;
            int id=I32(core.Index,at+4);
            byte selector=core.Assets[asset+0x0b];
            if(selector!=0)formatVariants.Add((id,selector));
            if(core.Assets[asset+0x2c]!=0)bangleClasses.Add(id);
        }
        output.WriteLine($"Oozla class format variants: {formatVariants.Count} [{string.Join(", ",formatVariants.Select(x=>$"{x.Id}:{x.Selector}"))}], bangle offsets: {bangleClasses.Count} [{string.Join(",",bangleClasses)}]");
        foreach(int id in new[]{2466,2859})
        {
            var table=core.Header.MobyClasses;
            var matches=Enumerable.Range(0,table.Count)
                .Where(i=>I32(core.Index,table.Offset+i*GcUyaMoby.ClassEntrySize+4)==id).ToArray();
            Assert.Single(matches);
            int entry=table.Offset+matches[0]*GcUyaMoby.ClassEntrySize;
            int start=I32(core.Index,entry);
            Assert.InRange(start,1,core.Assets.Length-1);
            int end=core.SectionBoundaries.First(bound=>bound>start);
            var buf=core.Assets.AsSpan(start,end-start).ToArray();
            Assert.Equal(buf,core.Assets.AsSpan(start,buf.Length).ToArray());
            int packetTable=I32(buf,0), packets=buf[4], joints=buf[8];
            // GC can embed R&C1-layout Mobys; native Wrench selects the packet
            // format from rac12_byte_b at class offset +0x0b.
            output.WriteLine($"class={id} format_selector_0x0b={buf[0x0b]} class_kind_0x46={buf[0x46]} highLOD={buf[4]} lowLOD={buf[5]} metal={buf[6]} metalBegin={buf[7]} bangles_offset_qw={buf[0x2c]} jointTable=0x{I32(buf,0x1c):x} collision=0x{I32(buf,0x10):x}");
            Assert.Equal(0,buf[0x0b]); // Normal R&C2 packet format.
            Assert.Equal(0,buf[0x06]); // No separate metal packets.
            Assert.Equal(0,buf[0x2c]); // No separate bangle packets.
            int skeleton=I32(buf,0x14),trans=I32(buf,0x18);
            int specialSequences=0,regularSequences=0,specialFrames=0,regularFrames=0,badFrameHeaders=0;
            double minQuaternionNorm=double.PositiveInfinity,maxQuaternionNorm=0,quaternionNormSum=0;
            int quaternions=0,nearUnitQuaternions=0;
            for(int s=0;s<buf[0x0c];s++)
            {
                int at=0x48+s*4;
                int so=I32(buf,at);
                if(so==0)continue;
                Assert.InRange(so,0,buf.Length-0x1c);
                int nf=buf[so+0x10],entryBase=so+0x1c;
                Assert.InRange(entryBase+nf*4,0,buf.Length);
                bool special=false;
                for(int f=0;f<nf;f++)
                {
                    uint frameEntry=(uint)I32(buf,entryBase+f*4);
                    if((frameEntry&0xf0000000)!=0){special=true;specialFrames++;}
                    else
                    {
                        regularFrames++;
                        int pos=(int)(frameEntry&0x0fffffff);
                        if(pos<0x10||pos+0x10+joints*8>buf.Length)badFrameHeaders++;
                        else
                        {
                            // Wrench's regular Rac123MobyFrameHeader has a
                            // qword count and a joint_data_size field.
                            int jointBytes=U16(buf,pos+8);
                            if(jointBytes!=joints*8)badFrameHeaders++;
                            for(int j=0;j<joints;j++)
                            {
                                double lengthSquared=0;
                                int off=pos+0x10+j*8;
                                for(int k=0;k<4;k++)
                                {
                                    double part=BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(off+k*2))/32768.0;
                                    lengthSquared+=part*part;
                                }
                                double norm=Math.Sqrt(lengthSquared);
                                minQuaternionNorm=Math.Min(minQuaternionNorm,norm);
                                maxQuaternionNorm=Math.Max(maxQuaternionNorm,norm);
                                quaternionNormSum+=norm;quaternions++;
                                if(Math.Abs(norm-1)<.02)nearUnitQuaternions++;
                            }
                        }
                    }
                }
                if(special)specialSequences++;else regularSequences++;
            }
            output.WriteLine($"class={id} sequences={buf[0x0c]} regular={regularSequences} special={specialSequences} regularFrames={regularFrames} specialFrames={specialFrames} rawJointSizeMismatches={badFrameHeaders}");
            output.WriteLine($"  frame joint quadruples count={quaternions}; norm min={minQuaternionNorm:F4} mean={quaternionNormSum/Math.Max(1,quaternions):F4} max={maxQuaternionNorm:F4}; near unit ±2%={nearUnitQuaternions}/{quaternions}");
            Assert.Equal(0,specialSequences);
            Assert.Equal(0,specialFrames);
            Assert.Equal(0,badFrameHeaders);
            Assert.Equal(id==2466?628:205,regularFrames);
            double scale=BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(0x24))/1024.0;
            Assert.True(packets>0&&joints>0&&packetTable>0&&scale>0);
            Assert.InRange(packetTable+packets*16,0,buf.Length);
            Assert.InRange(skeleton+joints*0x40,0,buf.Length);
            Assert.InRange(trans+joints*0x10,0,buf.Length);
            int unalignedParent=0,outOfRangeParent=0,nonFiniteJointMatrix=0,nonIdentityJointBases=0;
            for(int j=0;j<joints;j++)
            {
                int parent=U16(buf,trans+j*0x10+0x0c);
                if(parent%0x40!=0)unalignedParent++;
                if(parent>=joints*0x40)outOfRangeParent++;
                int mat=skeleton+j*0x40;
                double deviation=0;
                for(int row=0;row<3;row++)
                for(int col=0;col<3;col++)
                {
                    float value=BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(mat+(row*4+col)*4));
                    if(!float.IsFinite(value))nonFiniteJointMatrix++;
                    deviation=Math.Max(deviation,Math.Abs(value-(row==col?1:0)));
                }
                if(deviation>1e-4)nonIdentityJointBases++;
            }
            output.WriteLine($"  native skeleton parents: unaligned={unalignedParent}, outside joint table={outOfRangeParent}; matrices nonfinite={nonFiniteJointMatrix}, nonidentity bases={nonIdentityJointBases}/{joints}");
            Assert.Equal(0,unalignedParent);
            Assert.Equal(0,outOfRangeParent);
            Assert.Equal(0,nonFiniteJointMatrix);
            var mesh=GcUyaMoby.ReadClass(buf,recoverPersistentVertexCache:true);
            var local=mesh.SkinLocalPositions;
            var rawOutput=new List<RawVertex>();
            var cache=new Dictionary<int,RawVertex>();
            int totalNew=0,totalDups=0,visiblePackets=0,stShortPackets=0,stExactPackets=0,indexShortPackets=0;
            for(int pkt=0;pkt<packets;pkt++)
            {
                int po=packetTable+pkt*16;
                int vifOffset=I32(buf,po);
                int vifLength=U16(buf,po+4)*16;
                int vh=I32(buf,po+8);
                Assert.InRange(vifOffset,0,buf.Length-vifLength);
                Assert.InRange(vh,0,buf.Length-16);
                int two=U16(buf,vh+2),three=U16(buf,vh+4),main=U16(buf,vh+6),
                    duplicate=U16(buf,vh+8),transfer=U16(buf,vh),vertexOfs=U16(buf,vh+12);
                int n=two+three+main;
                Assert.InRange(n,1,4096);
                // Also audit what the VIF stream supplies to the model before
                // any GS mesh construction: STs cover both main and reused verts.
                var unpacks=Vif.FilterUnpacks(Vif.ReadCommandList(
                    new ArraySegment<byte>(buf,vifOffset,vifLength)));
                if(unpacks.Count<2){indexShortPackets++;continue;}
                int stCount=unpacks[0].Data.Count/4;
                if(stCount<n+duplicate)stShortPackets++;
                if(stCount==n+duplicate)stExactPackets++;
                if(unpacks[1].Data.Count<4)indexShortPackets++;
                int baseOfs=vh+vertexOfs;
                Assert.InRange(baseOfs+n*16,0,buf.Length);

                var source=new RawVertex[n];
                var ids=new int[n];
                for(int v=0;v<n;v++)
                {
                    int off=baseOfs+v*16;
                    source[v]=new RawVertex((short)U16(buf,off+10),(short)U16(buf,off+12),(short)U16(buf,off+14));
                    ids[v]=U16(buf,off)&511;
                }
                // Seven-in-flight VU1 strip IDs: the last seven are stored in
                // trailing source records, NOT in the live vertex record.
                for(int v=7;v<n;v++) ids[v-7]=ids[v];
                int epilogue=buf[po+12]-vertexOfs/16-n;
                Assert.InRange(epilogue,1,6);
                Assert.InRange(baseOfs+(n+epilogue)*16,0,buf.Length);
                for(int v=Math.Max(7-n,0);v<epilogue;v++)
                {
                    int target=n+v-7;
                    if(target>=0&&target<n) ids[target]=U16(buf,baseOfs+(n+v)*16)&511;
                }
                int last=baseOfs+(n+epilogue-1)*16;
                for(int v=Math.Max(7-n-epilogue,0);v<6;v++)
                {
                    int target=n+epilogue+v-7;
                    if(target>=0&&target<n)ids[target]=U16(buf,last+4+2*v)&511;
                }

                for(int v=0;v<n;v++)
                {
                    rawOutput.Add(source[v]);
                    cache[ids[v]]=source[v];
                }
                int dupOfs=vh+16+transfer*2;
                if(dupOfs%4!=0)dupOfs+=2;
                if(dupOfs%8!=0)dupOfs+=4;
                Assert.InRange(dupOfs+duplicate*2,0,buf.Length);
                for(int v=0;v<duplicate;v++)
                {
                    int slot=U16(buf,dupOfs+v*2)>>7;
                    Assert.True(cache.TryGetValue(slot,out var x),$"oClass {id} packet {pkt} unresolved slot {slot}");
                    rawOutput.Add(x);
                }
                totalNew+=n;totalDups+=duplicate;visiblePackets++;
            }
            Assert.Equal(mesh.Positions.Length/3,rawOutput.Count);
            Assert.Equal(local.Length/3,rawOutput.Count);
            for(int i=0;i<rawOutput.Count;i++)
            {
                Assert.Equal(rawOutput[i].X*scale,local[i*3],precision:7);
                Assert.Equal(rawOutput[i].Y*scale,local[i*3+1],precision:7);
                Assert.Equal(rawOutput[i].Z*scale,local[i*3+2],precision:7);
            }
            output.WriteLine($"  VIF packets with short ST arrays={stShortPackets}, exact ST count={stExactPackets}/{visiblePackets}, short index stream={indexShortPackets}");
            Assert.Equal(0,stShortPackets);
            Assert.Equal(0,indexShortPackets);
            Assert.Equal(visiblePackets,stExactPackets);
            output.WriteLine($"class={id}, index_entry=0x{entry:x}, asset_offset=0x{start:x}, bytes={buf.Length}, sha256={Hash(buf)}, packets={visiblePackets}, joints={joints}, source_vertices={totalNew}, source_duplicates={totalDups}, decoded_vertices={rawOutput.Count}, scale={scale:F9}; every vertex matches raw source");
        }
    }
}
