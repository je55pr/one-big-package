using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1CratePickupNativeSourceTests
{
    [SkippableFact]
    public void ExecutableBlocksAndAmmoDescriptorsMatchRetainedAuthority()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay();
        int Word(int address) => BinaryPrimitives.ReadInt32LittleEndian(read(address, 4));
        int Half(int address) => BinaryPrimitives.ReadUInt16LittleEndian(read(address, 2));
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root,
            "research", "generated", "rac1-crate-bolt-probe.json")));
        foreach (var block in evidence.RootElement.GetProperty("blocks").EnumerateObject())
        {
            int start = Convert.ToInt32(block.Value.GetProperty("start").GetString(), 16);
            int end = Convert.ToInt32(block.Value.GetProperty("end").GetString(), 16);
            Assert.Equal(block.Value.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(read(start, end - start))).ToLowerInvariant());
        }
        Assert.Equal(new[] { 0x2a5e74, 0x2a60b8, 0x2a6408, 0x2a6548, 0x2a67d0 },
            Enumerable.Range(0, 5).Select(state => Word(0x1e9790 + state * 4)));
        foreach (var ammo in Rac1CrateSupply.AmmoDefinitions)
        {
            Assert.Equal(ammo.ModelClass, Half(0x179ac0 + ammo.Item * 0x4c + 0x3a));
            Assert.Equal(ammo.Amount, Half(0x1c40b0 + ammo.Item * 0x18 + 0xc));
        }
        Assert.Equal(0x2407015c, Word(0x2d0c74)); // Nanotech fragments348.
        Assert.Equal(0x24030162, Word(0x2d0c8c)); // Metal fragments354.
        Assert.Equal(0x24050165, Word(0x2d0c9c)); // Wood/ammo fragments357.
        Assert.Equal(0x3c030002, Word(0x2cf4c0)); // Metal requires bit17 (lui v1,2).
        Assert.Equal(0x0000202d, Word(0x2d0a38)); // Break local sound0, including501.
        Assert.Equal(0x24040001, Word(0x2d956c)); // Cloud first arrival requests local1.
        Assert.Equal(0x240701f5, Word(0x2d957c)); // On crate class501, not cloud806.
    }

    [SkippableFact]
    public void EveryRetailLevelLoadsTheFourBoltModelsAndTheirClassRemappedSounds()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(path!);
        foreach (var level in Rac1DiscIndex.Read(reader).Levels)
        {
            var assets = Rac1PickupAssets.Load(path!, level.LevelId);
            foreach (int id in new[] { 13, 14, 15, 16 })
            {
                var model = assets.Models[id];
                Assert.True(model.VertexCount > 0 && model.TriangleCount > 0);
                Assert.All(model.Surfaces, surface => Assert.All(surface.Positions, value => Assert.True(double.IsFinite(value))));
                var sound = assets.Sounds![(id, 0)];
                Assert.True(sound.Clip.FrameCount > 100);
                Assert.Contains(sound.Clip.InterleavedPcm16, sample => sample != 0);
            }
        }
    }

    [SkippableFact]
    public void VeldinClassDirectoryResolvesExactSoundIdsAndFragmentModels()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        var assets = Rac1PickupAssets.Load(path!, 0);
        Assert.Equal(new[] { 91, 92, 93, 94 }, Enumerable.Range(13, 4).Select(id => assets.Sounds![(id, 0)].BankSoundId));
        Assert.Equal(179, assets.Sounds![(500, 0)].BankSoundId);
        Assert.Equal(180, assets.Sounds![(501, 0)].BankSoundId);
        Assert.Equal(181, assets.Sounds![(501, 1)].BankSoundId);
        Assert.Equal(182, assets.Sounds![(511, 0)].BankSoundId);
        foreach (int id in new[] { 348, 349, 357, 358 }) Assert.True(assets.Models.ContainsKey(id));
    }

    [SkippableFact]
    public void PlacedBoltInitializationAndLifetimeWritesArePinnedSeparatelyFromCrateEmission()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay();
        int Word(int address) => BinaryPrimitives.ReadInt32LittleEndian(read(address, 4));
        Assert.Equal(0x24030003, Word(0x2a5e78)); // Authored init directly selects resting state3.
        Assert.Equal(unchecked((int)0xa2a30020), Word(0x2a5e8c));
        Assert.Equal(0x92820054, unchecked((uint)Word(0x2a5f34))); // Preserve authored height bypasses floor snap.
        Assert.Equal(0x0c07bf1c, Word(0x2a5f28)); // Init ray1efc70.
        Assert.Equal(0x86a200b2, unchecked((uint)Word(0x2a6ab0))); // Authored UID-indexed object-state write.
        Assert.Equal(0xa0640454, unchecked((uint)Word(0x2a6ac8)));
        Assert.Equal(0xa0640454, unchecked((uint)Word(0x2a6b28)));
        Assert.Equal(0x0c093ada, Word(0x2a6b2c)); // Then common object removal24eb68.
        Assert.Equal(0x3f26, Word(0x2a6060) & 0xffff);
        Assert.Equal(0x6666, Word(0x2a6064) & 0xffff); // class13 scale0.65.
    }

    [SkippableFact]
    public void EveryAuthoredCurrencyBoltHasTheAdmittedPlacedStateIncludingBataliaFloatingFlag()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(path!);
        int[] expected = [29, 281, 204, 145, 136, 251, 59, 9, 194, 0, 7, 40, 97, 0, 7, 0, 23, 14, 46];
        int total = 0, fives = 0, floating = 0;
        foreach (var level in Rac1DiscIndex.Read(reader).Levels)
        {
            var instances = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level)).MobyInstances;
            var bolts = instances.Where(moby => Rac1PlacedBolt.IsBoltClass(moby.OClass)).ToArray();
            Assert.Equal(expected[level.LevelId], bolts.Length);
            foreach (var bolt in bolts)
            {
                Assert.True(bolt.PVar!.Length >= Rac1PlacedBolt.MinimumPVarSize);
                Assert.InRange(BinaryPrimitives.ReadInt16LittleEndian(bolt.RawRecord.AsSpan(0xc)), 0, short.MaxValue);
                if (bolt.OClass == 14) fives++;
                if (bolt.PVar[0x54] != 0)
                {
                    floating++;
                    Assert.Equal(8, level.LevelId);
                }
            }
            total += bolts.Length;
        }
        Assert.Equal(1542, total);
        Assert.Equal(14, fives);
        Assert.Equal(127, floating);
    }
}
