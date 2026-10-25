using OBP.Core;
using System.Buffers.Binary;
using OBP.RAC1.Player;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1TraversalEquipmentTests
{
    [Fact]
    public void AuthoredRuntimeAdapterOwnsPvarParsingAndRestoresNativeAxisOrder()
    {
        double[] matrix = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 12, 30, 20, 1];
        var source = new RuntimeDynamicObject(ObpSourceGame.Rac1, 758, 5, 8, "moby:758", "moby:5",
            new RuntimeObjectTransform(matrix), [], [new("rac1-pvar", TargetPvar(1, 10, 0))]);
        var authored = Rac1TraversalEquipmentSwingshotTarget.ReadAuthored(source);
        Assert.NotNull(authored);
        Assert.Equal(new Rac1NativeVector3(12, 20, 30), authored.Value.NativeAnchor);
        Assert.Equal(5, authored.Value.InstanceIndex);
        Assert.True(authored.Value.Target.HasUnrestrictedRegion);
        Assert.Null(Rac1TraversalEquipmentSwingshotTarget.ReadAuthored(source with { SourceGame = ObpSourceGame.Rac2 }));
        Assert.Null(Rac1TraversalEquipmentSwingshotTarget.ReadAuthored(source with { NativePayloads = [] }));
    }

    [Theory]
    [InlineData(0, Rac1TraversalEquipmentSwingshotMode.Pull)]
    [InlineData(1, Rac1TraversalEquipmentSwingshotMode.Swing)]
    [InlineData(5, Rac1TraversalEquipmentSwingshotMode.Swing)]
    public void OrdinaryVersaTargetSeparatesItsAuthoredModeAndRadius(
        int mode, Rac1TraversalEquipmentSwingshotMode expected)
    {
        byte[] pvar = TargetPvar(mode, 8, 0);
        Assert.True(Rac1TraversalEquipmentSwingshotTarget.TryDecode(758, pvar, out var target));
        Assert.Equal(expected, target.Mode);
        Assert.Equal(8, target.Radius);
        Assert.Equal(30, target.MaximumDistance);
        Assert.Equal(-1, target.RegionIndex);
    }

    [Fact]
    public void TargetDecoderRejectsOtherFamiliesDisabledAndInvalidAuthoredData()
    {
        byte[] pvar = TargetPvar(1, 8, 20);
        Assert.False(Rac1TraversalEquipmentSwingshotTarget.TryDecode(803, pvar, out _));
        Assert.False(Rac1TraversalEquipmentSwingshotTarget.TryDecode(758, pvar.AsSpan(0, 32), out _));
        BinaryPrimitives.WriteInt32LittleEndian(pvar, 1);
        Assert.False(Rac1TraversalEquipmentSwingshotTarget.TryDecode(758, pvar, out _));
        pvar = TargetPvar(1, float.NaN, 20);
        Assert.False(Rac1TraversalEquipmentSwingshotTarget.TryDecode(758, pvar, out _));
    }

    [Fact]
    public void TargetGateRequiresFacingDistanceRegionAndVisibilityFacts()
    {
        Assert.True(Rac1TraversalEquipmentSwingshotTarget.TryDecode(758, TargetPvar(1, 8, 20), out var target));
        var player = Rac1NativeVector3.Zero;
        var anchor = new Rac1NativeVector3(10, 0, 8);
        Assert.NotNull(target.CandidateScore(player, anchor, 0, true, true, true));
        Assert.Null(target.CandidateScore(player, anchor, Math.PI, true, true, true));
        Assert.Null(target.CandidateScore(player, anchor, 0, false, true, true));
        Assert.Null(target.CandidateScore(player, anchor, 0, true, false, true));
        Assert.Null(target.CandidateScore(player, anchor, 0, true, true, false));
        Assert.Null(target.CandidateScore(player, new(3.99, 0, 8), 0, true, true, true));
        Assert.NotNull(target.CandidateScore(player, new(4, 0, 0), 0, true, true, true));
        Assert.NotNull(target.CandidateScore(player, new(20, 0, 0), 0, true, true, true));
        Assert.Null(target.CandidateScore(player, new(20.01, 0, 0), 0, true, true, true));
        double score = target.CandidateScore(player, anchor, 0, true, true, true)!.Value;
        Assert.Equal(score + 17,
            target.CandidateScore(player, anchor, 0, true, true, true, false, true));
    }

    [Fact]
    public void TetherStepConstrainsThePredictedPositionAndSeeksTheAuthoredRadius()
    {
        var position = new Rac1NativeVector3(0, 0, 0);
        var anchor = new Rac1NativeVector3(0, 0, 10);
        var step = Rac1TraversalEquipmentSwingshot.StepSwing(position, anchor,
            new(0.12, 0, 0), 10, 8);
        double distance = new Rac1NativeVector3(
            position.X + step.Request.X - anchor.X,
            position.Y + step.Request.Y - anchor.Y,
            position.Z + step.Request.Z - anchor.Z).Length;
        Assert.Equal(10 - 10d / 60d, step.Radius, 12);
        Assert.Equal(step.Radius, distance, 12);
        Assert.True(step.Request.X > 0);
        Assert.True(step.Request.Z > 0);
    }

    [Fact]
    public void TetherEntryUsesActualDistanceAndReleaseKeepsTheNativeStrictSupportGate()
    {
        Assert.Equal(5, Rac1TraversalEquipmentSwingshot.InitialSwingRadius(new(1, 2, 3), new(4, 2, 7)));
        Assert.Equal(0, Rac1TraversalEquipmentSwingshot.ReleaseAction(0.39));
        Assert.Equal(6, Rac1TraversalEquipmentSwingshot.ReleaseAction((double)0.4f));
        Assert.Equal(6, Rac1TraversalEquipmentSwingshot.ReleaseAction(1));
        Assert.Equal(48, Rac1TraversalEquipmentSwingshot.SwingSequence);
        Assert.Equal(5, Rac1TraversalEquipmentSwingshot.SwingEntryBlendTicks);
    }

    [Fact]
    public void TetherConstraintPreservesTheNativeGravityAndContactDampingOrder()
    {
        var position = new Rac1NativeVector3(8, 0, 0);
        var anchor = new Rac1NativeVector3(0, 0, 0);
        var contact = new Rac1NativeVector3(0, 0.3, 0.1);
        var result = Rac1TraversalEquipmentSwingshot.StepSwing(position, anchor, contact, 8, 8);
        double nextY = position.Y + result.Request.Y;
        double nextZ = position.Z + result.Request.Z;
        Assert.Equal((0.3 * (double)0.9f) / (0.1 * (double)0.9f - 15d / 3600d), nextY / nextZ, 12);
        Assert.Equal(8, new Rac1NativeVector3(position.X + result.Request.X, nextY, nextZ).Length, 12);
    }

    [Fact]
    public void OxygenIsAnIntegerCounterAndMaskDoesNotGrantSwimmingEquipment()
    {
        int oxygen = Rac1TraversalEquipmentWater.MaximumOxygen;
        for (int i = 0; i < 909; i++)
            oxygen = Rac1TraversalEquipmentWater.StepOxygen(oxygen, 0x11, 0x33, false);
        Assert.Equal(1, oxygen);
        oxygen = Rac1TraversalEquipmentWater.StepOxygen(oxygen, 0x11, 0x33, false);
        Assert.Equal(0, oxygen);
        Assert.Equal(0, Rac1TraversalEquipmentWater.StepOxygen(oxygen, 0x11, 0x33, true));
        Assert.Equal(10_000, Rac1TraversalEquipmentWater.StepOxygen(oxygen, 0x12, 0x36, false));
        Assert.Equal(0, Rac1TraversalEquipmentWater.StepOxygen(oxygen, 0, 0x6a, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac1TraversalEquipmentWater.StepOxygen(-1, 0x11, 0x33, false));
    }

    private static byte[] TargetPvar(int mode, float radius, float maximumDistance)
    {
        var pvar = new byte[0x24];
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(4), radius);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(8), mode);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x1c), -1);
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0x20), maximumDistance);
        return pvar;
    }
}
