using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2.Level;

namespace OBP.Tests;

public sealed class GcAranosGameplayTests
{
    [SkippableFact]
    public void Level0CrateFamilySharesRetailUpdateAndZeroPostBreakRoute()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile("/G/LEVEL0.WAD")
            ?? throw new FileNotFoundException("/G/LEVEL0.WAD");
        var header = GcLevelWad.ReadHeader(wad);
        var gameplay = GcInstances.Read(GcLevelWad.RequireLump(wad, header, 2));
        var overlay = GcLevelOverlay.Open(GcLevelWad.RequireLump(wad, header, 0));

        Assert.True(overlay.Sections.Count > 3);
        var vtable = overlay.Sections[3];
        Assert.Equal(0x002A2E80u, vtable.DestinationAddress);
        Assert.Equal(0xB14, vtable.CopySize);

        var dispatch = new Dictionary<int, uint>();
        var bytes = overlay.Raw.AsSpan(vtable.DataOffset, vtable.CopySize);
        for (int offset = 8; offset + 12 <= bytes.Length; offset += 12)
        {
            int oClass = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 4, 4));
            uint update = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 8, 4));
            if (oClass <= 0 || oClass >= 10_000)
            {
                continue;
            }

            dispatch.TryAdd(oClass, update);
        }

        Assert.Equal(0x0039B440u, dispatch[500]);
        Assert.Equal(dispatch[500], dispatch[501]);
        Assert.Equal(dispatch[500], dispatch[511]);

        var crates = gameplay.MobyInstances.Where(moby => moby.OClass == 500).ToArray();
        Assert.Equal(43, crates.Length);
        Assert.All(crates, crate =>
        {
            Assert.NotNull(crate.PVarData);
            Assert.Equal(0x110, crate.PVarData!.Length);
            Assert.Equal(0, crate.PVarData[0xC8]);
            Assert.Equal(0, crate.PVarData[0xCC]);
        });
    }
}
