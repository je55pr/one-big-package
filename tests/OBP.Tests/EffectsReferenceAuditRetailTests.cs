using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC1;
using OBP.RAC1.Level;
using OBP.RAC2;
using OBP.RAC2.Level;
using OBP.RAC3;
using OBP.RAC3.Level;

namespace OBP.Tests;

/// <summary>
/// Investigation witnesses for #281. These pin source data and call edges,
/// not a recovered simulation, an upstream C build, or rendered equivalence.
/// </summary>
public sealed class EffectsReferenceAuditRetailTests
{
    [SkippableFact]
    public void NovalisClass27AndType6HaveAConcreteRetailFixture()
    {
        using var disc = Open("OBP_RAC1_ISO", Rac1Authority.PrimaryIsoSizeBytes);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(l => l.LevelId == 1);
        var overlay = Rac1ExecutableOverlay.Read(disc, level);
        Assert.Equal("81673b8dcf5930c7313a7c83d0f76e5e1948691dc09af63dd8c8f6a04f9a72ae", overlay.Sha256);
        byte[] Read(uint address, int size) => overlay.Read(checked((int)address), size).ToArray();

        var emitters = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level))
            .MobyInstances.Where(m => m.OClass == 27).ToArray();
        Assert.Equal(Enumerable.Range(285, 10), emitters.Select(m => m.Index));
        foreach (var emitter in emitters)
        {
            byte[] pvars = Assert.IsType<byte[]>(emitter.PVar);
            Assert.Equal(0xe0, pvars.Length);
            Hash(pvars, "8d5d507bb3ef654ef549279a97fc2c94d08cb6a87e3c6db13ff2076f74f82c71");
            Assert.Equal(0x170eu, Word(pvars, 0x70)); // No collision; colour/alpha/acceleration enabled.
            Assert.Equal(150u, Word(pvars, 0xc0));
            Assert.Equal(1, pvars[0xc4]); Assert.Equal(0, pvars[0xc5]);
            Assert.Equal(4, pvars[0xc7]); // Texture definition 4, not particle type 6.
        }

        Hash(Read(0x27c498, 0x150), "5ddf1df5ff5d62616e083130d0b8858e5f5f501178d3027b64aed94521a108f0");
        Assert.Equal(0x2301f800u, Word(Read(0x27c4b0, 4))); // Full at 2048 records.
        Assert.Equal(0x00061180u, Word(Read(0x27c5c0, 4))); // Slot * 64.
        Assert.Equal(new uint[] { 0x7c400000, 0x7c400010, 0x7c400010, 0x7c400010 },
            Words(Read(0x27c5c8, 16))); // Retail leaves the second half stale.

        Hash(Read(0x27e900, 0x208), "746f87b6b0b215b03ac1969dd07815f6e9de346429f41ba798e1e3928af2a7d8");
        Hash(Read(0x27eb08, 0x6f8), "9d86ac90011e0ee8e06cbf31b79b6f9f6f831ee2c6c9ec4ec8c7bda14ecc9e56");
        Allocation(Read, 0x27e998, 0x27c498, 6);
        Call(Read, 0x2bd338, 0x27e900); // Class-27's random-Euler velocity branch.
        Call(Read, 0x2bd164, 0x2221f0); // The Novalis visibility gate.
        Call(Read, 0x27eb98, 0x211870); // Type-6 collision query, outside the chosen no-collision fixture.
    }

    [SkippableFact]
    public void MuseumEditorAndDemonstratorUseDifferentSourceParticleFamilies()
    {
        using var disc = Open("OBP_GC_ISO", Rac2Authority.PrimaryIsoSizeBytes);
        var wad = Iso9660Filesystem.Open(disc).OpenFile("/G/LEVEL21.WAD")!;
        var header = GcLevelWad.ReadHeader(wad);
        Assert.Equal(30, header.LevelId);
        var image = GcLevelOverlay.Open(GcLevelWad.RequireLump(wad, header, 0));
        Hash(image.Raw, "b46e44c26195efd160b1bc62910cf22e1b1fe3eb46944f5926a9f2b7879e45b6");
        var read = image.ReadVirtual;
        Assert.Equal(17, GcMuseumEffectControls.ReadParticleEditor(image).Count);

        Hash(read(0x330670, 0x150), "94d8be2522a9704dfbbb425339fba8dad82b2c3f4b7266acad214f7c6db707af");
        Assert.Equal(0x2301f800u, Word(read(0x330688, 4)));
        Assert.Equal(0x00061180u, Word(read(0x330798, 4)));
        Assert.Equal(new uint[] { 0x7c400000, 0x7c400010, 0x7c400020, 0x7c400030 },
            Words(read(0x3307a0, 16))); // Unlike R&C1, all 64 bytes are cleared.
        Hash(read(0x3309c0, 0x9c), "a9e58f7934d5bbb0b0c345f0b2fff85c4933e8868a781633653ec32a1474fcb2");
        Assert.Equal(0x8c82003cu, Word(read(0x3309c0, 4))); // Callback at record +0x3c.

        // The editor calls the boot-family func_002C65A0's Museum placement.
        Call(read, 0x418b04, 0x33e8f8);
        Hash(read(0x33e8f8, 0x1a8), "66f1d77c061755046c9380067c664fa9f6460c056529ebced8826bcc1ae28a87");
        Allocation(read, 0x33e95c, 0x330670, 85);
        Hash(read(0x33eaa0, 0x120), "46b1f5f0f537fa40b04553af5587b6408b80d1c3fc423c6e90a22bdd352227eb");
        Assert.Equal(0x2463eaa0u, Word(read(0x33ea54, 4))); // Callback's low address half.
        Hash(read(0x331720, 0x5c0), "c91c92a2fdfd42a10328c7cccde4bd30cec6980bf5867ac072a7e457a0679e74");
        Assert.Equal(0xac520154u, Word(read(0x331c58, 4))); // Type 85 -> callback dispatcher.

        // Required upstream qcopy references are different families and placements.
        Hash(read(0x33f538, 0x174), "7f9588de468a1aaa1c3db8bc11df1f3b48fe2d5bbe4ce00e8a298568a89f038d");
        Allocation(read, 0x33f5a0, 0x330670, 91);
        Hash(read(0x33d2e8, 0x190), "66f9e8b10f739b41096e3ca0b918a04de760fbb019db5ca66bcc954bdfa8424e");
        Allocation(read, 0x33d340, 0x330670, 78);

        // The shared effect recipe reached by the Museum demonstrator also uses type 97.
        Call(read, 0x41911c, 0x419350);
        Hash(read(0x4196e0, 0xb0), "acc94751062ea9c80e2aa79fe46a33aa3e02f1b76fea0b6baf8cafa8afdac19e");
        Call(read, 0x419730, 0x3401b0);
        Hash(read(0x3401b0, 0x178), "961bcde831a1bd95c30d36bfd3371a7837e848a8651694617d51d899622d7b5d");
        Allocation(read, 0x340214, 0x330670, 97);
        Hash(read(0x340328, 0xcc), "7816bcc595c7acaa60492cba65a51cc1ce2c337b35c2047c86dd0943cf67fc86");
    }

    [SkippableFact]
    public void UyaChargeMuzzleAndReferenceSpawnerHaveSeparatePoolPaths()
    {
        using var disc = Open("OBP_UYA_ISO", Rac3Authority.PrimaryIsoSizeBytes);
        var main = UyaDiscIndex.OpenMainLevel(disc, 1).Reader;
        byte[] header = main.Read(0, 0x60);
        var primary = new SubRangeReader(main, (long)Word(header, 0x10) * 0x800, (long)Word(header, 0x14) * 0x800);
        byte[] range = primary.Read(0, 8);
        int size = checked((int)Word(range, 4));
        Assert.InRange(size, 0x10, 16 * 1024 * 1024);
        byte[] image = primary.Read(Word(range), size);
        Hash(image, "093fb8641cb2bf48073793f6cfac726cb7321654d285b848658f3e957df1dcec");
        byte[] Read(uint address, int count) => UyaVirtual(image, address, count);

        Hash(Read(0x45e438, 0x148), "3c54424c5d10960c8a213015c3d14c22cfd3b77349aa0b5a0b15ed7abebf739f");
        Assert.Equal(0x2301f800u, Word(Read(0x45e450, 4)));
        Assert.Equal(0x00061180u, Word(Read(0x45e558, 4)));
        Assert.Equal(new uint[] { 0x7c400000, 0x7c400010, 0x7c400020, 0x7c400030 },
            Words(Read(0x45e560, 16)));
        Hash(Read(0x45e788, 0x88), "774537af4bb98bedb7ab6746438dd591006432208e04b3ba50f1d4067f258c05");
        Assert.Equal(0x8c82003cu, Word(Read(0x45e778, 4)));

        Call(Read, 0x385c40, 0x46dcb8); // Held Shock Cannon charge emission, not the beam mesh.
        Hash(Read(0x46dcb8, 0x164), "00d87f614e1908b5cf43ec7aaecb9059e29947ec31cdcb064523b5ed3b5a4a3b");
        Allocation(Read, 0x46dd1c, 0x45e438, 97);
        Hash(Read(0x46de20, 0x138), "155e1b137d8d6607907ad7c69e1e1dfe1014dda20d2cdfe3f283e952b0b95be5");
        Call(Read, 0x46debc, 0x4c6ec8); // Additional UYA position/context hook, absent from the GC update.

        Call(Read, 0x386948, 0x3877d8);
        Call(Read, 0x387a18, 0x4633e8);
        Call(Read, 0x387b10, 0x466f98);
        Allocation(Read, 0x46343c, 0x45e438, 23);
        Allocation(Read, 0x466ffc, 0x45e438, 44);

        Hash(Read(0x46ee10, 0x1f8), "7272d0ba691dead1a6e05057ad29c968a5893ba47da7839166262426a07d71bf");
        Assert.Equal(0x2404006bu, Word(Read(0x46ee44, 4)));
        Call(Read, 0x46ee4c, 0x45e438); // Type 107 is a further lead, not proof of Cannon use.
        Hash(Read(0x45f4d0, 0x6c0), "ce8efefcfa5f788c2c053cd7ca702b8aadb1d446c00a1bee87bd98fda6f96a51");
        Assert.Equal(0xac460184u, Word(Read(0x45f9e8, 4))); // Type 97 registration.
        Assert.Equal(0xac4301acu, Word(Read(0x45fa70, 4))); // Type 107 registration.
    }

    private static FileRandomAccessReader Open(string variable, long expectedSize)
    {
        string? path = Environment.GetEnvironmentVariable(variable);
        Skip.If(string.IsNullOrWhiteSpace(path), $"{variable} not set");
        var disc = new FileRandomAccessReader(path!);
        Assert.Equal(expectedSize, disc.Length);
        return disc;
    }

    private static void Allocation(Func<uint, int, byte[]> read, uint call, uint allocator, int type)
    {
        Call(read, call, allocator);
        Assert.Equal(0x24040000u | (uint)type, Word(read(call + 4, 4)));
    }

    private static void Call(Func<uint, int, byte[]> read, uint address, uint expected)
    {
        uint word = Word(read(address, 4));
        Assert.Equal(3u, word >> 26);
        Assert.Equal(expected, ((address + 4) & 0xf0000000) | ((word & 0x03ffffff) << 2));
    }

    private static byte[] UyaVirtual(byte[] image, uint address, int count)
    {
        for (int at = 0; at + 0x10 <= image.Length;)
        {
            uint destination = Word(image, at);
            int size = checked((int)Word(image, at + 4));
            Assert.InRange(size, 0, image.Length - at - 0x10);
            if (Word(image, at + 8) is 1 or 8 && address >= destination &&
                (ulong)address + (uint)count <= (ulong)destination + (uint)size)
                return image.AsSpan(at + 0x10 + checked((int)(address - destination)), count).ToArray();
            at = checked(at + 0x10 + size);
        }
        throw new InvalidDataException($"UYA audit range 0x{address:x}+0x{count:x} is not mapped.");
    }

    private static uint Word(byte[] bytes, int offset = 0) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static IEnumerable<uint> Words(byte[] bytes) => Enumerable.Range(0, bytes.Length / 4).Select(i => Word(bytes, i * 4));
    private static void Hash(byte[] bytes, string expected) =>
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
}
