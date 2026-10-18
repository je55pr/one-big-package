using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1TraversalEquipmentPullTests
{
    [Fact]
    public void FarTargetAcceleratesToSourceCapBeforeArrivalBraking()
    {
        var pull = new Rac1TraversalEquipmentPull();
        var request = pull.Step(new(0, 0, 0), new(100, 0, 0), 0, 0);
        Assert.Equal(Rac1TraversalEquipmentPull.AccelerationStep, request.Length, 12);
        Assert.True(request.X > 0);
        Assert.True(request.Y < 0);
        Assert.Equal(0, request.Z);
        for (int i = 0; i < 30; i++)
            pull.Step(new(0, 0, 0), new(100, 0, 0), Rac1TraversalEquipmentPull.MaximumStep, 0);
        Assert.Equal(Rac1TraversalEquipmentPull.MaximumStep, pull.Speed);
        Assert.False(pull.IsBraking);
        Assert.Null(pull.EvaluateExit(true, 5));
    }

    [Fact]
    public void ArrivalSwitchUsesTheLargerRootAndDeceleratesOnTheFollowingUpdate()
    {
        var pull = new Rac1TraversalEquipmentPull();
        for (int i = 0; i < 21; i++)
            pull.Step(new(0, 0, 0), new(100, 0, 0), 0.45, 0);
        double before = pull.Speed;
        double distance = Rac1TraversalEquipmentPull.ArrivalDistance + 1;
        pull.Step(new(0, 0, 0), new(distance, 0, 0), before, 0);
        double discriminant = before * before - 2 * Rac1TraversalEquipmentPull.BrakingStep;
        Assert.True(pull.IsBraking);
        Assert.Equal((before + Math.Sqrt(discriminant)) / Rac1TraversalEquipmentPull.BrakingStep,
            pull.RemainingTicks, 12);
        Assert.Equal(before, pull.Speed);
        pull.Step(new(0, 0, 0), new(distance, 0, 0), before, 0);
        Assert.Equal(before - Rac1TraversalEquipmentPull.BrakingStep, pull.Speed, 12);
    }

    [Fact]
    public void PullTrajectoryCompletesAutomaticallyAndCarriesFallFields()
    {
        var pull = new Rac1TraversalEquipmentPull();
        var position = new Rac1NativeVector3(0, 0, 0);
        var anchor = new Rac1NativeVector3(20, 0, 0);
        double contactLength = 0;
        Rac1TraversalEquipmentPullExit? exit = null;
        for (int tick = 0; tick < 240 && exit is null; tick++)
        {
            double yaw = Math.Atan2(anchor.Y - position.Y, anchor.X - position.X);
            var request = pull.Step(position, anchor, contactLength, yaw);
            position += request;
            contactLength = request.Length;
            exit = pull.EvaluateExit(true, 5);
        }
        Assert.NotNull(exit);
        Assert.True(pull.IsBraking);
        Assert.True(pull.NearArrival);
        Assert.Equal(6, exit.Value.NativeAction);
        Assert.False(exit.Value.PrematureRelease);
        Assert.True(pull.Speed < Rac1TraversalEquipmentPull.AutomaticReleaseStep);
        Assert.Equal(29d / 3600d, exit.Value.NativeFallField940, 12);
        Assert.Equal(42d / 3600d, exit.Value.NativeFallField944, 12);
        Assert.True(position.Z < 0);
    }

    [Fact]
    public void UseReleaseUsesStrictSupportGateAndCappedScalar()
    {
        var pull = new Rac1TraversalEquipmentPull();
        for (int i = 0; i < 21; i++)
            pull.Step(new(0, 0, 0), new(100, 0, 0), 0.45, 0);
        var grounded = pull.EvaluateExit(false, 0.29)!.Value;
        Assert.Equal(0, grounded.NativeAction);
        Assert.Equal(7d / 60d, grounded.ScalarSpeed, 12);
        Assert.True(grounded.PrematureRelease);
        Assert.Equal(6, pull.EvaluateExit(false, (double)0.3f)!.Value.NativeAction);
        pull.Reset();
        Assert.Equal(0, pull.Speed);
        Assert.Equal(60, pull.RemainingTicks);
        Assert.False(pull.IsBraking);
        Assert.False(pull.NearArrival);
        Assert.Null(pull.EvaluateExit(true, 0));
    }

    [SkippableFact]
    public void ActualAridiaTargetsUseThePullModeAndUnrestrictedRegions()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 2);
        byte[] gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var instances = Rac1Instances.Parse(gameplay).MobyInstances.Where(i => i.OClass == 758).ToArray();
        Assert.Equal(11, instances.Length);
        foreach (var instance in instances)
        {
            Assert.True(Rac1TraversalEquipmentSwingshotTarget.TryDecode(758, instance.PVar!, out var target));
            Assert.Equal(Rac1TraversalEquipmentSwingshotMode.Pull, target.Mode);
        }
        var first = instances.Single(i => i.Index == 629);
        Assert.True(Rac1TraversalEquipmentSwingshotTarget.TryDecode(758, first.PVar!, out var firstTarget));
        Assert.True(firstTarget.HasUnrestrictedRegion);
        Assert.Equal(7, firstTarget.Radius);
        Assert.Equal(30, firstTarget.MaximumDistance);
        Assert.Equal(136.16592f, first.Position.X);
        Assert.Equal(253f, first.Position.Y);
        Assert.Equal(34.999657f, first.Position.Z);
    }

    [SkippableFact]
    public void VeldinOverlayPinsPullMotionQuadraticAndExitBranches()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 0);
        var outer = level.CoreRanges[0];
        var data = new SubRangeReader(reader, outer.OffsetBytes, outer.SizeBytes, "level data");
        var range = Rac1LevelData.OpenRange(data, Rac1LevelData.ReadDirectory(data), 0);
        byte[] payload = range.Read(0, checked((int)range.Length));
        Assert.Equal("eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804",
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());
        var sections = new List<(int Address, int Offset, int Size)>();
        for (int offset = 0; offset < payload.Length;)
        {
            int address = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
            int size = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4));
            sections.Add((address, offset + 16, size));
            offset += 16 + size;
        }
        byte[] Read(int address, int size)
        {
            var section = sections.Single(s => address >= s.Address && address + size <= s.Address + s.Size);
            return payload.AsSpan(section.Offset + address - section.Address, size).ToArray();
        }
        foreach (var (start, end, hash) in new[]
        {
            (0x223b20, 0x223b64, "57eb6ea79e2ef07fdb0bac1d125a0d9a605fde2719c25a977d00e17d1e10b8ba"),
            (0x219a0c, 0x219d74, "111b5cdfebef84dd54ca9881343ebe8acb1be2fa72342f2f728409369ba5f90d"),
            (0x22c5c8, 0x22c7e0, "b0c4f587f23d8c8d1d860e87b481d621df991123d6ce2f234615750790012156"),
            (0x2595a0, 0x259698, "7783bcc67a1fbc3a450030f0f5e5d9b233cd7e64a28753b0a289a15374bee3f3"),
        })
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(Read(start, end - start))).ToLowerInvariant());
        Assert.Equal(0x44806000, BinaryPrimitives.ReadInt32LittleEndian(Read(0x219a38, 4)));
        Assert.Equal(unchecked((int)0x8e030978), BinaryPrimitives.ReadInt32LittleEndian(Read(0x2c68e0, 4)));
    }
}
