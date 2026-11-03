using OBP.Core;
using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

/// <summary>Shared synthetic class-500 Bolt Crate builder. Synthetic data only; no retail content.</summary>
internal static class Rac1BoltCrateFixtures
{
    internal static RuntimeDynamicObject Class500(int uid, int rewardCentre)
    {
        var raw = new byte[0x78];
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x0c, 2), checked((ushort)uid));
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x10, 4), rewardCentre);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x18, 4), 500);
        var pvar = new byte[0x100];

        return new RuntimeDynamicObject(
            ObpSourceGame.Rac1, 500, 89, uid, "moby:500", "moby:89",
            new RuntimeObjectTransform(new double[16]), Array.Empty<RuntimeObjectMesh>(),
            [new RuntimeOpaquePayload(Rac1BoltCrate.InstancePayloadFormat, raw),
             new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, pvar)]);
    }
}
