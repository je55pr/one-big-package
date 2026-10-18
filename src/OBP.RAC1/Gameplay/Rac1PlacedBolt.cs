using System.Buffers.Binary;
using System.Numerics;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>Authored class13..16 initialization at 0x2a5e74, distinct from crate emission.</summary>
public static class Rac1PlacedBolt
{
    public const int MinimumPVarSize = 0x70;
    public static bool IsBoltClass(int nativeClass) => nativeClass is >= 13 and <= 16;

    public sealed record Authored(int NativeClassId, int InstanceIndex, int Uid, Vector3 Position,
        Vector3 Up, bool PreserveAuthoredHeight)
    {
        public Vector3 ProbeStart => Position + Up * .2f;
        public Vector3 ProbeEnd => Position - Up * 3;
    }

    public static Authored? ReadAuthored(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac1" || !IsBoltClass(source.NativeClassId)) return null;
        byte[]? raw = source.NativePayloads?.SingleOrDefault(payload => payload.Format == Rac1BoltCrate.InstancePayloadFormat)?.Data;
        byte[]? pvar = source.NativePayloads?.SingleOrDefault(payload => payload.Format == Rac1BoltCrate.PVarPayloadFormat)?.Data;
        if (raw is not { Length: 0x78 } || pvar is null || pvar.Length < MinimumPVarSize)
            throw new InvalidDataException("R&C1 placed bolt requires its packed placement and at least 0x70 bytes of PVar.");
        int uid = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(0xc, 2));
        if (uid < 0 || source.InstanceIndex < 0)
            throw new InvalidDataException("R&C1 placed bolt has no nonnegative authored identity.");
        double[] m = source.Transform.Matrix;
        if (m.Length != 16 || m.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("R&C1 placed bolt transform is not a finite affine matrix.");
        Vector3 up = new((float)m[4], (float)m[6], (float)m[5]);
        if (up.LengthSquared() < 1e-12f) throw new InvalidDataException("R&C1 placed bolt has no probe up vector.");
        return new(source.NativeClassId, source.InstanceIndex, uid,
            new((float)m[12], (float)m[14], (float)m[13]), Vector3.Normalize(up), pvar[0x54] != 0);
    }
}
