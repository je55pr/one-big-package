using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using OBP.RAC2.Player;

namespace OBP.Tests;

public sealed class Rac2RatchetSequenceSelectionTests
{
    [Fact]
    public void StateNumbersStayRac2Specific()
    {
        Assert.Equal(0, Rac2RatchetSequenceSelection.IdleState);
        Assert.Equal(2, Rac2RatchetSequenceSelection.WalkState);
        Assert.Equal(3, Rac2RatchetSequenceSelection.SkidState);
        Assert.Equal(4, Rac2RatchetSequenceSelection.CrouchState);
        Assert.Equal(6, Rac2RatchetSequenceSelection.FallState);
        Assert.Equal(7, Rac2RatchetSequenceSelection.JumpState);
        Assert.Equal(8, Rac2RatchetSequenceSelection.GlideState);
        Assert.Equal(19, Rac2RatchetSequenceSelection.ComboAttackState);
        Assert.Equal(20, Rac2RatchetSequenceSelection.JumpAttackState);
        Assert.Equal(21, Rac2RatchetSequenceSelection.ThrowAttackState);
        Assert.Equal(22, Rac2RatchetSequenceSelection.GetHitState);
        Assert.Equal(29, Rac2RatchetSequenceSelection.TargetingState);
        Assert.Equal(30, Rac2RatchetSequenceSelection.GunWaitingState);
        Assert.Equal(57, Rac2RatchetSequenceSelection.DeathState);
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
            Rac2RatchetSequenceSelection.SelectSkidSequence(modeIsFour, nativeC38));
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
            Rac2RatchetSequenceSelection.SelectDirectionalCrouchSequence(nativeAxis1A4));
    }

    [Theory]
    [InlineData(1.749f, 10)]
    [InlineData(1.75f, 10)]
    [InlineData(1.751f, 11)]
    public void FallSelectorPreservesStrictRetailThreshold(float native31C, int expected)
    {
        Assert.Equal(
            expected,
            Rac2RatchetSequenceSelection.SelectFallSequence(native31C));
    }

    [Theory]
    [InlineData(0, 23)]
    [InlineData(1, 24)]
    [InlineData(2, 25)]
    public void ComboSelectorUsesRecoveredModuloThreeStage(int stage, int expected)
    {
        Assert.Equal(expected, Rac2RatchetSequenceSelection.SelectComboAttackSequence(stage));
    }

    [Fact]
    public void FixedCombatAndDeathSelectorsRemainDistinct()
    {
        Assert.Equal(43, Rac2RatchetSequenceSelection.JumpAttackSequenceId);
        Assert.Equal(26, Rac2RatchetSequenceSelection.ThrowAttackSequenceId);
        Assert.Equal(16, Rac2RatchetSequenceSelection.GetHitSequenceId);
        Assert.Equal(69, Rac2RatchetSequenceSelection.DeathSequenceId);
    }

    [Fact]
    public void LocomotionBoundaryDoesNotBorrowRac1Roles()
    {
        Assert.Equal(0, Rac2RatchetSequenceSelection.DefaultIdleSequenceId);
        Assert.Equal(3, Rac2RatchetSequenceSelection.WalkSequenceId);
        Assert.Equal(
            Rac2RatchetSequenceSelection.WalkSequenceId,
            Rac2RatchetSequenceSelection.SustainedWalkSequenceId);
        Assert.Equal(7, Rac2RatchetSequenceSelection.JumpSequenceId);
        Assert.Equal(
            Rac2RatchetSequenceSelection.JumpSequenceId,
            Rac2RatchetSequenceSelection.JumpLaunchSequenceId);
        Assert.Equal(19, Rac2RatchetSequenceSelection.GlideSequenceId);
        Assert.True(Rac2RatchetSequenceSelection.TargetingPreservesContextSequence);
        Assert.Null(Rac2RatchetSequenceSelection.TargetingDirectionalSequenceId);
        Assert.True(Rac2RatchetSequenceSelection.GunWaitingPreservesContextSequence);
        Assert.Null(Rac2RatchetSequenceSelection.GunWaitingSequenceId);
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
        var header = Rac2LevelWad.ReadHeader(wad);
        var data = Rac2LevelWad.RequireLump(wad, header, 0);
        var overlay = Rac2LevelOverlay.Open(data);
        var core = Rac2LevelCore.Open(data);

        const uint stateInitJumpTableAddress = 0x002A1F30;
        var jumpTable = overlay.ReadVirtual(stateInitJumpTableAddress, 148 * sizeof(uint));

        uint Entry(int state) =>
            BinaryPrimitives.ReadUInt32LittleEndian(
                jumpTable.AsSpan(state * sizeof(uint), sizeof(uint)));

        Assert.Equal(0x002BED6Cu, Entry(Rac2RatchetSequenceSelection.IdleState));
        Assert.Equal(0x002BF010u, Entry(Rac2RatchetSequenceSelection.WalkState));
        Assert.Equal(0x002BF338u, Entry(Rac2RatchetSequenceSelection.SkidState));
        Assert.Equal(0x002BF48Cu, Entry(Rac2RatchetSequenceSelection.CrouchState));
        Assert.Equal(0x002BF718u, Entry(Rac2RatchetSequenceSelection.FallState));
        Assert.Equal(0x002BF990u, Entry(Rac2RatchetSequenceSelection.JumpState));
        Assert.Equal(0x002BF91Cu, Entry(Rac2RatchetSequenceSelection.GlideState));
        Assert.Equal(0x002C0A48u, Entry(Rac2RatchetSequenceSelection.ComboAttackState));
        Assert.Equal(0x002C0A48u, Entry(Rac2RatchetSequenceSelection.JumpAttackState));
        Assert.Equal(0x002C0A48u, Entry(Rac2RatchetSequenceSelection.ThrowAttackState));
        Assert.Equal(0x002C126Cu, Entry(Rac2RatchetSequenceSelection.GetHitState));
        Assert.Equal(0x002C165Cu, Entry(Rac2RatchetSequenceSelection.TargetingState));
        Assert.Equal(0x002C16A4u, Entry(Rac2RatchetSequenceSelection.GunWaitingState));
        Assert.Equal(0x002BEEE0u, Entry(Rac2RatchetSequenceSelection.DeathState));

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

        Assert.Equal(0x002BBCDCu, Update(Rac2RatchetSequenceSelection.WalkState));
        Assert.Equal(0x002BA23Cu, Update(Rac2RatchetSequenceSelection.CrouchState));
        Assert.Equal(0x002BA23Cu, Update(Rac2RatchetSequenceSelection.TargetingState));
        Assert.Equal(0x002BA23Cu, Update(Rac2RatchetSequenceSelection.GunWaitingState));

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
