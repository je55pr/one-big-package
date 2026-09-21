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
    public void LocomotionBoundaryDoesNotBorrowRac1Roles()
    {
        Assert.Equal(0, GcRatchetSequenceSelection.DefaultIdleSequenceId);
        Assert.Equal(3, GcRatchetSequenceSelection.WalkSequenceId);
        Assert.Equal(
            GcRatchetSequenceSelection.WalkSequenceId,
            GcRatchetSequenceSelection.SustainedWalkSequenceId);
        Assert.Equal(7, GcRatchetSequenceSelection.JumpSequenceId);
        Assert.Equal(
            GcRatchetSequenceSelection.JumpSequenceId,
            GcRatchetSequenceSelection.JumpLaunchSequenceId);
        Assert.Equal(19, GcRatchetSequenceSelection.GlideSequenceId);
        Assert.True(GcRatchetSequenceSelection.TargetingPreservesContextSequence);
        Assert.Null(GcRatchetSequenceSelection.TargetingDirectionalSequenceId);
        Assert.True(GcRatchetSequenceSelection.GunWaitingPreservesContextSequence);
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
            [7] = 29,
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

        const uint stateUpdateJumpTableAddress = 0x002A1CE0;
        var updateTable = overlay.ReadVirtual(stateUpdateJumpTableAddress, 147 * sizeof(uint));
        uint Update(int state) => BinaryPrimitives.ReadUInt32LittleEndian(
            updateTable.AsSpan(state * sizeof(uint), sizeof(uint)));

        Assert.Equal(0x002BBCDCu, Update(GcRatchetSequenceSelection.WalkState));
        Assert.Equal(0x002BA23Cu, Update(GcRatchetSequenceSelection.CrouchState));
        Assert.Equal(0x002BA23Cu, Update(GcRatchetSequenceSelection.TargetingState));
        Assert.Equal(0x002BA23Cu, Update(GcRatchetSequenceSelection.GunWaitingState));

        bool Calls(uint start, uint end, uint target)
        {
            var code = overlay.ReadVirtual(start, checked((int)(end - start)));
            for (int offset = 0; offset + sizeof(uint) <= code.Length; offset += sizeof(uint))
            {
                uint word = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset, sizeof(uint)));
                if ((word >> 26) != 3)
                {
                    continue;
                }

                uint pc = start + (uint)offset;
                uint actual = ((pc + 4) & 0xF0000000u) | ((word & 0x03FFFFFFu) << 2);
                if (actual == target)
                {
                    return true;
                }
            }

            return false;
        }

        uint[] sequenceSetters = [0x002C7928u, 0x002C7B80u, 0x00310060u, 0x00310138u];
        foreach (uint setter in sequenceSetters)
        {
            Assert.False(Calls(0x002BBCDCu, 0x002BC670u, setter));
            Assert.False(Calls(0x002BA294u, 0x002BA394u, setter));
            Assert.False(Calls(0x002BA524u, 0x002BA790u, setter));
        }

        uint VirtualWord(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(
            overlay.ReadVirtual(address, sizeof(uint)));
        Assert.Equal(0x24020004u, VirtualWord(0x002BA394u));
        Assert.Equal(0x54620062u, VirtualWord(0x002BA398u));

        Assert.Equal(0x24040007u, VirtualWord(0x002C696Cu));
        Assert.Equal(0x0C0B1E4Au, VirtualWord(0x002C6974u));
        Assert.Equal(0x24020007u, VirtualWord(0x002C697Cu));
        Assert.Equal(0xAE022294u, VirtualWord(0x002C6980u));

        var sequenceWriters = new List<uint>();
        foreach (var section in overlay.Sections)
        {
            var sectionBytes = overlay.Raw.AsSpan(section.DataOffset, section.CopySize);
            for (int offset = 0; offset + sizeof(uint) <= sectionBytes.Length; offset += sizeof(uint))
            {
                uint word = BinaryPrimitives.ReadUInt32LittleEndian(sectionBytes.Slice(offset, sizeof(uint)));
                if ((word >> 26) == 0x28 && (word & 0xFFFFu) == 0x43u)
                {
                    sequenceWriters.Add(section.DestinationAddress + (uint)offset);
                }
            }
        }

        Assert.Equal(new uint[]
        {
            0x002C7A08u, 0x002C7BD8u, 0x002D1354u, 0x002FA340u,
            0x003100CCu, 0x00310240u, 0x003103B0u, 0x00318C50u,
            0x00358A08u, 0x00358B60u, 0x0035B840u, 0x0036F938u,
            0x00370634u, 0x00370744u, 0x003815D4u,
        }, sequenceWriters.OrderBy(x => x).ToArray());
    }
}
