using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using Xunit.Abstractions;

namespace OBP.Tests;

/// <summary>Read-only retail check for persistent VU1 duplicate cache / trailing vertex IDs.</summary>
public sealed class Rac2MobyPacketCacheRetailTests(ITestOutputHelper log)
{
    [SkippableFact]
    public void OozlaPacketDuplicateReferences()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_GC_ISO absent");
        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile("/G/LEVEL1.WAD")!;
        var header = Rac2LevelWad.ReadHeader(wad);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var classes = Rac2MobyClasses.Read(core);
        int totalDups = 0, oldMissing = 0, correctLocalMissing = 0, priorPacketDups = 0,
            totalImpossible = 0, affectedClasses = 0, affectedPackets = 0, epilogueChanged = 0,
            badEpilogue = 0, skipped = 0;
        var witnesses = new List<(int Id, int Packets, int Dups, int OldMissing,
            int LocalMissing, int CrossPacket, int Impossible, int EpilogueChanged)>();
        var table = core.Header.MobyClasses;
        for (int i = 0; i < table.Count; i++)
        {
            int at = table.Offset + i * GcUyaMoby.ClassEntrySize;
            int start = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int id = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4));
            if (start <= 0 || !classes.ContainsKey(id)) continue;
            int end = core.SectionBoundaries.FirstOrDefault(b => b > start, core.Assets.Length);
            var buf = core.Assets.AsSpan(start, end - start);
            int packetTable = BinaryPrimitives.ReadInt32LittleEndian(buf);
            int count = buf[4];
            if (packetTable < 0 || packetTable + count * 16 > buf.Length) continue;
            var global = new HashSet<int>();
            int clsDups = 0, clsOld = 0, clsLocal = 0, clsCross = 0, clsImpossible = 0, clsChanged = 0;
            int worked = 0;
            for (int k = 0; k < count; k++)
            {
                int po = packetTable + k * 16;
                int vh = BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(po + 8));
                int dataSize = buf[po + 12];
                if (vh < 0 || vh + 16 > buf.Length) { skipped++; continue; }
                int transfers = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(vh));
                int two = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(vh + 2));
                int three = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(vh + 4));
                int main = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(vh + 6));
                int dupCount = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(vh + 8));
                int vo = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(vh + 12));
                int n = two + three + main;
                int basePos = vh + vo;
                if (n > 4096 || n < 1 || basePos + n * 16 > buf.Length || dataSize * 16 + vh > buf.Length) { skipped++; continue; }
                int epi = dataSize - vo / 16 - n;
                if (epi < 1 || epi > 6) { badEpilogue++; continue; }
                int[] raw = new int[n], old = new int[n], correct = new int[n];
                for (int v = 0; v < n; v++)
                    raw[v] = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(basePos + v * 16)) & 511;
                Array.Copy(raw, old, n);
                Array.Copy(raw, correct, n);
                for (int v = 7; v < n; v++) { old[v - 7] = raw[v]; correct[v - 7] = raw[v]; }
                for (int v = Math.Max(7 - n, 0); v < epi; v++)
                {
                    int dest = n + v - 7;
                    if (dest >= 0 && dest < n) correct[dest] = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(basePos + (n + v) * 16)) & 511;
                }
                int last = basePos + (n + epi - 1) * 16;
                for (int v = Math.Max(7 - n - epi, 0); v < 6; v++)
                {
                    int dest = n + epi + v - 7;
                    if (dest >= 0 && dest < n) correct[dest] = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(last + 4 + v * 2)) & 511;
                }
                clsChanged += old.Where((x, v) => x != correct[v]).Count();
                var oldLocal = old.ToHashSet();
                var correctLocal = correct.ToHashSet();
                int ofs = vh + 16 + transfers * 2;
                if (ofs % 4 != 0) ofs += 2;
                if (ofs % 8 != 0) ofs += 4;
                if (ofs + dupCount * 2 > buf.Length) { skipped++; continue; }
                int packetMissing = 0;
                foreach (int ix in Enumerable.Range(0, dupCount))
                {
                    int target = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(ofs + ix * 2)) >> 7;
                    clsDups++;
                    if (!oldLocal.Contains(target)) clsOld++;
                    if (!correctLocal.Contains(target))
                    {
                        clsLocal++;
                        packetMissing++;
                        if (global.Contains(target)) clsCross++;
                        else clsImpossible++;
                    }
                }
                if (packetMissing > 0) affectedPackets++;
                foreach (int ix in correct) global.Add(ix);
                worked++;
            }
            totalDups += clsDups; oldMissing += clsOld; correctLocalMissing += clsLocal;
            priorPacketDups += clsCross; totalImpossible += clsImpossible; epilogueChanged += clsChanged;
            if (clsOld > 0 || clsChanged > 0) affectedClasses++;
            if (clsDups > 0) witnesses.Add((id, worked, clsDups, clsOld, clsLocal, clsCross, clsImpossible, clsChanged));
        }
        log.WriteLine($"OOZLA classes affected={affectedClasses}, packets crossrefs={affectedPackets}, epilogue changed vertex IDs={epilogueChanged}, bad epilogues={badEpilogue}, skipped packets={skipped}");
        log.WriteLine($"duplicate references={totalDups}; missing in OBP per-packet cache={oldMissing}; missing current packet with correct trailing IDs={correctLocalMissing}; supplied by prior packet={priorPacketDups}; impossible even global={totalImpossible}");
        foreach (var w in witnesses.OrderByDescending(w => w.CrossPacket).ThenByDescending(w => w.OldMissing).Take(35))
            log.WriteLine($"class={w.Id} packets={w.Packets} dup={w.Dups} old_missing={w.OldMissing} current_missing={w.LocalMissing} from_prior={w.CrossPacket} impossible={w.Impossible} late_id_changes={w.EpilogueChanged}");
        Assert.True(totalDups > 0);
        Assert.Equal(0, totalImpossible);
        Assert.Equal(0, badEpilogue);
    }
}
