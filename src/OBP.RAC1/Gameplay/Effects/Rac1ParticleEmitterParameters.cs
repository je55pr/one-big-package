using System.Buffers.Binary;
using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>Class-27's 0xe0-byte native parameter block; units remain Z-up and per native tick.</summary>
public sealed class Rac1ParticleEmitterParameters
{
    public const int ByteSize = 0xe0;
    private readonly byte[] _data;
    private Rac1ParticleEmitterParameters(byte[] data) => _data = data;

    public static Rac1ParticleEmitterParameters Parse(ReadOnlySpan<byte> pvars)
    {
        if (pvars.Length != ByteSize) throw new InvalidDataException("Class-27 requires its complete 0xe0-byte PVar block.");
        int[] scalars = [0, 4, 8, 0xc, 0x10, 0x14, 0x18, 0x20, 0x24, 0x28, 0x30, 0x34, 0x38, 0x3c,
            0x40, 0x44, 0x48, 0x4c, 0x50, 0x54, 0x58, 0x5c, 0x60, 0x64, 0x68, 0x6c, 0x74, 0x78,
            0x80, 0x84, 0x88, 0x8c, 0x90, 0x94, 0x98, 0xa0, 0xa4, 0xa8, 0xac, 0xb0, 0xb4, 0xb8, 0xbc, 0xcc, 0xd0, 0xd4];
        foreach (int at in scalars)
            if (!float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(pvars[at..])))
                throw new InvalidDataException($"Class-27 parameter +0x{at:x} is not finite.");
        return new(pvars.ToArray());
    }

    public Vector3 Acceleration => V(0);
    public Vector3 BoundsMinimum => V(0x10);
    public Vector3 BoundsMaximum => V(0x20);
    public float SizeStep => F(0x30);
    public float AngularStep => F(0x34);
    public float AlphaStep => F(0x38);
    public float BounceFactor => F(0x3c);
    public float SpeedLimit => F(0x40);
    public float AlphaMinimum => F(0x44);
    public float AlphaMaximum => F(0x48);
    public Vector3 ColorStep => V(0x4c);
    public Vector3 ColorMinimum => V(0x58);
    public Vector3 ColorMaximum => V(0x64);
    public uint Flags => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(0x70));
    public float SizeMinimum => F(0x74);
    public float SizeMaximum => F(0x78);
    public Vector3 VelocityMinimumOrBase => V(0x80);
    public Vector3 VelocityMaximumOrEulerRange => V(0x90);
    public float AngleMinimum => F(0xa0);
    public float AngleMaximum => F(0xa4);
    public float InitialSizeMinimum => F(0xa8);
    public float InitialSizeMaximum => F(0xac);
    public Vector3 InitialColor => V(0xb0);
    public float InitialAlpha => F(0xbc);
    public int Lifetime => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(0xc0));
    public byte BurstCount => _data[0xc4];
    public byte BurstInterval => _data[0xc5];
    public byte DrawFlags => _data[0xc6];
    public byte TextureDefinition => _data[0xc7];
    public int InitialCountdown => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(0xc8));
    public Vector3 OffsetRange => V(0xcc);
    public byte[] CopyNativeParameters() => (byte[])_data.Clone();

    internal void RequireSupported()
    {
        if ((Flags & 1) != 0)
            throw new NotSupportedException("Type-6 collision/bounce needs its recovered native contact adapter; this slice does not admit flag 1.");
        if ((Flags & 0xffff0000) != 0)
            throw new NotSupportedException("Class-27 flags outside the recovered low 16 bits are not admitted.");
        if (Lifetime is < 1 or > short.MaxValue)
            throw new NotSupportedException("Class-27 requires a positive NTSC lifetime fitting the native signed timer.");
        if (BoundsMinimum.X > BoundsMaximum.X || BoundsMinimum.Y > BoundsMaximum.Y || BoundsMinimum.Z > BoundsMaximum.Z ||
            ColorMinimum.X > ColorMaximum.X || ColorMinimum.Y > ColorMaximum.Y || ColorMinimum.Z > ColorMaximum.Z ||
            AlphaMinimum > AlphaMaximum || SizeMinimum > SizeMaximum || SpeedLimit < 0)
            throw new InvalidDataException("Class-27 has inverted bounds or a negative speed cap.");
    }

    private float F(int offset) => BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(offset));
    private Vector3 V(int offset) => new(F(offset), F(offset + 4), F(offset + 8));
}
