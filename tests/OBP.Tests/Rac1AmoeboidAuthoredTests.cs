using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1AmoeboidAuthoredTests
{
    [Fact]
    public void SourceLargeMediumSmallVariantsHaveDistinctDormantActivationAndSplit()
    {
        var pvar = new byte[Rac1AmoeboidAuthored.PVarSize];
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x25c), -1);
        Assert.Equal(Rac1AmoeboidAuthored.DormantPoolState,
            Rac1AmoeboidAuthored.Read(865, pvar).InitialNativeState);
        Assert.Equal(866, Rac1AmoeboidAuthored.Read(865, pvar).SplitChildClassId);
        Assert.Null(Rac1AmoeboidAuthored.Read(866, pvar).SplitChildClassId);

        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x25c), 44);
        Assert.Equal(13, Rac1AmoeboidAuthored.Read(866, pvar).InitialNativeState);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x258), 21);
        Assert.Equal(1, Rac1AmoeboidAuthored.Read(866, pvar).InitialNativeState);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AmoeboidAuthored.Read(827, pvar));
        Assert.Throws<InvalidDataException>(() =>
            Rac1AmoeboidAuthored.Read(572, new byte[0x100]));
    }

    [SkippableFact]
    public void AllRetailAmoeboidsAreThirtySevenActiveFourVolumeGatedAndOneEightySixDormant()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        int large = 0, medium = 0, small = 0, active = 0, gated = 0, dormant = 0;
        foreach (var level in Rac1DiscIndex.Read(reader).Levels)
        {
            byte[] gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            var clips = Rac1ClipVolumes.Read(gameplay);
            foreach (var moby in Rac1Instances.Parse(gameplay).MobyInstances
                .Where(m => Rac1AmoeboidAuthored.IsFamily(m.OClass)))
            {
                var source = Rac1AmoeboidAuthored.Read(moby.OClass, moby.PVar!);
                if (moby.OClass == 572) large++;
                if (moby.OClass == 865) medium++;
                if (moby.OClass == 866) small++;
                if (source.IsActiveOnLoad) active++;
                else if (source.IsSpawnPoolReserve) dormant++;
                else
                {
                    gated++;
                    Assert.Equal(44, source.VolumeIndex);
                    Assert.True(source.VolumeIndex < clips.Count);
                }
            }
        }
        Assert.Equal(30, large);
        Assert.Equal(63, medium);
        Assert.Equal(134, small);
        Assert.Equal(37, active);
        Assert.Equal(4, gated);
        Assert.Equal(186, dormant);
    }
}
