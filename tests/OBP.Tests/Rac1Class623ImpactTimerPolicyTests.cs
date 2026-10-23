using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1Class623ImpactTimerPolicyTests
{
    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 0, 2)]
    [InlineData(2, 1, 0)]
    [InlineData(3, 2, 0)]
    [InlineData(32767, 32766, 0)]
    [InlineData(-1, 0, 2)]
    [InlineData(-32768, 0, 2)]
    public void NativeHalfwordCountdownHasDistinctEmptyExpiredAndActiveResults(
        short before, short after, int callbackResult)
    {
        Assert.Equal(new Rac1ImpactCountdownResult(after, callbackResult),
            Rac1Class623ImpactTimerPolicy.Tick(before));
    }

    [Theory]
    [InlineData(0, 37)]
    [InlineData(1, 15)]
    [InlineData(2, 30)]
    [InlineData(3, 30)]
    [InlineData(4, 60)]
    [InlineData(5, 30)]
    [InlineData(6, 30)]
    [InlineData(8, 30)]
    [InlineData(9, 30)]
    [InlineData(10, 30)]
    [InlineData(11, 60)]
    [InlineData(12, 30)]
    [InlineData(17, 30)]
    public void SecondNativeDispatchSelectsDurationOrDefault(int responseIndex, int duration)
    {
        Assert.Equal(duration,
            Rac1Class623ImpactTimerPolicy.DurationForResponseIndex(responseIndex, null));
    }

    [Fact]
    public void SpecialDurationRequiresActualNativeGlobalFlag()
    {
        Assert.Null(Rac1Class623ImpactTimerPolicy.DurationForResponseIndex(7, null));
        Assert.Equal(45, Rac1Class623ImpactTimerPolicy.DurationForResponseIndex(7, true));
        Assert.Equal(60, Rac1Class623ImpactTimerPolicy.DurationForResponseIndex(7, false));
    }

    [Theory]
    [InlineData(0, 0, 1, 11, true)]
    [InlineData(0, 0x10, 1, 0, true)]
    [InlineData(0, 0x20, 2, 11, true)]
    [InlineData(0, 0x30, 2, 0, true)]
    [InlineData(1, 0x30, 2, 1, true)]
    [InlineData(4, 0, 1, 11, true)]
    [InlineData(4, 1, 1, 4, true)]
    [InlineData(4, 2, 2, 11, true)]
    [InlineData(4, 3, 2, 4, true)]
    [InlineData(7, 0, 1, 11, true)]
    [InlineData(7, 4, 1, 7, true)]
    [InlineData(7, 8, 2, 11, true)]
    [InlineData(7, 12, 2, 7, true)]
    [InlineData(2, 0, 2, 13, true)]
    [InlineData(3, 0, 2, 13, true)]
    [InlineData(5, 0, 2, 13, true)]
    [InlineData(6, 0, 2, 13, true)]
    [InlineData(8, 0, 2, 13, true)]
    [InlineData(9, 0, 2, 13, true)]
    [InlineData(10, 0, 2, 13, true)]
    [InlineData(11, 0, 2, 12, false)]
    [InlineData(11, 0xffff, 2, 12, false)]
    [InlineData(12, 0, 2, 13, true)]
    [InlineData(17, 0, 2, 13, true)]
    [InlineData(-1, 0, 2, 13, true)]
    public void ActiveCountdownSecondTableAppliesOriginalCollisionMask(
        int initialIndex, uint collisionFlags, int expectedStatus, int expectedIndex, bool clearsScalar)
    {
        Assert.Equal(new Rac1ActiveImpactTimerResult(expectedStatus, expectedIndex, clearsScalar),
            Rac1Class623ImpactTimerPolicy.ResolveActiveCountdown(
                nativeStatus: 2, responseIndex: initialIndex, collisionFlags));
    }

    [Fact]
    public void ActiveCountdownPreservesExistingNonTwoStatusWhenMaskAllowsIt()
    {
        Assert.Equal(new Rac1ActiveImpactTimerResult(4, 4, true),
            Rac1Class623ImpactTimerPolicy.ResolveActiveCountdown(4, 4, 3));
        Assert.Equal(new Rac1ActiveImpactTimerResult(3, 11, true),
            Rac1Class623ImpactTimerPolicy.ResolveActiveCountdown(3, 4, 2));
        Assert.Equal(new Rac1ActiveImpactTimerResult(3, 13, true),
            Rac1Class623ImpactTimerPolicy.ResolveActiveCountdown(3, 8, 0xff));
        Assert.Equal(new Rac1ActiveImpactTimerResult(3, 12, false),
            Rac1Class623ImpactTimerPolicy.ResolveActiveCountdown(3, 11, 0xff));
    }

    [SkippableFact]
    public void RetailActiveCountdownTableAndMaskWritesMatchOriginalExecutable()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var overlay = Rac1ExecutableOverlay.Read(reader, level);
        static uint Word(Rac1ExecutableOverlay o, int address) =>
            BinaryPrimitives.ReadUInt32LittleEndian(o.Read(address, 4));

        Assert.Equal(0x1040001cu, Word(overlay, 0x00284ba8)); // response outside table
        Assert.Equal(0x244276a0u, Word(overlay, 0x00284bb0)); // 0x2176a0
        Assert.Equal(0x8c430000u, Word(overlay, 0x00284bb8)); // pointer
        uint[] expectedTargets =
        [
            0x00284bc4, 0x00284bc4, 0x00284c1c, 0x00284c1c,
            0x00284bd8, 0x00284c1c, 0x00284c1c, 0x00284bec,
            0x00284c1c, 0x00284c1c, 0x00284c1c, 0x00284c14
        ];
        for (int i = 0; i < expectedTargets.Length; i++)
            Assert.Equal(expectedTargets[i], Word(overlay, 0x002176a0 + i * 4));

        Assert.Equal(0x33c20010u, Word(overlay, 0x00284bc4)); // index 0/1 primary
        Assert.Equal(0x0062880au, Word(overlay, 0x00284bcc)); // MOVZ response=11
        Assert.Equal(0x33c40020u, Word(overlay, 0x00284bd4)); // index 0/1 secondary
        Assert.Equal(0x33c20001u, Word(overlay, 0x00284bd8)); // index 4 primary
        Assert.Equal(0x33c40002u, Word(overlay, 0x00284be8)); // index 4 secondary
        Assert.Equal(0x33c20004u, Word(overlay, 0x00284bec)); // index 7 primary
        Assert.Equal(0x33c40008u, Word(overlay, 0x00284bf8)); // index 7 secondary
        Assert.Equal(0x54800009u, Word(overlay, 0x00284bfc)); // BNEL flag
        Assert.Equal(0x24020001u, Word(overlay, 0x00284c04)); // status 1
        Assert.Equal(0xae620000u, Word(overlay, 0x00284c08)); // store status
        Assert.Equal(0x2411000cu, Word(overlay, 0x00284c18)); // index 11 -> 12
        Assert.Equal(0xae80002cu, Word(overlay, 0x00284c1c)); // clear scalar
        Assert.Equal(0x2411000du, Word(overlay, 0x00284c20)); // other -> 13
        Assert.Equal(0xa6560016u, Word(overlay, 0x00284c24)); // clear ID +0x16
        Assert.Equal(0xa6560014u, Word(overlay, 0x00284c28)); // clear ID +0x14
    }

    [Fact]
    public void CountdownCannotBeMistakenForRealImpactAdmission()
    {
        var pending = Rac1Class623ImpactTimerPolicy.Tick(1);
        Assert.Equal(2, pending.CallbackResult);
        // Status/result of the timer does not supply native 0x284730's
        // final collision status, scalar or return response.
        var targets = new Rac1Class623ActivationSession();
        byte[] pvar = new byte[Rac1AmoeboidClass623Admission.PVarSize];
        BinaryPrimitives.WriteInt64LittleEndian(pvar.AsSpan(0x2b8), -1);
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0x20), 1f);
        targets.Register(1009, 623, pvar);
        Assert.False(targets.ApplyObservedNativeImpact(1009, null, null).Applied);
        Assert.Equal(new Rac1Class623InitialTargetState(1, true), targets.Get(1009));
        Assert.Equal(1f, targets.Health(1009));
    }

    [SkippableFact]
    public void RetailCountdownAndSecondDispatchMatchOriginalOverlay()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var overlay = Rac1ExecutableOverlay.Read(reader, level);
        static uint Word(Rac1ExecutableOverlay o, int address) =>
            BinaryPrimitives.ReadUInt32LittleEndian(o.Read(address, 4));

        Assert.Equal(0x84810000u, Word(overlay, 0x0022dc50)); // signed halfword
        Assert.Equal(0x20020001u, Word(overlay, 0x0022dc54)); // empty result 1
        Assert.Equal(0x702208c8u, Word(overlay, 0x0022dc5c)); // PMAXW to one
        Assert.Equal(0x2021ffffu, Word(overlay, 0x0022dc60)); // decrement
        Assert.Equal(0x20020000u, Word(overlay, 0x0022dc64)); // active result 0
        Assert.Equal(0xa4810000u, Word(overlay, 0x0022dc6c)); // store halfword
        Assert.Equal(0x20020002u, Word(overlay, 0x0022dc70)); // expired result 2

        Assert.Equal(0x86440014u, Word(overlay, 0x00284b1c)); // prior linked ID
        Assert.Equal(0x10960002u, Word(overlay, 0x00284b24)); // source equal
        Assert.Equal(0xa640001cu, Word(overlay, 0x00284b2c)); // reset if changed
        Assert.Equal(0x0c08b714u, Word(overlay, 0x00284b30)); // timer callback
        Assert.Equal(0x2644001cu, Word(overlay, 0x00284b34)); // slot +0x1c
        Assert.Equal(0x1040001bu, Word(overlay, 0x00284b38)); // result 0 branch
        Assert.Equal(0x2ea2000cu, Word(overlay, 0x00284b3c)); // index < 12
        Assert.Equal(0x24427670u, Word(overlay, 0x00284b48)); // table at 0x217670
        Assert.Equal(0x00600008u, Word(overlay, 0x00284b54)); // jump table
        uint[] durationEntries =
        [
            0x00284b5c, 0x00284b64, 0x00284b94, 0x00284b94,
            0x00284b6c, 0x00284b94, 0x00284b94, 0x00284b74,
            0x00284b94, 0x00284b94, 0x00284b94, 0x00284b8c
        ];
        for (int i = 0; i < durationEntries.Length; i++)
            Assert.Equal(durationEntries[i], Word(overlay, 0x00217670 + i * 4));

        Assert.Equal(0x24040025u, Word(overlay, 0x00284b60)); // 37
        Assert.Equal(0x2404000fu, Word(overlay, 0x00284b68)); // 15
        Assert.Equal(0x2404003cu, Word(overlay, 0x00284b70)); // 60
        Assert.Equal(0x9043e533u, Word(overlay, 0x00284b78)); // flag at 0x13e533
        Assert.Equal(0x2404002du, Word(overlay, 0x00284b80)); // 45
        Assert.Equal(0x2404003cu, Word(overlay, 0x00284b88)); // 60
        Assert.Equal(0x2404003cu, Word(overlay, 0x00284b90)); // 60
        Assert.Equal(0x2404001eu, Word(overlay, 0x00284b94)); // 30
        Assert.Equal(0x0c08b6f6u, Word(overlay, 0x00284b98)); // duration allocator
        Assert.Equal(0xa642001cu, Word(overlay, 0x00284ba4)); // slot +0x1c write
        // On zero callback result the code takes another response path,
        // *not* this duration table. Neither implies hit/damage admission.
        Assert.Equal(0x244276a0u, Word(overlay, 0x00284bb0));
        Assert.Equal(0x8c430000u, Word(overlay, 0x00284bb8));
    }
}
