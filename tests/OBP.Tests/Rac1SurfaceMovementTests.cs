using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1SurfaceMovementTests
{
    [Fact]
    public void IceAcceleratesAsAVectorTowardNativeInputTarget()
    {
        var session = new Rac1SurfaceMovementSession();
        session.Reset();
        var first = session.Step(Ice);
        Assert.Equal(Rac1SurfaceMovementSession.IceInputAcceleration, first.Motion.X, 12);
        Assert.Equal(0d, first.Motion.Y);
        Assert.Equal(-Rac1SurfaceMovementSession.IceSupportedDownwardRequest, first.Motion.Z);
        Assert.Equal(0x2f, first.NativeAction);
        Assert.Equal(55, first.Sequence);
        for (int update = 0; update < 200; update++)
            first = session.Step(Ice);
        Assert.Equal(Rac1SurfaceMovementSession.IceInputSpeed, first.Motion.X, 12);

        var partial = new Rac1SurfaceMovementSession();
        partial.Reset();
        for (int update = 0; update < 200; update++)
            first = partial.Step(Ice with { InputMagnitude = .5d, DesiredYaw = Math.PI / 2d });
        Assert.Equal(0d, first.Motion.X, 12);
        Assert.Equal(Rac1SurfaceMovementSession.IceInputSpeed * .5d, first.Motion.Y, 12);
    }

    [Fact]
    public void FlatIceNeutralPreservesContactMomentumAndCounterSteeringBrakesFaster()
    {
        var session = new Rac1SurfaceMovementSession();
        session.Reset(new(.07d, 0d, 0d));
        var neutral = session.Step(Ice with { InputMagnitude = 0d });
        Assert.Equal(.07d, neutral.Motion.X);
        session.Reset(new(.07d, 0d, 0d));
        var reverse = session.Step(Ice with { DesiredYaw = Math.PI });
        Assert.Equal(.07d - Rac1SurfaceMovementSession.IceInputAcceleration -
            Rac1SurfaceMovementSession.IceOppositionAcceleration, reverse.Motion.X, 12);
    }

    [Fact]
    public void IceOverspeedUsesTheSeparateNativeThirteenCoefficient()
    {
        var session = new Rac1SurfaceMovementSession();
        session.Reset(new(.12d, 0d, 0d));
        var result = session.Step(Ice with { InputMagnitude = 0d });
        Assert.Equal(.12d - Rac1SurfaceMovementSession.IceOverspeedDeceleration, result.Motion.X, 12);
    }

    [Fact]
    public void IceNeutralAtTargetRoundsTheNativeStoredLengthBeforeTestingOverspeed()
    {
        double speed = Rac1SurfaceMovementSession.IceInputSpeed;
        var session = new Rac1SurfaceMovementSession();
        session.Reset(new(speed, 0d, 0d));
        var request = session.Step(Ice).Motion;
        session.RecordPrimaryContact(request, new(speed, 0d, 0d));
        var neutral = session.Step(Ice with { InputMagnitude = 0d });
        Assert.Equal(speed, neutral.Motion.X, 12);

        // One true source-float ULP above the limit must still take the
        // thirteen-coefficient branch. This is a storage boundary, not epsilon.
        double nextSourceFloat = MathF.BitIncrement((float)speed);
        session.Reset(new(nextSourceFloat, 0d, 0d));
        neutral = session.Step(Ice with { InputMagnitude = 0d });
        Assert.Equal(nextSourceFloat - Rac1SurfaceMovementSession.IceOverspeedDeceleration,
            neutral.Motion.X, 12);
    }

    [Fact]
    public void IceSlidesDownhillAboveFiveDegreesEvenWithNoInput()
    {
        double angle = Math.PI / 6d;
        var session = new Rac1SurfaceMovementSession();
        session.Reset();
        var slope = Ice with
        {
            InputMagnitude = 0d,
            SurfaceNormal = new(-Math.Sin(angle), 0d, Math.Cos(angle)),
        };
        var first = session.Step(slope);
        Assert.Equal(-Rac1SurfaceMovementSession.IceSlopeAcceleration * angle /
            Rac1SurfaceMovementSession.IceSlopeReference, first.Motion.X, 12);
        for (int update = 0; update < 1000; update++)
            first = session.Step(slope);
        Assert.True(first.Motion.X < 0d);
        session.Reset();
        var below = session.Step(slope with
        {
            SurfaceNormal = new(-Math.Sin(.05d), 0d, Math.Cos(.05d)),
        });
        Assert.Equal(0d, below.Motion.X);
    }

    [Fact]
    public void IceCrouchSeedsControlledCarryOnceAndUsesOnePointSevenBrake()
    {
        var session = new Rac1SurfaceMovementSession();
        session.Reset(new(.08d, .06d, .04d));
        var first = session.Step(Ice with { CrouchHeld = true });
        Assert.Equal(4, first.NativeAction);
        Assert.Equal(13, first.Sequence);
        double firstPlanar = Math.Sqrt(first.Motion.X * first.Motion.X + first.Motion.Y * first.Motion.Y);
        Assert.Equal(.1d - Rac1SurfaceMovementSession.IceCrouchBrake, firstPlanar, 12);
        session.RecordPrimaryContact(first.Motion, first.Motion);
        var second = session.Step(Ice with { CrouchHeld = true });
        double secondPlanar = Math.Sqrt(second.Motion.X * second.Motion.X + second.Motion.Y * second.Motion.Y);
        Assert.Equal(firstPlanar - Rac1SurfaceMovementSession.IceCrouchBrake, secondPlanar, 12);
        Assert.Equal(-Rac1OrdinaryGroundContactMotion.GroundDownwardRequestPerTick, second.Motion.Z);
        Assert.Equal(0d, first.Motion.X * second.Motion.Y - first.Motion.Y * second.Motion.X, 12);
    }

    [Fact]
    public void CollisionFeedbackStopsIceMomentumAtAnObstructedWall()
    {
        var session = new Rac1SurfaceMovementSession();
        session.Reset(new(.07d, 0d, 0d));
        var first = session.Step(Ice with { InputMagnitude = 0d });
        session.RecordPrimaryContact(first.Motion, new(0d, 0d, first.Motion.Z));
        var next = session.Step(Ice with { InputMagnitude = 0d });
        Assert.Equal(0d, next.Motion.X);
        Assert.Equal(0d, next.Motion.Y);
    }

    [Fact]
    public void IceEdgeUsesPreviousFinalVerticalStepAndTwentyFiveSquaredTickGravity()
    {
        var session = new Rac1SurfaceMovementSession();
        session.Reset(new(.04d, 0d, -.02d));
        var result = session.Step(Ice with { IsGrounded = false, JumpPressAgeTicks = 0 });
        Assert.Equal(-.02d - 25f * Rac1SurfaceMovementSession.SquaredUpdateScale, result.Motion.Z, 12);
        Assert.Null(result.RequestedJumpAction);
    }

    [Fact]
    public void MudPreventsPlanarInputAndSwitchesSinkRateByDepthAndEscapeCount()
    {
        var session = new Rac1SurfaceMovementSession();
        session.Reset(new(.09d, 0d, 0d));
        var shallow = session.Step(Mud with { Position = new(0d, 0d, -.4d) });
        Assert.Equal(0d, shallow.Motion.X);
        Assert.Equal(0d, shallow.Motion.Y);
        Assert.Equal(-Rac1SurfaceMovementSession.SecondsPerUpdate, shallow.Motion.Z);
        Assert.Equal(0x68, shallow.NativeAction);
        Assert.Equal(113, shallow.Sequence);
        var deeper = session.Step(Mud with { Position = new(0d, 0d, -.6d) });
        Assert.Equal(-.25f * Rac1SurfaceMovementSession.SecondsPerUpdate, deeper.Motion.Z);
        session.RecordMudEscape();
        var afterEscape = session.Step(Mud with { Position = new(0d, 0d, -.6d) });
        Assert.Equal(-Rac1SurfaceMovementSession.SecondsPerUpdate, afterEscape.Motion.Z);
    }

    [Fact]
    public void MudEscapeRequiresSourceAgeWindowAndFewerThanTwoPreviousEscapes()
    {
        var session = new Rac1SurfaceMovementSession();
        session.Reset();
        for (int update = 0; update < 15; update++)
            Assert.Null(session.Step(Mud with { JumpPressAgeTicks = 0 }).RequestedJumpAction);
        var jump = session.Step(Mud with { JumpPressAgeTicks = 6 });
        Assert.Equal(0x69, jump.RequestedJumpAction);
        session.Reset(mudEscapeCount: 2);
        for (int update = 0; update < 100; update++)
            Assert.Null(session.Step(Mud with { JumpPressAgeTicks = 0 }).RequestedJumpAction);
    }

    [Fact]
    public void MudRequestsEnvironmentalDeathBelowOnePointTwoOrAfterTheNativeTimer()
    {
        var session = new Rac1SurfaceMovementSession();
        session.Reset();
        Assert.True(session.Step(Mud with { Position = new(0d, 0d, -1.21d) }).RequestsEnvironmentalDeath);
        session.Reset();
        for (int update = 0; update < 170; update++)
            Assert.False(session.Step(Mud).RequestsEnvironmentalDeath);
        Assert.True(session.Step(Mud).RequestsEnvironmentalDeath);
    }

    [Fact]
    public void ClassZeroTransitionConsumesExistingCarryWithoutInventingFreeWalking()
    {
        var session = new Rac1SurfaceMovementSession();
        session.Reset(new(.03d, 0d, 0d));
        var result = session.Step(Ice with { Interaction = Rac1SurfaceInteractionKind.ShallowWaterWade });
        Assert.Equal(0x72, result.NativeAction);
        Assert.Equal(.03d - Rac1SurfaceMovementSession.OrdinaryCarryBrake, result.Motion.X, 12);
        for (int update = 0; update < 20; update++)
            result = session.Step(Ice with { Interaction = Rac1SurfaceInteractionKind.ShallowWaterWade });
        Assert.Equal(0d, result.Motion.X);
        Assert.Null(result.RequestedJumpAction);
    }

    [SkippableFact]
    public void CanonicalOverlayPinsSpecialFloorMotionAndDistinctPlanarContactProjection()
    {
        var read = ReadOverlay();
        foreach (var (start, end, hash) in new[]
        {
            (0x2167d0, 0x216de8, "6fa866cf770b0387c5009b06c8bfbcc97667b5647a0ff4fe96050d6007574bce"),
            (0x21c438, 0x21c674, "04832a09ab002f52ad65579c1d6c02b057639962cd8f92e0c318905e779f104c"),
            (0x21f710, 0x21f77c, "3900f2c418ce965596ae9e9ea6591b7d0a235acab8ef0555f1b8cbe162ddf282"),
            (0x22668c, 0x226724, "39d704348841196c2f7889b224388b762d3acde71419c82e7974e54369dd53e7"),
            (0x22be90, 0x22bf30, "aba2e62489a38d688d77168d8716fbf0a5bd5238a303fa490099e46c1415635e"),
            (0x227344, 0x227388, "5435aedb44cca0d4b2e7f11dc8be9e7c9d78a028005b1736658ff2d4df4d0fef"),
            (0x217a18, 0x217a5c, "9dc7bf54ceb0b6449c1bc85b7f413b2fde1adfc8206d06ddd3cc2ec7c8ddf293"),
            (0x217f4c, 0x217fcc, "d3abcc556c6cf9ee2908c2d02f3e4750c72b2332166d5e26042099c64c313cbd"),
            (0x213b5c, 0x213bbc, "9f4eab2a6fc287f8908e6db0bddedcae1d1ad033ddd3943388fedc9e90db6df0"),
        })
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(read(start, end - start))).ToLowerInvariant());
        int word(int address) => BinaryPrimitives.ReadInt32LittleEndian(read(address, 4));
        float literal(int address)
        {
            int high = (word(address) & 0xffff) << 16;
            int next = word(address + 4);
            return BitConverter.Int32BitsToSingle(high | ((next >> 16) == 0x3421 ? next & 0xffff : 0));
        }
        Assert.Equal(Rac1SurfaceMovementSession.IceInputSpeed, literal(0x2168a8) * (1f / 60f));
        Assert.Equal(Rac1SurfaceMovementSession.IceInputAcceleration, literal(0x2169e0) * (1f / 3600f));
        Assert.Equal(Rac1SurfaceMovementSession.IceOverspeedDeceleration, literal(0x216aa8) * (1f / 3600f));
        Assert.Equal(Rac1SurfaceMovementSession.IceSlopeThreshold, literal(0x216bc4));
        Assert.Equal(Rac1SurfaceMovementSession.IceSlopeSpeed, literal(0x216bfc) * (1f / 60f));
        Assert.Equal(Rac1SurfaceMovementSession.IceSlopeAcceleration, literal(0x216c18) * (1f / 3600f));
        Assert.Equal(Rac1SurfaceMovementSession.IceSupportedDownwardRequest, literal(0x21c660) * (1f / 3600f));
        Assert.Equal(0x21c438, word(0x1e7e20 + 0x2f * 4));
        Assert.Equal(0x21f710, word(0x1e7e20 + 0x68 * 4));
        Assert.Equal(0x217a00, word(0x1e7e20 + 0x72 * 4));
        Assert.Equal(0x0c000000 | 0x2167d0 >> 2, word(0x21c444));
        Assert.Equal(0x0c000000 | 0x2338d0 >> 2, word(0x213b6c));
        Assert.Equal(0x0c000000 | 0x2338d0 >> 2, word(0x213b78));
        Assert.Equal(unchecked((int)0xe6000160), word(0x213c38)); // swc1 f0,P+0x160
    }

    private static Rac1SurfaceMovementInput Ice => new(
        Rac1SurfaceInteractionKind.IceSlide, true, default, 0d,
        new(0d, 0d, 1d), 0d, 1d);
    private static Rac1SurfaceMovementInput Mud => Ice with { Interaction = Rac1SurfaceInteractionKind.MudSink };

    private static Func<int, int, byte[]> ReadOverlay()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == 0);
        var outer = level.CoreRanges[0];
        var data = new SubRangeReader(reader, outer.OffsetBytes, outer.SizeBytes, "level data");
        var directory = Rac1LevelData.ReadDirectory(data);
        var range = Rac1LevelData.OpenRange(data, directory, 0);
        byte[] payload = range.Read(0, checked((int)range.Length));
        Assert.Equal("eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804",
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());
        var sections = new List<(int Address, int Offset, int Size)>();
        for (int offset = 0; offset < payload.Length;)
        {
            int address = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
            int size = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4));
            Assert.Equal(0x245c28, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 12)));
            sections.Add((address, offset + 16, size));
            offset += 16 + size;
        }
        Assert.Equal(7, sections.Count);
        return (address, size) =>
        {
            var section = sections.Single(section => address >= section.Address && address + size <= section.Address + section.Size);
            return payload.AsSpan(section.Offset + address - section.Address, size).ToArray();
        };
    }
}
