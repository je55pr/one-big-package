using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class UyaMobyPlacementTests
{
    [Fact]
    public void AuthoredPlacementReaderUsesExactNativeRecordOffsets()
    {
        byte[] raw = new byte[UyaMobyPlacement.NativeRecordBytes];
        Write(raw, UyaMobyPlacement.NativePositionOffset + 0x00, 11.25f);
        Write(raw, UyaMobyPlacement.NativePositionOffset + 0x04, 22.5f);
        Write(raw, UyaMobyPlacement.NativePositionOffset + 0x08, 33.75f);
        Write(raw, UyaMobyPlacement.NativeRotationOffset + 0x00, 0.1f);
        Write(raw, UyaMobyPlacement.NativeRotationOffset + 0x04, 0.2f);
        Write(raw, UyaMobyPlacement.NativeRotationOffset + 0x08, 0.3f);

        RuntimeDynamicObject source = new(
            "rac3",
            5821,
            430,
            null,
            "moby:5821",
            "table:1:moby:430",
            new RuntimeObjectTransform(new double[16]),
            [],
            [
                new RuntimeOpaquePayload(
                    UyaMobyRuntimeSession.InstancePayloadFormat,
                    raw),
            ]);

        UyaMobyPlacementFacts facts = UyaMobyPlacement.ReadAuthored(source);

        Assert.Equal(11.25f, facts.NativeX);
        Assert.Equal(22.5f, facts.NativeY);
        Assert.Equal(33.75f, facts.NativeZ);
        Assert.Equal(0.1f, facts.RotationX);
        Assert.Equal(0.2f, facts.RotationY);
        Assert.Equal(0.3f, facts.RotationZ);
    }

    [Fact]
    public void ObpYUpConversionRestoresNativeZUpOrdering()
    {
        UyaNativePoint native =
            UyaMobyPlacement.ObpYUpToNative(10f, 30f, 20f);

        Assert.Equal(10f, native.X);
        Assert.Equal(20f, native.Y);
        Assert.Equal(30f, native.Z);
    }

    [SkippableFact]
    public void RetailTable1RuntimePayloadRoundTripsClass5821NativePlacement()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        UyaLevelCore.OpenedLevel opened = UyaLevelCore.Open(reader, 1);
        UyaGameplay.Gameplay gameplay = UyaGameplay.Read(opened.GameplayReader);
        RuntimeWorld world = Rac3WorldImport.Build(reader, 1);

        UyaGameplay.MobyInstance authored = gameplay.MobyInstances
            .Single(instance => instance.Index == 430);
        Assert.Equal(5821, authored.OClass);

        RuntimeDynamicObject source = world.DynamicObjects!
            .Single(instance =>
                instance.NativeClassId == 5821 &&
                instance.InstanceIndex == 430);
        UyaMobyPlacementFacts facts = UyaMobyPlacement.ReadAuthored(source);

        Assert.Equal(authored.Position.X, facts.NativeX);
        Assert.Equal(authored.Position.Y, facts.NativeY);
        Assert.Equal(authored.Position.Z, facts.NativeZ);
        Assert.Equal(authored.Rotation.X, facts.RotationX);
        Assert.Equal(authored.Rotation.Y, facts.RotationY);
        Assert.Equal(authored.Rotation.Z, facts.RotationZ);
    }

    private static void Write(byte[] target, int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(target.AsSpan(offset), value);
}
