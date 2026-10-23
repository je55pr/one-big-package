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
