using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1CommonMobyAuthoredTests
{
    [Fact]
    public void MetalDetectorPathAndVolumeAreAlternativeAdmissionConditions()
    {
        var pathAndArea = new Rac1MetalDetectorTargetAuthored(3, 5, 50);
        Assert.False(pathAndArea.Eligible(false, false));
        Assert.True(pathAndArea.Eligible(true, false));
        Assert.True(pathAndArea.Eligible(false, true));
        Assert.True(new Rac1MetalDetectorTargetAuthored(-1, -1, 10).Eligible(false, false));

        var bytes = new byte[0x20];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x10), 3);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x14), -1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x18), 50);
        Assert.Equal(new Rac1MetalDetectorTargetAuthored(3, -1, 50),
            Rac1CommonMobyAuthored.ReadMetalDetectorTarget(Source(605, bytes)));
    }

    [Fact]
    public void CheckpointFlagsOnlyAdmitProvenNativeGameModes()
    {
        var marker = new Rac1CheckpointMarkerAuthored(7, 2, true, -1);
        Assert.True(marker.IsEligible(1, 99, true));
        Assert.False(marker.IsEligible(0, 99, true));

        marker = marker with { Flags = 4 };
        Assert.True(marker.IsEligible(0, 99, true));
        Assert.True(marker.IsEligible(3, 99, true));
        Assert.False(marker.IsEligible(1, 99, true));

        marker = marker with { Flags = 0x10, SpawnSlot = 12 };
        Assert.False(marker.IsEligible(0, 1, false));
        Assert.True(marker.IsEligible(0, 0, true));
        Assert.True(marker.IsEligible(0, 1, true));
        Assert.True(marker.IsEligible(0, 9, true));
        Assert.False(marker.IsEligible(0, 2, true));
        Assert.False((marker with { Flags = 8 }).IsEligible(0, 0, true));
    }

    [Fact]
    public void UnknownSourceOrMalformedPVarsCannotAcquireCheckpointOrDetectorSemantics()
    {
        Assert.Null(Rac1CommonMobyAuthored.ReadMetalDetectorTarget(Source(500, new byte[0x20])));
        Assert.Null(Rac1CommonMobyAuthored.ReadCheckpointMarker(Source(500, new byte[0x30])));
        Assert.Throws<InvalidDataException>(() =>
            Rac1CommonMobyAuthored.ReadMetalDetectorTarget(Source(605, new byte[0x10])));
        Assert.Throws<InvalidDataException>(() =>
            Rac1CommonMobyAuthored.ReadCheckpointMarker(Source(805, new byte[0x20])));
    }

    [SkippableFact]
    public void AllNineteenRetailLevelsRetainNativeDetectorAndCheckpointInputs()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        int detectors = 0, checkpoints = 0, blastCrates = 0, pulses = 0;
        foreach (var level in Rac1DiscIndex.Read(reader).Levels)
        {
            var mobies = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level)).MobyInstances;
            foreach (var moby in mobies)
            {
                if (moby.OClass == Rac1CommonMobyAuthored.BuriedMetalDetectorTarget)
                {
                    var target = Rac1CommonMobyAuthored.ReadMetalDetectorTarget(Source(moby.OClass, moby.PVar!));
                    Assert.NotNull(target);
                    Assert.Contains(target.InitialResources, new[] { 10, 50 });
                    detectors++;
                }
                else if (moby.OClass == Rac1CommonMobyAuthored.CheckpointMarker)
                {
                    Assert.NotNull(Rac1CommonMobyAuthored.ReadCheckpointMarker(Source(moby.OClass, moby.PVar!)));
                    checkpoints++;
                }
                else if (moby.OClass == Rac1CommonMobyAuthored.ExplosiveCrate) blastCrates++;
                else if (moby.OClass == Rac1CommonMobyAuthored.PulsingColourObject) pulses++;
            }
        }
        Assert.Equal(358, detectors);
        Assert.Equal(97, checkpoints);
        Assert.Equal(608, blastCrates);
        Assert.Equal(334, pulses);
    }

    [SkippableFact]
    public void LevelTwoCheckpointMarker708MatchesIndependentlyObservedNativeRestartRecord()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == 2);
        var source = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level))
            .MobyInstances.Single(moby => moby.Index == 708);
        Assert.Equal(Rac1CommonMobyAuthored.CheckpointMarker, source.OClass);
        Assert.Equal(205.5801544189, source.Position.X, 4);
        Assert.Equal(163.0475158691, source.Position.Y, 4);
        Assert.Equal(0.7809665203, source.Rotation.Z, 6);
        // The separately captured active record is 0.156 units below the
        // authored marker, matching retail's ground adjustment before saving.
        Assert.InRange(Math.Abs(source.Position.Z - 26.0592937469), 0.15, 0.16);
        var marker = Rac1CommonMobyAuthored.ReadCheckpointMarker(Source(source.OClass, source.PVar!));
        Assert.NotNull(marker);
        Assert.Equal(53, marker.VolumeIndex);
        Assert.Equal(0u, marker.Flags);
    }

    private static RuntimeDynamicObject Source(int nativeClass, byte[] pvar) =>
        new("rac1", nativeClass, 17, null, "native", "native",
            new RuntimeObjectTransform(new double[16]),
            [], NativePayloads: [new RuntimeOpaquePayload("rac1-pvar", pvar)]);
}
