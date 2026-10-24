using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1Class623ImpactModeClassifierTests
{
    [Theory]
    [InlineData(0, 0, 1, 2, 0, false)]
    [InlineData(0, 5, 2, 2, 0, false)]
    [InlineData(0, 0, 3, 2, 1, false)]
    [InlineData(1, 0, 0, 2, 11, false)]
    [InlineData(1, 1, 0, 2, 11, false)]
    [InlineData(1, 2, 0, 2, 5, false)]
    [InlineData(1, 255, 0, 2, 9, false)]
    [InlineData(2, 0, 0, 2, 8, false)]
    [InlineData(2, 2, 0, 2, 13, false)]
    [InlineData(3, 1, 0, 2, 10, false)]
    [InlineData(3, 2, 0, 2, 15, false)]
    [InlineData(3, 3, 0, 2, 16, true)]
    [InlineData(4, 0, 0, 2, 14, false)]
    [InlineData(5, 0, 0, 3, 4, false)]
    [InlineData(5, 2, 0, 4, 7, false)]
    [InlineData(6, 0, 0, 2, 1, false)]
    [InlineData(7, 0, 0, 2, 12, false)]
    [InlineData(7, 2, 0, 2, 17, false)]
    [InlineData(8, 0, 0, 2, 1, false)]
    [InlineData(255, 255, 255, 2, 1, false)]
    public void TableModeProposesNativeStatusAndIndexOnly(
        byte mode, byte submode, ushort sourceKind, int status, int index, bool reset)
    {
        Assert.Equal(new Rac1ProvisionalImpactClassification(status, index, reset),
            Rac1Class623ImpactModeClassifier.Classify(mode, submode, sourceKind));
    }

    [Fact]
    public void ProvisionalTableIndexMustNotBeUsedAsNativeReturnClassification()
    {
        // The general native service can subsequently change the return
        // index based on callback/control-slot results. No direct bridge
        // from provisional table classification to terminal state exists.
        var proposal = Rac1Class623ImpactModeClassifier.Classify(6, 0, 0);
        Assert.Equal(1, proposal.ResponseIndex);
        var targets = new Rac1Class623ActivationSession();
        byte[] pvar = new byte[Rac1AmoeboidClass623Admission.PVarSize];
        BinaryPrimitives.WriteInt64LittleEndian(pvar.AsSpan(0x2b8), -1);
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0x20), 1f);
        targets.Register(1009, 623, pvar);
        // No final native response index was supplied. The provisional
        // index is deliberately NOT passed as the final response index.
        var outcome = targets.ApplyObservedNativeImpact(1009, 0, 0.125f);
        Assert.True(outcome.Applied);
        Assert.False(outcome.TakesTerminalPath);
        Assert.Equal(new Rac1Class623InitialTargetState(1, true), targets.Get(1009));
    }

    [SkippableFact]
    public void RetailModeSwitchUsesExactJumpTableAndContextOffsets()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var overlay = Rac1ExecutableOverlay.Read(reader, level);
        static uint Word(Rac1ExecutableOverlay source, int address) =>
            BinaryPrimitives.ReadUInt32LittleEndian(source.Read(address, 4));

        Assert.Equal(0x92830028u, Word(overlay, 0x002849c8)); // mode byte
        Assert.Equal(0x2c620008u, Word(overlay, 0x002849cc)); // eight entries
        Assert.Equal(0x24427650u, Word(overlay, 0x002849dc)); // table 0x217650
        Assert.Equal(0x8c640000u, Word(overlay, 0x002849e4));
        Assert.Equal(0x00800008u, Word(overlay, 0x002849e8));
        uint[] jumpTargets =
        [
            0x002849f0, 0x00284a0c, 0x00284a3c, 0x00284a5c,
            0x00284aa0, 0x00284aac, 0x00284af0, 0x00284ad0
        ];
        for (int i = 0; i < jumpTargets.Length; i++)
            Assert.Equal(jumpTargets[i], Word(overlay, 0x00217650 + i * 4));

        Assert.Equal(0x9682002au, Word(overlay, 0x002849f0)); // mode0 kind
        Assert.Equal(0x92830029u, Word(overlay, 0x00284a0c)); // mode1 submode
        Assert.Equal(0x92820029u, Word(overlay, 0x00284a3c)); // mode2
        Assert.Equal(0x92830029u, Word(overlay, 0x00284a5c)); // mode3
        Assert.Equal(0x92820029u, Word(overlay, 0x00284aac)); // mode5
        Assert.Equal(0x92820029u, Word(overlay, 0x00284ad0)); // mode7
        Assert.Equal(0xa6430014u, Word(overlay, 0x00284a94)); // reset tracking
        Assert.Equal(0xa640001cu, Word(overlay, 0x00284a9c)); // reset tracking
        Assert.Equal(0x24020002u, Word(overlay, 0x00284af0)); // status 2
        Assert.Equal(0x24150001u, Word(overlay, 0x00284af4)); // fallback index 1
        Assert.Equal(0xae620000u, Word(overlay, 0x00284af8)); // status out
        Assert.Equal(0x8fa30024u, Word(overlay, 0x00284afc)); // scalar pointer
        // Mode classification is NOT the final return value: the native
        // service runs a slot query and a second dispatch afterwards.
        Assert.Equal(0x0c08b714u, Word(overlay, 0x00284b30));
        Assert.Equal(0x8c430000u, Word(overlay, 0x00284b50));
        Assert.Equal(0x0220102du, Word(overlay, 0x00284c78)); // return index
    }
}
