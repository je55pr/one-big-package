using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1OrdinaryLocomotionAnimationTests
{
    [SkippableFact]
    public void VeldinOverlayPinsRecoveredLocomotionCrouchAndStopRules()
    {
        var Read = ReadOverlay(0, 0x245c28,
            "eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804");
        float Float(int address) => BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(Read(address, 4)));
        int Word(int address) => BinaryPrimitives.ReadInt32LittleEndian(Read(address, 4));

        Assert.Equal("9cfea16db6221b3391feaa516631cddb0dbdd1c3061a9779a1ab0dc59ca61a10",
            Convert.ToHexString(SHA256.HashData(Read(0x228c18, 0x390))).ToLowerInvariant());
        Assert.Equal(0x2232d4, Word(0x1e8038));
        Assert.Equal(0x22b434, Word(0x1e82b8));
        // Ordering is part of the clock contract, not an inference from FPS.
        Assert.Equal(0x0c000000 | (0x232670 >> 2), Word(0x207184));
        Assert.Equal(0x0c000000 | (0x211670 >> 2), Word(0x20718c));
        Assert.Equal(0x0c000000 | (0x229b70 >> 2), Word(0x2071b4));
        Assert.Equal(0x0c000000 | (0x216f10 >> 2), Word(0x2071c8));
        // The movement executor is nested in input update, before action selection.
        Assert.Equal(0x0c000000 | (0x217970 >> 2), Word(0x211728));
        Assert.Equal(0x0c000000 | (0x2215c8 >> 2), Word(0x211814));
        Assert.Equal("dc3aca07cd5734c75ec07f253dea799924c05cf71b8cd16820f37b18d127cff2",
            Convert.ToHexString(SHA256.HashData(Read(0x232670, 0x360))).ToLowerInvariant());
        Assert.Equal(Rac1OrdinaryLocomotionAnimation.RunEntryStep,
            Float(0x17bdbc) * Rac1OrdinaryLocomotionAnimation.NativeSecondsPerUpdate);
        Assert.Equal(Rac1OrdinaryLocomotionAnimation.WalkReentryStep,
            Float(0x17bdc8) * Rac1OrdinaryLocomotionAnimation.NativeSecondsPerUpdate);
        Assert.Equal(Word(0x15f6d8), Rac1OrdinaryLocomotionAnimation.EntryNextFrame(3));
        Assert.Equal(Word(0x15f6dc), Rac1OrdinaryLocomotionAnimation.EntryNextFrame(4));
        for (int frame = 0; frame < 33; frame++)
            Assert.Equal((frame * 23 / 33 + Word(0x15f6cc)) % 23,
                Rac1OrdinaryLocomotionAnimation.TransitionNextFrame(3, frame, 33, 4, 23));
        for (int frame = 0; frame < 23; frame++)
            Assert.Equal((frame * 33 / 23 + Word(0x15f6d0)) % 33,
                Rac1OrdinaryLocomotionAnimation.TransitionNextFrame(4, frame, 23, 3, 33));

        Assert.Equal(0x2238e0, Word(0x1e8040));
        Assert.Equal(0x22c45c, Word(0x1e82c0));
        foreach (var (start, end, hash) in new[]
        {
            (0x2238e0, 0x223a1c, "e46e7156f8446af3493611bfdee6d8efd78792de9ab3023fca1b0586dd22410d"),
            (0x22c45c, 0x22c4c0, "8b11c0f15d6b05e95178a2fe51c210a49e97364ec31a50da64eb8b537c96ea85"),
            (0x217bf0, 0x217e08, "65c2d1843358984f55f6b5787954262f88d85e61a19e4f95d6d4b1f24dd5cda5"),
            (0x2325e0, 0x232670, "471c13556524e298ff7f535d0c67e6c62b4354390bf76d06ab22e25d020a0ff6"),
            (0x2323b8, 0x2325e0, "8a4f1131e0d2485bb88a884f706e6adbceaabb40863f3bc2c3ee356e4fbcf352"),
            (0x2237c8, 0x223840, "607bcdf85765aa46dc116498d4f954e5ee63a9548edd6fe23bb8de7dc3c8782d"),
            (0x22b934, 0x22bae8, "2687409e68565e6400d26d4a3d908bb23b21aa7c780fc04af43bb6bf6031d887"),
            (0x217f4c, 0x217fcc, "d3abcc556c6cf9ee2908c2d02f3e4750c72b2332166d5e26042099c64c313cbd"),
            (0x216710, 0x2167cc, "b0d8137a5645f27e1ba4ca75ba82e4fa7a64eb94173bcfd8230be729b4ddb006"),
            (0x211670, 0x211830, "cc665505013435ed79fd766780a2aaf9605bdd2c52a6e40a0eb444db906e07d5"),
            (0x213ae0, 0x213b5c, "a7d63ca9c57da3f99e07a2acc2600c6041e3db1c40d4829946d5cccbf25efa7b"),
            (0x213c64, 0x213d08, "a279e5380f07e012248ce72de8b4222beaf91eec4995f064e04ca7817fb4ddfb"),
            (0x1ff500, 0x1ff550, "5ec601583b8e7502d8e4a68f24eb807144f34a744b4b3bf3729851aab7cbed0e"),
            (0x1ff398, 0x1ff3b8, "5e210556aac25070659a913240992fc2b948cc4db59f1e26c09aaec053db957d"),
            (0x1ff3d8, 0x1ff430, "85e352f571e3c2bf6b91bfebe56c75997ad5b883e1d50399f71d3b017f957653"),
            (0x211830, 0x21186c, "8372d9143ec4a6f552e90ab55b5f5f13b4ce1a730a4a2c0d74137c9e28ca53e9"),
            (0x211690, 0x2116c0, "9fa107be2cf553506a4824666646ff2f6d127aaba5f00f3b7106b4d8c555b070"),
            (0x267650, 0x26769c, "074f409d9361fc61dc20d8bc2787049703c91ea8b83995132fa970fe1de482f5"),
            (0x2677bc, 0x267808, "27f2601101fbc3dc85ac4cdf98dfb9969946bd34e1dd7243933ea57f43297446"),
            (0x266e80, 0x266ec4, "23e4aad55566bf00908216119bc0b9c948b172ed8556e5c3a581acdc41c6c4f5"),
            (0x24edc8, 0x24eebc, "4c5c178e085a7376560435fb55b39695ac2d950ef788d4a6d204f2f6eb881330"),
            (0x22c2e4, 0x22c45c, "2079ab1e292c86369fed5917b3f80c7909e826594aa4e2de027d1942a7440ff5"),
            (0x17bdf0, 0x17be1c, "68d15fead0760b3f739711139add183bff7e5967b24790215224633c3ee97515"),
            (0x2232d4, 0x2233a4, "50838bf501ae62ba9e121f9c402c4179b47768f1b3b5ed7ddbf2291ce33e8077"),
            (0x22a720, 0x22a750, "fd3d668d8b3c19a6d72a78d11efb4d0ba62f0a289c39568ee611774054790607"),
        })
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(Read(start, end - start))).ToLowerInvariant());

        // Independently decode the literal instruction pairs used by the
        // manually traced predicates, rather than comparing two report copies.
        float Literal(int address) => BitConverter.Int32BitsToSingle(
            ((Word(address) & 0xffff) << 16) | ((Word(address + 4) >> 16) == 0x3421 ? Word(address + 4) & 0xffff : 0));
        Assert.Equal(Rac1CrouchAnimation.TurnEntryMagnitude, Literal(0x217c08));
        Assert.Equal(Rac1CrouchAnimation.TurnVelocityThreshold, Literal(0x217cc8) * (1f / 60f));
        Assert.Equal(Rac1CrouchAnimation.TurnRateDivisor, Literal(0x217d4c) * (1f / 60f));
        Assert.Equal(6.48f, Literal(0x217f78));
        Assert.Equal(0.001f, Literal(0x216740));
        Assert.Equal(Rac1OrdinaryStopAnimation.ReleaseInputThreshold, Literal(0x22b948));
        Assert.Equal(Rac1OrdinaryStopAnimation.LowMotionThreshold, Literal(0x22b968) * (1f / 60f));
        Assert.Equal(Rac1OrdinaryStopAnimation.HighMotionThreshold, Literal(0x22b998) * (1f / 60f));
        Assert.Equal(Rac1OrdinaryStopAnimation.RecentInputThreshold, Literal(0x22b9c4));
        Assert.Equal(Rac1OrdinaryStopAnimation.RestartInputThreshold, Literal(0x22c384));
        Assert.Equal(Rac1OrdinaryStopAnimation.MovingRestartInputThreshold, Literal(0x22c438));
        Assert.Equal(Rac1OrdinaryStopAnimation.IdleRestartInputThreshold, Literal(0x22a720));
        Assert.Equal(11, Word(0x15f6e0));
        for (int index = 0; index < 11; index++)
            Assert.Equal(Float(0x17bdf0 + index * 4), Rac1OrdinaryStopAnimation.StandingBlend[index]);
        Assert.Equal(0x0c000000 | (0x1ff500 >> 2), Word(0x213b28));
        Assert.Equal(0x0c000000 | (0x1ff398 >> 2), Word(0x213b34));
        Assert.Equal(0x0c000000 | (0x1ff500 >> 2), Word(0x213b54));
        Assert.Equal(0x0280282d, Word(0x213b58)); // move a1,s4: original request
        Assert.Equal(0x26030110, Word(0x223950)); // source P+0x110, distinct from P+0x100
        Assert.Equal(16, Word(0x15f6e4));
        for (int index = 0; index < 16; index++)
            Assert.Equal(Float(0x17be54 + index * 4), Rac1CrouchAnimation.StandUpBlend[index]);
    }

    [SkippableTheory]
    [InlineData(0, 0x245c28, 0x217f4c, 0x216710, 0x1ff068,
        "eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804")]
    [InlineData(12, 0x25de18, 0x238ed4, 0x2378a8, 0x221f78,
        "ffe8c2b5dcb8e6d02d131037472936a766cbf9aabeeeb809a17057fa69515622")]
    public void VeldinAndHovenDeriveIceFlagFromClassSevenAndUseTheSameCrouchBrake(
        int levelId, int entryPoint, int decay, int carry, int zeroFill, string overlayHash)
    {
        var Read = ReadOverlay(levelId, entryPoint, overlayHash);
        int Word(int address) => BinaryPrimitives.ReadInt32LittleEndian(Read(address, 4));
        float Literal(int address) => BitConverter.Int32BitsToSingle(
            ((Word(address) & 0xffff) << 16) | (Word(address + 4) & 0xffff));

        // This block is byte-identical despite different level-specific addresses.
        Assert.Equal("4d24b0a795c0c6bd58937b9cfcbe88ba6e65695d414c6e9e6a1b6135c63aa4cf",
            Convert.ToHexString(SHA256.HashData(Read(decay, 0x54))).ToLowerInvariant());
        Assert.Equal(unchecked((int)0x920212e2), Word(decay + 0x0c)); // lbu v0,P+0x12e2
        Assert.Equal(0x10400006, Word(decay + 0x10)); // zero flag takes normal branch
        Assert.Equal(1.7f, Literal(decay + 0x18));
        Assert.Equal(6.48f, Literal(decay + 0x2c));
        Assert.Equal(0x0c000000 | (carry >> 2), Word(decay + 0x78));
        Assert.Equal(0x4600a306, Word(decay + 0x7c)); // pass selected f20 as decay argument

        if (levelId == 0)
        {
            Assert.Equal(0x24050010, Word(0x20b96c)); // clear 16-byte flags
            Assert.Equal(0x262412e0, Word(0x20b984)); // start at P+0x12e0
            Assert.Equal(0x0c000000 | (zeroFill >> 2), Word(0x20b990));
            Assert.Equal(0x24040007, Word(0x20bb64)); // class 7
            Assert.Equal(0x16440005, Word(0x20bb74)); // other classes skip ice store
            Assert.Equal(0x24020001, Word(0x20bb80));
            Assert.Equal(unchecked((int)0xa06212e2), Word(0x20bb84));
        }
        else
        {
            Assert.Equal(0x24050010, Word(0x22de34));
            Assert.Equal(0x264412e0, Word(0x22de50));
            Assert.Equal(0x0c000000 | (zeroFill >> 2), Word(0x22de5c));
            Assert.Equal(0x24020007, Word(0x22df94));
            Assert.Equal(0x16620005, Word(0x22df98));
            Assert.Equal(0x24020001, Word(0x22dfa4));
            Assert.Equal(unchecked((int)0xa06212e2), Word(0x22dfa8));
        }
        // The clear helper writes zero in 16-byte chunks, including the ice flag.
        Assert.Equal(unchecked((int)0x7c800000), Word(zeroFill));
    }

    [Fact]
    public void WalkingDoesNotBecomeRunningWhenTimeOrClipCyclesPass()
    {
        for (int age = 0; age < 600; age++)
            Assert.Equal(3, Rac1OrdinaryLocomotionAnimation.SelectSequence(3, 0.015f, age, false));
    }

    [Fact]
    public void StrictThresholdsAndTheirAdjacentFloatsPreserveHysteresis()
    {
        float run = Rac1OrdinaryLocomotionAnimation.RunEntryStep;
        float walk = Rac1OrdinaryLocomotionAnimation.WalkReentryStep;
        Assert.Equal(3, Select(3, MathF.BitDecrement(run)));
        Assert.Equal(3, Select(3, run));
        Assert.Equal(4, Select(3, MathF.BitIncrement(run)));
        Assert.Equal(3, Select(4, MathF.BitDecrement(walk)));
        Assert.Equal(4, Select(4, walk));
        Assert.Equal(4, Select(4, MathF.BitIncrement(walk)));
        Assert.Equal(3, Select(3, 2f / 60f));
        Assert.Equal(4, Select(4, 2f / 60f));
    }

    [Fact]
    public void SubstateAgeAndEntryGuardBlockBothDirectionsWithoutStoppingRate()
    {
        foreach (int age in new[] { 0, 4 })
        {
            Assert.Equal(3, Rac1OrdinaryLocomotionAnimation.SelectSequence(3, 0.095f, age, false));
            Assert.Equal(4, Rac1OrdinaryLocomotionAnimation.SelectSequence(4, 0.015f, age, false));
        }
        Assert.Equal(3, Rac1OrdinaryLocomotionAnimation.SelectSequence(3, 0.095f, 5, true));
        Assert.Equal(4, Rac1OrdinaryLocomotionAnimation.SelectSequence(4, 0.015f, 5, true));
        Assert.Equal(4, Select(3, 0.095f));
        Assert.Equal(3, Select(4, 0.015f));
        Assert.Equal(4f, Rac1OrdinaryLocomotionAnimation.PlaybackMultiplier(3, 0.095f));
    }

    [Theory]
    [InlineData(3, 0f, 0.6f)]
    [InlineData(3, 0.015f, 2.355f)]
    [InlineData(3, 0.04f, 4f)]
    [InlineData(4, 0.015f, 0.6f)]
    [InlineData(4, 0.095f, 1.33f)]
    [InlineData(4, 0.16f, 2.2f)]
    public void PlaybackRateScalesTheAssetRateAndClamps(int sequence, float speed, float expected)
    {
        Assert.Equal(expected, Rac1OrdinaryLocomotionAnimation.PlaybackMultiplier(sequence, speed), 5);
    }

    [Theory]
    [InlineData(3, 0, 33, 4, 23, 1)]
    [InlineData(3, 16, 33, 4, 23, 12)]
    [InlineData(3, 32, 33, 4, 23, 0)]
    [InlineData(4, 0, 23, 3, 33, 5)]
    [InlineData(4, 11, 23, 3, 33, 20)]
    [InlineData(4, 22, 23, 3, 33, 3)]
    public void SequenceChangeUsesIntegerNextFramePhaseAndDirectionalOffset(
        int previous, int frame, int previousCount, int next, int nextCount, int expected)
    {
        Assert.Equal(expected, Rac1OrdinaryLocomotionAnimation.TransitionNextFrame(
            previous, frame, previousCount, next, nextCount));
    }

    [Fact]
    public void EntryFramesAreDifferentFromThePhaseMappedTransitionFrames()
    {
        Assert.Equal(8, Rac1OrdinaryLocomotionAnimation.EntryNextFrame(3));
        Assert.Equal(0, Rac1OrdinaryLocomotionAnimation.EntryNextFrame(4));
    }

    [Fact]
    public void RejectsInputsOutsideTheRecoveredContract()
    {
        foreach (float speed in new[] { -0.01f, float.NaN, float.PositiveInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => Select(3, speed));
        Assert.Throws<ArgumentOutOfRangeException>(() => Select(7, 0.015f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Select(3, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1OrdinaryLocomotionAnimation.SelectSequence(3, 0.015f, -1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1OrdinaryLocomotionAnimation.TransitionNextFrame(3, 33, 33, 4, 23));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1OrdinaryLocomotionAnimation.TransitionNextFrame(3, 0, 0, 4, 23));
    }

    private static int Select(int sequence, float speed) =>
        Rac1OrdinaryLocomotionAnimation.SelectSequence(sequence, speed, 5, false);

    private static Func<int, int, byte[]> ReadOverlay(int levelId, int entryPoint, string expectedHash)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == levelId);
        var outer = level.CoreRanges[0];
        var data = new SubRangeReader(reader, outer.OffsetBytes, outer.SizeBytes, "level data");
        var directory = Rac1LevelData.ReadDirectory(data);
        var range = Rac1LevelData.OpenRange(data, directory, 0);
        byte[] payload = range.Read(0, checked((int)range.Length));
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());

        // Independently map every serialized section, including type-8 payloads.
        var sections = new List<(int Address, int Offset, int Size)>();
        for (int offset = 0; offset < payload.Length;)
        {
            int address = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
            int size = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4));
            Assert.Equal(entryPoint, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 12)));
            sections.Add((address, offset + 16, size));
            offset += 16 + size;
        }
        Assert.Equal(7, sections.Count);
        return (address, size) =>
        {
            var section = sections.Single(s => address >= s.Address && address + size <= s.Address + s.Size);
            return payload.AsSpan(section.Offset + address - section.Address, size).ToArray();
        };
    }
}
