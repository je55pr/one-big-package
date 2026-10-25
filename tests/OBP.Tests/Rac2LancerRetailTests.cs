using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OBP.IO;
using OBP.PS2;
using OBP.PS2.Compression;
using OBP.PS2.Elf;
using OBP.PS2.Iso;
using OBP.RAC2;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;

namespace OBP.Tests;

public sealed class Rac2LancerRetailTests
{
    [SkippableFact]
    public void RetailOozlaLancerRulesAndLocalDiscAssetsMatchThePortableSlice()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_GC_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        Assert.Equal(Rac2Authority.PrimaryIsoSizeBytes, reader.Length);
        Assert.Equal(Rac2Authority.Primary.Serial, Ps2Boot.ReadBootInfo(reader).Serial);
        var fs = Iso9660Filesystem.Open(reader);
        var exe = fs.OpenFile("/SCUS_972.68")!;
        var segments = Elf32Reader.ReadProgramHeaders(exe);
        byte[] Boot(uint address, int count) => Elf32Reader.ReadVirtualRange(exe, segments, address, count);
        Assert.Equal(30, Assert.Single(Boot(0x00139568 + Rac2LancerProfile.WeaponIndex, 1)));
        Assert.Equal(Rac2LancerProfile.TickSeconds, F32(Boot(0x001A7928, 4), 0));

