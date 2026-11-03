using OBP.Core;
using System.Buffers.Binary;
using OBP.RAC3.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

/// <summary>Shared synthetic class-5821 builders. Synthetic data only; no retail content.</summary>
internal static class Rac3Class5821Fixtures
{
    /// <summary>
    /// Copies the source's PVar, lets <paramref name="mutate"/> edit the copy, and returns the source carrying
    /// only that PVar payload (the synthetic class-5821 fixture has no other payloads).
    /// </summary>
    internal static RuntimeDynamicObject WithPvar(RuntimeDynamicObject source, Action<byte[]> mutate)
    {
        byte[] pvar = source.NativePayloads!
            .Single(payload =>
                payload.Format == Rac3MobyRuntimeSession.PVarPayloadFormat)
            .Data.ToArray();
        mutate(pvar);
        return source with
        {
            NativePayloads =
            [
                new RuntimeOpaquePayload(
                    Rac3MobyRuntimeSession.PVarPayloadFormat,
                    pvar),
            ],
        };
    }

    internal static RuntimeDynamicObject Class5821(
        int instanceIndex,
        short authoredLifetime)
    {
        byte[] pvar = new byte[Rac3Class5821Actor.PVarSize];
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.LifetimeRelativePointerOffset, sizeof(int)),
            Rac3Class5821Actor.RuntimeLifetimeOffset);
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.TargetBlockRelativePointerOffset, sizeof(int)),
            Rac3Class5821Actor.RuntimeTargetBlockOffset);
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.DamageConfigRelativePointerOffset, sizeof(int)),
            Rac3Class5821Actor.DamageConfigOffset);
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.RuntimeLifetimeOffset, sizeof(float)),
            1f);
        BinaryPrimitives.WriteInt16LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.AuthoredLifetimeOffset, sizeof(short)),
            authoredLifetime);
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.DamageConfigVerticalThresholdOffset, sizeof(float)),
            0f);
        pvar[Rac3Class5821Actor.DamageConfigByte49Offset] = 0;
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.DamageConfigSameClassMultiplierOffset, sizeof(float)),
            0f);
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.DamageConfigOptionalMultiplierOffset, sizeof(float)),
            1f);
        pvar[Rac3Class5821Actor.NativeOrdinaryAttackDamageByteOffset] = 1;
        pvar[Rac3Class5821Actor.NativeOrdinaryAttackFlagSelectorOffset] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.PrimaryTargetSelectorIndexOffset, sizeof(int)),
            13);
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.PrimaryTargetSelectorRadiusOffset, sizeof(float)),
            16f);
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.SecondaryTargetSelectorIndexOffset, sizeof(int)),
            18);
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.SecondaryTargetSelectorRadiusOffset, sizeof(float)),
            32f);
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.RuntimeTargetSelectorModeOffset, sizeof(int)),
            0);
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.RuntimeTargetSelectorAuxOffset, sizeof(int)),
            0);

        return new RuntimeDynamicObject(
            ObpSourceGame.Rac3,
            Rac3Class5821Actor.NativeClassId,
            instanceIndex,
            null,
            $"moby:{Rac3Class5821Actor.NativeClassId}",
            $"table:1:moby:{instanceIndex}",
            new RuntimeObjectTransform(
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 10, 0, 1,
            ]),
            Array.Empty<RuntimeObjectMesh>(),
            [
                new RuntimeOpaquePayload(
                    Rac3MobyRuntimeSession.PVarPayloadFormat,
                    pvar),
            ]);
    }
}
