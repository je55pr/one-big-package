using OBP.Core;
using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Player;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1TraversalEquipmentSwingTests
{
    [Fact]
    public void SeparateTargetFamilyRequiresItsOwnEnabledFieldAndSourcePayload()
    {
        byte[] pvar = Pvar();
        Assert.True(Rac1TraversalEquipmentSwingTarget.TryDecode(803, pvar, out var target));
        Assert.False(Rac1TraversalEquipmentSwingshotTarget.TryDecode(803, pvar, out _));
        Assert.False(Rac1TraversalEquipmentSwingTarget.TryDecode(758, pvar, out _));
        Assert.Equal(16, target.MaximumDistance);
        Assert.Equal(0.025f, target.RadiusGain);
        Assert.Equal(0.33f, target.RadiusDamping);
        Assert.Equal(11f / 60f, target.RadiusMaximumStep);
        Assert.True(target.HasUnrestrictedRegion);
        BinaryPrimitives.WriteInt32LittleEndian(pvar, 0);
        Assert.False(Rac1TraversalEquipmentSwingTarget.TryDecode(803, pvar, out _));
    }

    [Fact]
    public void RuntimeAdapterRetainsAuthoredIdentityAndNativeAnchor()
    {
        double[] matrix = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 71, 53, 280.07758, 1];
        var source = new RuntimeDynamicObject(ObpSourceGame.Rac1, 803, 701, null, "moby:803", "moby:701",
            new RuntimeObjectTransform(matrix), [], [new("rac1-pvar", Pvar())]);
        var actual = Rac1TraversalEquipmentSwingTarget.ReadAuthored(source);
        Assert.NotNull(actual);
        Assert.Equal(701, actual.Value.InstanceIndex);
        Assert.Equal(new Rac1NativeVector3(71, 280.07758, 53), actual.Value.NativeAnchor);
        Assert.Null(Rac1TraversalEquipmentSwingTarget.ReadAuthored(source with { SourceGame = ObpSourceGame.Rac2 }));
    }

    [Fact]
    public void SwingCandidateHasDistinctPitchRangeAndBelowPlayerGuards()
    {
        var target = Target();
        Assert.NotNull(target.CandidateScore(new(0, 0, 0), new(10, 0, 7), 0, true, true, true));
        Assert.Null(target.CandidateScore(new(0, 0, 0), new(10, 0, 11), 0, true, true, true));
        Assert.Null(target.CandidateScore(new(0, 0, 4), new(10, 0, 0), 0, true, true, true));
        Assert.NotNull(target.CandidateScore(new(0, 0, 3), new(10, 0, 0), 0, true, true, true));
        Assert.Null(target.CandidateScore(new(0, 0, 0), new(17, 0, 0), 0, true, true, true));
        Assert.NotNull(target.CandidateScore(new(0, 0, 0), new(1, 0, 0), 0, true, true, true));
    }

    [Fact]
    public void NativeRadiusSeekEmitsSeparatePositionCorrection()
    {
        var session = new Rac1TraversalEquipmentSwing();
        var position = new Rac1NativeVector3(8, 0, 1);
        var anchor = Rac1NativeVector3.Zero;
        session.Attach(Target(), position, anchor, new(0, 0.1, 0));
        double initialRadius = session.Radius;
        var step = session.Step(position, anchor, new(0, 0.1, 0), 0, 0, 0, 5, true);
        Assert.True(step.Radius < initialRadius);
        Assert.True(step.PositionCorrection.Length > 0);
        var attachment = new Rac1NativeVector3(position.X, position.Y - 0.15f, position.Z);
        var final = attachment + step.Request + step.PositionCorrection;
        Assert.Equal(step.Radius, final.Length, 10);
    }

    [Fact]
    public void StaticNormalGravitySwingRunsAFullArcAndReleasesWithSourceFields()
    {
        var session = new Rac1TraversalEquipmentSwing();
        var position = new Rac1NativeVector3(0, 0, 0);
        var anchor = new Rac1NativeVector3(5, 0, 7);
        var translation = new Rac1NativeVector3(0.1, 0, 0);
        session.Attach(Target(), position, anchor, translation);
        double greatestX = position.X;
        for (int tick = 0; tick < 150; tick++)
        {
            var step = session.Step(position, anchor, translation, session.EntryFacingYaw, 0, 0, 5, true);
            translation = step.Request + step.PositionCorrection;
            position += translation;
            greatestX = Math.Max(greatestX, position.X);
            Assert.True(double.IsFinite(position.Length));
            Assert.True(Math.Abs(session.Radius - 7) < 2);
            Assert.Null(session.EvaluateExit(true));
        }
        Assert.True(greatestX > 5);
        Assert.False(session.InitialBoost);
        var exit = session.EvaluateExit(false);
        Assert.NotNull(exit);
        Assert.Equal(0x2d, exit.Value.NativeAction);
        Assert.False(exit.Value.PrematureRelease);
        Assert.Equal(27d / 3600d, exit.Value.NativeFallField940, 12);
        Assert.Equal(7d / 3600d, exit.Value.NativeFallField944, 12);
        Assert.False(session.Active);
    }

    [Fact]
    public void ReleasingBeforeNativeMinimumAgeDoesNotDetach()
    {
        var session = new Rac1TraversalEquipmentSwing();
        session.Attach(Target(), new(0, 0, 0), new(5, 0, 7), Rac1NativeVector3.Zero);
        for (int i = 0; i < 10; i++)
        {
            session.Step(new(0, 0, 0), new(5, 0, 7), Rac1NativeVector3.Zero, 0, 0, 0, 5, true);
            Assert.Null(session.EvaluateExit(false));
        }
        session.Step(new(0, 0, 0), new(5, 0, 7), Rac1NativeVector3.Zero, 0, 0, 0, 5, true);
        var exit = session.EvaluateExit(false);
        Assert.NotNull(exit);
        Assert.True(exit.Value.PrematureRelease);
        Assert.Equal(11, exit.Value.Sequence);
        Assert.Equal(15, exit.Value.BlendTicks);
    }

    [Fact]
    public void DynamicFacingTargetsRequireTheirLiveOwner()
    {
        var session = new Rac1TraversalEquipmentSwing();
        Assert.Throws<NotSupportedException>(() => session.Attach(Target() with { FollowsTargetFacing = true },
            Rac1NativeVector3.Zero, new(5, 0, 7), Rac1NativeVector3.Zero));
    }

    [SkippableFact]
    public void RealAridiaAuthoredTargetsSeparateSixSwingFromElevenPullTargets()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 2);
        var gameplay = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level));
        var swings = gameplay.MobyInstances.Where(i => i.OClass == 803).ToArray();
        Assert.Equal(6, swings.Length);
        Assert.Equal(11, gameplay.MobyInstances.Count(i => i.OClass == 758));
        var first = swings.Single(i => i.Index == 701);
        Assert.True(Rac1TraversalEquipmentSwingTarget.TryDecode(803, first.PVar!, out var target));
        Assert.Equal(7, target.Radius);
        Assert.Equal(0.33333334f, target.MaximumRequestLength);
        Assert.True(target.HasUnrestrictedRegion);
        Assert.False(target.FollowsTargetFacing);
        Assert.Equal(71f, first.Position.X);
        Assert.Equal(280.07758f, first.Position.Y);
        Assert.Equal(53f, first.Position.Z);
    }

    [SkippableFact]
    public void VeldinSourcePinsTheSeparateSwingFamilyAndAttachmentFlag()
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
            (0x223bbc, 0x223e0c, "a8b1e25b51fbb89f766f7977e12f3b00b39b2dc37d891f00f97cfb5669213adc"),
            (0x219e70, 0x21a720, "95c2421d063027557dda174a1906eff40acc5c337e0b07af617e1a5adc648d38"),
            (0x22c860, 0x22c8e8, "2aeb964e7e68bf55f629f1978f4a521792b9ec75b1706dc3e5f0a0e742daba87"),
            (0x221ee0, 0x222158, "da484defd6fa857be16166d223f4bd93cef29d068b477d32227fe663ecef7b2c"),
            (0x25b798, 0x25b970, "0cb19b0f7c63f4da89b9d292ac471d0917816121e2fbd86cd8a30a0defaad769"),
            (0x2c6c2c, 0x2c6ca4, "30eb66987d9380cfc42aa45185bf5ca9159fa842c0acfa1d1a0cd34601ef4031"),
        })
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(Read(start, end - start))).ToLowerInvariant());
        Assert.Equal(0x24020323, BinaryPrimitives.ReadInt32LittleEndian(Read(0x221f40, 4)));
    }

    private static Rac1TraversalEquipmentSwingTarget Target()
    {
        Assert.True(Rac1TraversalEquipmentSwingTarget.TryDecode(803, Pvar(), out var target));
        return target;
    }

    private static byte[] Pvar()
    {
        var pvar = new byte[64];
        BinaryPrimitives.WriteInt32LittleEndian(pvar, 1);
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(4), 7);
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0xc), 1f / 3f);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x1c), -1);
        return pvar;
    }
}
