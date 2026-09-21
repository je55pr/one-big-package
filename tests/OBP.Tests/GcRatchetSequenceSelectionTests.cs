using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using OBP.RAC2.Player;

namespace OBP.Tests;

public sealed class GcRatchetSequenceSelectionTests
{
    [Fact]
    public void StateNumbersStayGcSpecific()
    {
        Assert.Equal(0, GcRatchetSequenceSelection.IdleState);
        Assert.Equal(2, GcRatchetSequenceSelection.WalkState);
        Assert.Equal(3, GcRatchetSequenceSelection.SkidState);
        Assert.Equal(4, GcRatchetSequenceSelection.CrouchState);
        Assert.Equal(6, GcRatchetSequenceSelection.FallState);
        Assert.Equal(7, GcRatchetSequenceSelection.JumpState);
        Assert.Equal(8, GcRatchetSequenceSelection.GlideState);
        Assert.Equal(19, GcRatchetSequenceSelection.ComboAttackState);
        Assert.Equal(20, GcRatchetSequenceSelection.JumpAttackState);
        Assert.Equal(21, GcRatchetSequenceSelection.ThrowAttackState);
        Assert.Equal(22, GcRatchetSequenceSelection.GetHitState);
        Assert.Equal(29, GcRatchetSequenceSelection.TargetingState);
        Assert.Equal(30, GcRatchetSequenceSelection.GunWaitingState);
        Assert.Equal(57, GcRatchetSequenceSelection.DeathState);
    }

    [Theory]
    [InlineData(false, 0f, 6)]
    [InlineData(true, 3f, 6)]
    [InlineData(true, 3.0001f, 5)]
    [InlineData(true, 15.999f, 5)]
    [InlineData(true, 16f, 6)]
    public void SkidSelectorPreservesStrictRetailWindow(
        bool modeIsFour,
        float nativeC38,
        int expected)
    {
        Assert.Equal(
            expected,
            GcRatchetSequenceSelection.SelectSkidSequence(modeIsFour, nativeC38));
    }

    [Theory]
    [InlineData(-1f, 15)]
    [InlineData(-0.001f, 15)]
    [InlineData(0f, 14)]
    [InlineData(1f, 14)]
    public void DirectionalCrouchSelectorPreservesNativeSign(
        float nativeAxis1A4,
        int expected)
    {
        Assert.Equal(
            expected,
            GcRatchetSequenceSelection.SelectDirectionalCrouchSequence(nativeAxis1A4));
    }

    [Theory]
    [InlineData(1.749f, 10)]
    [InlineData(1.75f, 10)]
    [InlineData(1.751f, 11)]
    public void FallSelectorPreservesStrictRetailThreshold(float native31C, int expected)
    {
        Assert.Equal(
            expected,
            GcRatchetSequenceSelection.SelectFallSequence(native31C));
    }

    [Theory]
    [InlineData(0, 23)]
    [InlineData(1, 24)]
    [InlineData(2, 25)]
    public void ComboSelectorUsesRecoveredModuloThreeStage(int stage, int expected)
    {
        Assert.Equal(expected, GcRatchetSequenceSelection.SelectComboAttackSequence(stage));
    }

    [Fact]
    public void FixedCombatAndDeathSelectorsRemainDistinct()
    {
        Assert.Equal(43, GcRatchetSequenceSelection.JumpAttackSequenceId);
        Assert.Equal(26, GcRatchetSequenceSelection.ThrowAttackSequenceId);
        Assert.Equal(16, GcRatchetSequenceSelection.GetHitSequenceId);
        Assert.Equal(69, GcRatchetSequenceSelection.DeathSequenceId);
    }

    [Fact]
    public void UnresolvedSelectorsDoNotBorrowRac1Roles()
    {
        Assert.Equal(0, GcRatchetSequenceSelection.DefaultIdleSequenceId);
        Assert.Equal(3, GcRatchetSequenceSelection.WalkEntrySequenceId);
        Assert.Equal(19, GcRatchetSequenceSelection.GlideSequenceId);
        Assert.Null(GcRatchetSequenceSelection.SustainedWalkSequenceId);
        Assert.Null(GcRatchetSequenceSelection.JumpLaunchSequenceId);
        Assert.Null(GcRatchetSequenceSelection.TargetingDirectionalSequenceId);
        Assert.Null(GcRatchetSequenceSelection.GunWaitingSequenceId);
    }

