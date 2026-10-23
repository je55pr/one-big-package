using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1AlienQueenLinkedGateTests
{
    private static RuntimeDynamicObject Original(int id, int cls, int index = -1)
    {
        var p = new byte[cls == 1051 ? 0x1b0 : 0x90];
        if (cls == 1051)
        {
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0xfc), 573);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0x100), 578);
        }
        else BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0x1c), index);
        var matrix = new double[16];
        matrix[0] = matrix[5] = matrix[10] = matrix[15] = 1;
        return new("rac1", cls, id, null, "linked", "linked",
            new RuntimeObjectTransform(matrix), [],
            NativePayloads: [new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, p)]);
    }

    private static Rac1AlienQueenLinkedGate Setup() =>
        new(Original(626, 1051), [Original(573, 1021), Original(578, 1021)]);

    [Fact]
    public void SevenDynamicChildrenPerParentGateSourceProximityAndQueenRelease()
    {
        var gate = Setup();
        Assert.Equal(new[] { 573, 578 }, gate.OriginalParentIds);
        Assert.Equal(2, gate.OriginalParentCount);
        Assert.Equal(0, gate.RuntimeChildCount);
        Assert.False(gate.ActivateFromQueen(573));
        Assert.Equal(2, gate.Step(573, Vector3.Zero, true, 0).NativeState);
        Assert.Equal(2, gate.Step(578, Vector3.Zero, true, 0).NativeState);
        Assert.Equal(14, gate.RuntimeChildCount);
        var first = gate.Get(573).Children;
        var second = gate.Get(578).Children;
        Assert.Equal(7, first.Count);
        Assert.Equal(7, second.Count);
        Assert.Empty(first.Select(c => c.RuntimeId).Intersect(second.Select(c => c.RuntimeId)));
        Assert.All(first, child => Assert.Equal(1, child.NativeState));
        Assert.Equal(Enumerable.Range(1, 7).Select(n => n * MathF.PI / 4f),
            first.Select(c => c.RelativeYawRadians));
        Assert.Equal(2, gate.Step(573, Vector3.Zero, true, 0).NativeState);
        Assert.Equal(2, gate.Step(573, Vector3.Zero, true, 0, nativeCommandZero: false).NativeState);
        foreach (var child in first.Take(6)) gate.ConfirmChildInitialized(child.RuntimeId);
        Assert.Equal(2, gate.Step(573, Vector3.Zero, true, 0).NativeState);
        gate.ConfirmChildInitialized(first[6].RuntimeId);
        Assert.Equal(2, gate.Step(573, Vector3.UnitX * 6f, true, 0).NativeState); // strict <6
        Assert.Equal(2, gate.Step(573, Vector3.Zero, false, 0).NativeState);
        Assert.Equal(4, gate.Step(573, Vector3.Zero, true, 0).NativeState);
        Assert.All(first, child => Assert.Equal(5, gate.GetChildState(child.RuntimeId)));
        Assert.False(gate.ActivateFromQueen(573));
        Assert.Equal(4, gate.Step(573, Vector3.Zero, false, .5f).NativeState);
        Assert.True(gate.Get(573).Visible);
        Assert.Equal(6, gate.Step(573, Vector3.Zero, false, 1f).NativeState);
        Assert.False(gate.Get(573).Visible);
        // Independent child update has already reached state7; the source
        // Queen helper still reopens them. It does not require state5.
        foreach (var child in first)
        {
            Assert.Equal(5, gate.StepChild(child.RuntimeId, .5f).NativeState);
            var hidden = gate.StepChild(child.RuntimeId, 1f);
            Assert.Equal(7, hidden.NativeState);
            Assert.False(hidden.Visible);
        }
        Assert.True(gate.ActivateFromQueen(573));
        Assert.True(gate.Get(573).Visible);
        Assert.Equal(8, gate.Get(573).NativeState);
        Assert.All(first, child =>
        {
            var opened = gate.Children(573).Single(c => c.RuntimeId == child.RuntimeId);
            Assert.Equal(9, opened.NativeState);
            Assert.True(opened.Visible);
            Assert.InRange(opened.RotationOffset, 1.047f, 1.048f);
        });
        Assert.False(gate.ActivateFromQueen(573));
        foreach (var child in first)
        {
            var restored = gate.StepChild(child.RuntimeId, 1.1f);
            Assert.Equal(3, restored.NativeState);
            Assert.True(restored.Visible);
            Assert.Equal(0f, restored.RotationOffset);
        }
        Assert.Equal(8, gate.Step(573, Vector3.Zero, false, .7f).NativeState);
        Assert.Equal(2, gate.Step(573, Vector3.Zero, false, .7f).NativeState);
        Assert.Equal(14, gate.RuntimeChildCount); // always native runtime, not source placements
        Assert.Equal(2, gate.OriginalParentCount);
        Assert.Equal(2, gate.Get(578).NativeState);
    }

    [Fact]
    public void ChildOwnershipAndPVarNativeTriggerGateAreStrict()
    {
        var gate = Setup();
        Assert.Throws<KeyNotFoundException>(() => gate.GetChildState(-42));
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.Step(573, new(float.NaN, 0, 0), true, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.Step(573, Vector3.Zero, true, -1));
        gate.Step(573, Vector3.Zero, true, 0);
        var id = gate.Get(573).Children[0].RuntimeId;
        gate.ConfirmChildInitialized(id);
        Assert.Throws<InvalidOperationException>(() => gate.ConfirmChildInitialized(id));
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.ConfirmChildInitialized(-100));
        Assert.Throws<InvalidDataException>(() =>
            new Rac1AlienQueenLinkedGate(Original(626, 1051),
                [Original(573, 1021, 8), Original(578, 1021)]));
        Assert.Throws<InvalidDataException>(() =>
            new Rac1AlienQueenLinkedGate(Original(626, 1051),
                [Original(573, 1021), Original(578, 827)]));
    }

    [Fact]
    public void RecoveredNativeAngleRecurrenceHidesAndRestoresAllLinkedChildren()
    {
        var tuning = new Rac1AlienQueenGateNative(270, 360, 180, 0, 3.109f);
        var gate = Setup();
        gate.StepNative(573, Vector3.Zero, false, tuning);
        foreach (var child in gate.Get(573).Children)
            gate.ConfirmChildInitialized(child.RuntimeId);
        Assert.Equal(4, gate.StepNative(573, Vector3.Zero, true, tuning).NativeState);
        for (int i = 0; i < 250 && gate.Get(573).NativeState != 6; i++)
        {
            gate.StepNative(573, Vector3.Zero, false, tuning);
            foreach (var child in gate.Get(573).Children)
                gate.StepChildNative(child.RuntimeId, tuning);
        }
        Assert.Equal(6, gate.Get(573).NativeState);
        Assert.False(gate.Get(573).Visible);
        Assert.All(gate.Get(573).Children, c =>
        {
            Assert.Equal(7, c.NativeState);
            Assert.False(c.Visible);
            Assert.InRange(c.RotationOffset, 1.047f, 1.048f);
        });
        Assert.True(gate.ActivateFromQueen(573));
        for (int i = 0; i < 250 && gate.Get(573).NativeState != 2; i++)
        {
            gate.StepNative(573, Vector3.Zero, false, tuning);
            foreach (var child in gate.Get(573).Children)
                gate.StepChildNative(child.RuntimeId, tuning);
        }
        Assert.Equal(2, gate.Get(573).NativeState);
        Assert.All(gate.Get(573).Children, c =>
        {
            Assert.Equal(3, c.NativeState);
            Assert.True(c.Visible);
            Assert.Equal(0f, c.RotationOffset);
        });
    }

    [SkippableFact]
    public void OriginalLevel6OverlayContainsQueenLinkedGateSdaConstants()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 6);
        var tuning = Rac1AlienQueenGateNative.Read(Rac1ExecutableOverlay.Read(reader, level));
        Assert.Equal(270f, tuning.AngularApproachDegrees);
        Assert.Equal(360f, tuning.AngularBrakeDegrees);
        Assert.Equal(180f, tuning.AngularMaximumDegrees);
        Assert.Equal(0f, tuning.InitialProximityGate);
        Assert.InRange(tuning.ChildRadialDistance, 3.1089f, 3.1091f);
        Assert.InRange(tuning.PerTickApproach, .00130f, .00132f);
        Assert.InRange(tuning.PerTickBrake, .00174f, .00175f);
        Assert.InRange(tuning.PerTickMaximum, .05235f, .05237f);
    }

    [SkippableFact]
    public void RetailQueenLinksExactlyTwoSourceParentsAndFourteenRuntimeChildren()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 6);
        var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var authored = Rac1Instances.Parse(gameplay).MobyInstances;
        RuntimeDynamicObject Convert(int id)
        {
            var instance = authored[id];
            var data = instance.PVar!;
            return new("rac1", instance.OClass, id, null, "authored", "authored",
                new RuntimeObjectTransform(new double[16]), [],
                NativePayloads: [new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, data)]);
        }
        var gate = new Rac1AlienQueenLinkedGate(Convert(626),
            [Convert(573), Convert(578)]);
        Assert.Equal(new[] { 573, 578 }, gate.OriginalParentIds);
        Assert.Equal(2, gate.OriginalParentCount);
        foreach (int parent in gate.OriginalParentIds)
            Assert.Equal(2, gate.Step(parent, Vector3.Zero, false, 0).NativeState);
        Assert.Equal(14, gate.RuntimeChildCount);
        Assert.Equal(2, gate.OriginalParentCount);
    }
}
