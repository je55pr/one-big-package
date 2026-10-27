using System.Buffers.Binary;
using System.Numerics;
using OBP.Core;

namespace OBP.PS2.Graphics;

/// <summary>
/// Shared 64-byte particle VU input format measured in GC Museum and UYA TABLE1.
/// Callers own projection, material/resource binding and source identity.
/// Source identities accompany the descriptor as provenance and are not encoded.
/// </summary>
public readonly record struct RcParticleSpriteDescriptor(ObpSourceRef EmitterSource, ObpSourceRef TextureSource,
    Vector3 RelativePosition, Vector2 ProjectedAxes, uint Color, byte Blend, ulong NativeTex0)
{
    public byte[] CopyNativeDescriptor()
    {
        byte[] result = new byte[64];
        void F(int at, float f) => BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(at), f);
        void U(int at, uint n) => BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at), n);
        F(0, RelativePosition.X); F(4, RelativePosition.Y); F(8, RelativePosition.Z);
        U(0x10, Color & 255); U(0x14, Color >> 8 & 255); U(0x18, Color >> 16 & 255); U(0x1c, Color >> 24);
        F(0x20, ProjectedAxes.X); F(0x24, ProjectedAxes.Y);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(0x28), Blend);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(0x30), NativeTex0);
        return result;
    }
}