    [SkippableFact]
    public void RetailOozlaStateJumpTableAndRatchetSequenceSlotsMatchRecoveredContract()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile("/G/LEVEL1.WAD")
            ?? throw new FileNotFoundException("/G/LEVEL1.WAD");
        var header = GcLevelWad.ReadHeader(wad);
        var data = GcLevelWad.RequireLump(wad, header, 0);
        var overlay = GcLevelOverlay.Open(data);
        var core = GcLevelCore.Open(data);

        const uint stateInitJumpTableAddress = 0x002A1F30;
        var jumpTable = overlay.ReadVirtual(stateInitJumpTableAddress, 148 * sizeof(uint));

        uint Entry(int state) =>
            BinaryPrimitives.ReadUInt32LittleEndian(
                jumpTable.AsSpan(state * sizeof(uint), sizeof(uint)));

        Assert.Equal(0x002BED6Cu, Entry(GcRatchetSequenceSelection.IdleState));
        Assert.Equal(0x002BF010u, Entry(GcRatchetSequenceSelection.WalkState));
        Assert.Equal(0x002BF338u, Entry(GcRatchetSequenceSelection.SkidState));
        Assert.Equal(0x002BF48Cu, Entry(GcRatchetSequenceSelection.CrouchState));
        Assert.Equal(0x002BF718u, Entry(GcRatchetSequenceSelection.FallState));
        Assert.Equal(0x002BF990u, Entry(GcRatchetSequenceSelection.JumpState));
        Assert.Equal(0x002BF91Cu, Entry(GcRatchetSequenceSelection.GlideState));
        Assert.Equal(0x002C0A48u, Entry(GcRatchetSequenceSelection.ComboAttackState));
        Assert.Equal(0x002C0A48u, Entry(GcRatchetSequenceSelection.JumpAttackState));
        Assert.Equal(0x002C0A48u, Entry(GcRatchetSequenceSelection.ThrowAttackState));
        Assert.Equal(0x002C126Cu, Entry(GcRatchetSequenceSelection.GetHitState));
        Assert.Equal(0x002C165Cu, Entry(GcRatchetSequenceSelection.TargetingState));
        Assert.Equal(0x002C16A4u, Entry(GcRatchetSequenceSelection.GunWaitingState));
        Assert.Equal(0x002BEEE0u, Entry(GcRatchetSequenceSelection.DeathState));

        Assert.Equal(0x80E0, core.Header.RatchetSeqsOffset);

        int SequenceOffset(int sequence) =>
            BinaryPrimitives.ReadInt32LittleEndian(
                core.Index.AsSpan(
                    core.Header.RatchetSeqsOffset + sequence * sizeof(int),
                    sizeof(int)));

        int FrameCount(int sequence)
        {
            int offset = SequenceOffset(sequence);
            Assert.InRange(offset, 1, core.Assets.Length - 0x11);
            return core.Assets[offset + 0x10];
        }

        int populated = 0;
        for (int sequence = 0; sequence < 256; sequence++)
        {
            if (SequenceOffset(sequence) > 0)
            {
                populated++;
            }
        }
        Assert.Equal(102, populated);

        var expectedFrames = new Dictionary<int, int>
        {
            [0] = 10,
            [3] = 33,
            [5] = 13,
            [6] = 13,
            [10] = 1,
            [11] = 6,
            [13] = 15,
            [14] = 7,
            [15] = 7,
            [16] = 18,
            [19] = 9,
            [23] = 21,
            [24] = 22,
            [25] = 35,
            [26] = 45,
            [43] = 13,
            [69] = 25,
        };

        foreach (var (sequence, frameCount) in expectedFrames)
        {
            Assert.Equal(frameCount, FrameCount(sequence));
        }
    }
}