        var wad = fs.OpenFile("/G/LEVEL1.WAD")!;
        var header = Rac2LevelWad.ReadHeader(wad);
        var data = Rac2LevelWad.RequireLump(wad, header, 0);
        var overlay = Rac2LevelOverlay.Open(data);
        var core = Rac2LevelCore.Open(data);
        uint Word(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(overlay.ReadVirtual(address, 4));

        var descriptor = overlay.ReadVirtual(0x002637A0 + 30 * 0xE0, 0xE0);
        Assert.Equal(Rac2LancerProfile.NameTextId, I32(descriptor, 8));
        Assert.Equal(Rac2LancerProfile.HeldMobyClass, I32(descriptor, 0x14));
        Assert.Equal(Rac2LancerProfile.GroundedPlayerFireSequence, I32(descriptor, 0x28));
        Assert.Equal(Rac2LancerProfile.ShotsPerSecond, F32(descriptor, 0x54));
        Assert.Equal(Rac2LancerProfile.ProjectileSpeed, F32(descriptor, 0x50));
        Assert.Equal(Rac2LancerProfile.ProjectileDamage.DamageHp, F32(descriptor, 0x70));
        Assert.Equal(Rac2LancerProfile.AmmoCapacity, BinaryPrimitives.ReadUInt16LittleEndian(descriptor.AsSpan(0x8E)));

        // Compact instruction witnesses: caller arguments, descriptor sources,
        // collision-before-expiry and the source-specific animation/sound calls.
        Assert.Equal(0x24050001u, Word(0x003A00CC)); // consume one
        Assert.Equal(0x0C0B25ACu, Word(0x003A00C8)); // ammo admission
        Assert.Equal(0xC4400054u, Word(0x003A0290)); // cadence from descriptor
        Assert.Equal(0x46000843u, Word(0x003A0298)); // 1 / cadence
        Assert.Equal(0xE641000Cu, Word(0x003A02A0)); // install cooldown
        Assert.Equal(0xE641000Cu, Word(0x003A0498)); // empty branch too
        Assert.Equal(0x3C013DCCu, Word(0x003A094C)); // 0.1f equip delay
        Assert.Equal(0x3421CCCDu, Word(0x003A0950));
        Assert.Equal(0x0C0B8C5Cu, Word(0x003A057C)); // native float countdown
        Assert.Equal(0xC7828938u, Word(0x002E3170)); // ordinary frame delta, gp-0x76C8
        Assert.Equal(0xA2910020u, Word(0x003A0588)); // state 2 -> 1, later update fires
        Assert.Equal(0x24050002u, Word(0x003A0188)); // held fire sequence
        Assert.Equal(0x24050001u, Word(0x003A0004)); // held idle after clip end
        Assert.Equal(0x24100005u, Word(0x002B0124)); // player fire transition, slot 30 branch
        Assert.Equal(0x24040BFEu, Word(0x003E3924)); // projectile allocation 3070
        Assert.Equal(0x3C0141A0u, Word(0x003A0A50)); // base-Lancer range 20
        Assert.Equal(0x46010502u, Word(0x003E39B8)); // speed * 1/60
        Assert.Equal(0x46146303u, Word(0x003E3ABC)); // range / per-tick speed
        Assert.Equal(0x0C0B4F20u, Word(0x003E3ED8)); // swept contact
        Assert.Equal(0x0C0B8C38u, Word(0x003E3F68)); // then lifetime decrement
        Assert.Equal(0x24040006u, Word(0x003E3F48)); // six-tick impact tail
        Assert.Equal(0x3C030001u, Word(0x003E3E60)); // damage flags 0x10000
        Assert.Equal(0xC4400070u, Word(0x003E3E80)); // damage HP from descriptor
        Assert.Equal(0x964300AAu, Word(0x003E3EA4)); // projectile class -> damage index
        Assert.Equal(0xA3B50058u, Word(0x003E3ED4)); // damage class 1
        Assert.Equal(0xA3B50059u, Word(0x003E3EDC)); // damage strength 1
        Assert.Equal(0xAFB50060u, Word(0x003E3E78)); // flags word 1
        Assert.Equal(0x0000202Du, Word(0x003A01C8)); // class-local fire sound 0
        Assert.Equal(0x0C0D560Au, Word(0x003A01D8)); // class sound dispatch
        Assert.Equal(0x24040001u, Word(0x003A0450)); // empty sound 1

        var gameplay = WadLz.ReadBlock(Rac2LevelWad.RequireLump(wad, header, 2), 0).Data;
        string Text(int id)
        {
            int block = I32(gameplay, 0x10);
            for (int i = 0; i < I32(gameplay, block); i++)
            {
                int at = block + 8 + i * 16;
                if (BinaryPrimitives.ReadInt16LittleEndian(gameplay.AsSpan(at + 4)) != id) continue;
                int start = block + I32(gameplay, at);
                int end = Array.IndexOf(gameplay, (byte)0, start);
                return Encoding.Latin1.GetString(gameplay, start, end - start);
            }

            throw new InvalidDataException($"Missing GC text id {id}.");
        }

        Assert.Equal("Lancer", Text(Rac2LancerProfile.NameTextId));
        Assert.Equal("Heavy Lancer", Text(2676));

        var gadget = fs.OpenFile(Rac2LancerAssets.GadgetIsoPath)!;
        byte[] row = gadget.Read(8 + 15 * 20, 20);
        Assert.Equal(new[] { 1490, 449, 17, 466, 9 },
            Enumerable.Range(0, 5).Select(i => I32(row, i * 4)).ToArray());
        var held = Rac2LancerAssets.ReadHeld(gadget);
        Assert.Equal(1490, held.Model.OClass);
        Assert.Equal(1325, held.Model.Mesh.Indices.Length / 3);
        Assert.Equal(new[] { 0, 1, 2 }, held.Model.Sequences.Select(s => s.Index));
        Assert.Equal(new[] { 11, 1, 5 }, held.Model.Sequences.Select(s => s.Frames.Count));
        var idleModel = Rac2LancerPresentation.HeldModel(held, 1, 0);
        foreach (var surface in idleModel)
            for (int v = 0; v < surface.Positions.Length; v += 3)
            {
                Assert.InRange(Math.Abs(surface.Positions[v] - held.Model.Mesh.SkinLocalPositions[v]), 0, .00002);
                Assert.InRange(Math.Abs(surface.Positions[v + 1] - held.Model.Mesh.SkinLocalPositions[v + 2]), 0, .00002);
                Assert.InRange(Math.Abs(surface.Positions[v + 2] - held.Model.Mesh.SkinLocalPositions[v + 1]), 0, .00002);
            }
        Assert.InRange(idleModel[0].Positions.Where((_, i) => i % 3 == 2).Min(), -.649, -.647);
        Assert.All(held.Model.TriangleTextureIds, id => Assert.Equal(0, id));
        Assert.Equal(0x50494632, I32(held.TexturePif, 0));
        Assert.Equal(128, I32(held.TexturePif, 8));
        Assert.Equal(128, I32(held.TexturePif, 12));
        Assert.Equal(19, I32(held.TexturePif, 16)); // native PSMT8
        Assert.Equal(3, held.SoundBank.Sounds.Count);
        foreach (var sound in held.SoundBank.Sounds)
            Assert.NotEmpty(held.SoundBank.DecodeTone(Assert.Single(sound.Tones)).Samples);

        var classes = Rac2MobyClasses.Read(core);
        var projectile = classes[3070];
        Assert.Equal(24, projectile.Mesh.Indices.Length / 3);
        Assert.Equal(new[] { 153 }, projectile.TextureIds);
        Assert.DoesNotContain(1490, classes.Keys);

        var witnesses = new (uint Address, int Size, string Sha256)[]
        {
            (0x002651E0, 224, "3322ab9c4ac5f382cfb06ce9fc64613b42b82ba70626a6552d6fc9196e0e6877"),
            (0x0039FA88, 3200, "d7b642c69bd9dfe21c22e4f943aac4ef5544b1610ea547b9c748924675184c9f"),
            (0x003A0948, 320, "a242732641b2c4043024bf27be9c6dafcedba4a21575bb73a782cd09118cf661"),
            (0x003E38F8, 912, "f462439cfce44c297b0f11aeae698f1757c686c6f8a577ea084347d393303f63"),
            (0x003E3C88, 1728, "d06076b053340d7dcbc1dc8f6d62d6f8d72a0ea5df1102068f48122bb7f492cc"),
            (0x002C96B0, 128, "268fbc3d57e5a32273468fae193aaa40c9da8fa61e40eac1f45c0df84c6a415d"),
            (0x002E3170, 64, "245a37d1d5426de1805083d49489783c2fbf2b1c5b632c4ead2729688a696dad"),
        };
        var code = witnesses.Select(witness =>
        {
            string hash = Convert.ToHexString(SHA256.HashData(overlay.ReadVirtual(witness.Address, witness.Size))).ToLowerInvariant();
            Assert.Equal(witness.Sha256, hash);
            return new { address = $"0x{witness.Address:x8}", size = witness.Size, sha256 = hash };
        }).ToArray();

        var evidence = new
        {
            schema = "obp.gc-lancer.v1",
            authority = new { buildId = Rac2Authority.Primary.BuildId, isoSha256 = Rac2Authority.Primary.Sha256 },
            method = "Static retail tables and loaded overlay dataflow; no live emulator observation claimed.",
            identity = new { sourceGame = "rac2", weaponIndex = 30, nameTextId = 2677, name = Text(2677), heldClass = 1490, projectileClass = 3070 },
            rules = new
            {
                capacity = I32(descriptor, 0x8C) >> 16,
                ammoPerShot = 1,
                equipDelaySeconds = 0.1f,
                fireRate = F32(descriptor, 0x54),
                projectileSpeed = F32(descriptor, 0x50),
                projectileRange = 20,
                flightTicks = 20,
                impactEffectTicks = 6,
                damageFlags = "0x00010000",
                damageClass = 1,
                damageStrength = 1,
                damageIndex = 3070,
                damageHp = F32(descriptor, 0x70),
                flags = 1
            },
            assets = new
            {
                heldFile = Rac2LancerAssets.GadgetIsoPath,
                directoryRow = 15,
                dataSector = 449,
                dataSectors = 17,
                soundBankSector = 466,
                soundBankSectors = 9,
                heldTriangles = 1325,
                heldSequenceFrames = new[] { 11, 1, 5 },
                heldTextureWidth = 128,
                heldTextureHeight = 128,
                fireSound = 0,
                emptySound = 1,
                projectileFile = "/G/LEVEL1.WAD",
                projectileTriangles = 24,
                oozlaProjectileTexture = 153
            },
            code,
        };
        string json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true })
            .Replace("\r\n", "\n") + "\n";
        string evidencePath = Path.Combine(RepoPaths.Root, "research", "generated", "gc-lancer.json");
        if (Environment.GetEnvironmentVariable("OBP_UPDATE_GC_LANCER_EVIDENCE") == "1")
            File.WriteAllText(evidencePath, json, new UTF8Encoding(false));
        Assert.Equal(File.ReadAllText(evidencePath).Replace("\r\n", "\n"), json);
    }

    private static int I32(byte[] bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at, 4));
    private static float F32(byte[] bytes, int at) => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at, 4));
}
