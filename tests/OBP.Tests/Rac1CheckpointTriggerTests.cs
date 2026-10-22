using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1CheckpointTriggerTests
{
    [Fact]
    public void NativeClipVolumeAdmitsRotatedScaledBasisNotAnArbitraryRadius()
    {
        var volume = new Rac1ClipVolume(17, new(10, 20, 30),
            new(2, 2, 0), new(-3, 3, 0), new(0, 0, 4));
        Assert.True(volume.Contains(volume.Centre));
        Assert.True(volume.Contains(volume.Centre + .5f * volume.AxisX + .8f * volume.AxisY));
        Assert.False(volume.Contains(volume.Centre + 1.2f * volume.AxisX));
        Assert.False(volume.Contains(volume.Centre + 1.3f * volume.AxisZ));
    }

    [Fact]
    public void UnconditionalCheckpointTriggersOnSourceClipEntryAfterFirstUpdate()
    {
        var region = new Rac1ClipVolume(0, new(10, 20, 30),
            new(2, 0, 0), new(0, 3, 0), new(0, 0, 1));
        var session = new Rac1CheckpointTriggerSession([region]);
        session.Register(Placement(7, 805, new(10, 20, 30), 1.2f),
            new Rac1CheckpointMarkerAuthored(0, 0, false, -1));
        Assert.Equal(1, session.RegisteredCount);
        Assert.Equal(1, session.UngatedCount);
        Assert.Empty(session.Step(region.Centre)); // native command initialization
        var first = Assert.Single(session.Step(region.Centre));
        Assert.Equal(7, first.InstanceIndex);
        Assert.Equal(1.2, first.NativeYaw, 4);
        Assert.Empty(session.Step(region.Centre));
        Assert.Empty(session.Step(region.Centre + region.AxisX * 2));
        Assert.Single(session.Step(region.Centre)); // outside resets the armed flag
    }

    [Fact]
    public void UnknownGlobalModesSpawnSlotOrMissingVolumeCannotInventCheckpoint()
    {
        var region = new Rac1ClipVolume(0, new(10, 20, 30),
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        var session = new Rac1CheckpointTriggerSession([region]);
        session.Register(Placement(1, 805, region.Centre, 0), new(0, 2, false, -1));
        session.Register(Placement(2, 805, region.Centre, 0), new(0, 0, false, 5));
        session.Register(Placement(3, 805, region.Centre, 0), new(-1, 0, false, -1));
        Assert.Equal(3, session.RegisteredCount);
        Assert.Equal(0, session.UngatedCount);
        Assert.Empty(session.Step(region.Centre));
        Assert.Empty(session.Step(region.Centre));
    }

    [SkippableFact]
    public void AllRetailCheckpointsReferToValidNativeClipVolumes()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        int total = 0, ungated = 0;
        foreach (var level in Rac1DiscIndex.Read(reader).Levels)
        {
            var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            var volumes = Rac1ClipVolumes.Read(gameplay);
            var session = new Rac1CheckpointTriggerSession(volumes);
            foreach (var moby in Rac1Instances.Parse(gameplay).MobyInstances
                .Where(item => item.OClass == 805))
            {
                var bytes = moby.PVar!;
                var marker = new Rac1CheckpointMarkerAuthored(
                    BitConverter.ToInt32(bytes), BitConverter.ToUInt32(bytes, 8),
                    BitConverter.ToInt32(bytes, 0x10) != 0,
                    BitConverter.ToInt32(bytes, 0x18));
                session.Register(moby, marker);
                total++;
            }
            ungated += session.UngatedCount;
            Assert.True(volumes.Count > 0);
        }
        Assert.Equal(97, total);
        Assert.InRange(ungated, 70, 92);
    }

    private static Rac1Instances.MobyInstance Placement(int index, int oclass, Vector3 position, float yaw) =>
        new(index, oclass, 1f, (position.X, position.Y, position.Z), (0f, 0f, yaw),
            null, index, new byte[0x78], new byte[0x30]);
}
