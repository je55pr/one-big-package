using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Compression;

namespace OBP.RAC2.Level;

/// <summary>
/// The Going Commando gameplay lump (level WAD slot 2, WAD-LZ) — instance
/// placement. The decompressed buffer starts with a table of <c>s32</c> block
/// pointers; each instance block is <c>{ s32 count; pad to 0x10 }</c> then
/// <c>count</c> packed structs. Translated from
/// <c>reference-ts/packages/gc-instances</c>.
/// </summary>
public static class GcInstances
{
    private const int DirLightsPtr = 0x04;
    private const int TieInstancesPtr = 0x34;
    private const int ShrubInstancesPtr = 0x40;
    private const int MobyInstancesPtr = 0x4c;
    private const int PVarTablePtr = 0x5c;
    private const int PVarDataPtr = 0x60;
    private const int PointLightsPtr = 0x80;
    private const int EnvTransitionsPtr = 0x84;
    private const int EnvSamplesPtr = 0x8c;
    private const int TieInstanceSize = 0x60;
    private const int ShrubInstanceSize = 0x70;
    private const int MobyInstanceSize = 0x88;

    /// <summary>Matrix instance (tie / shrub): a column-major 4×4.</summary>
    public sealed record MatrixInstance(int Index, int OClass, float[] Matrix);

    /// <summary>
    /// Pos/rot/scale instance (moby). <see cref="LightColour"/> is the static
    /// ambient light baked at the instance (<c>Rgb96</c> @ 0x74, s32 per channel
    /// / 255); <see cref="LightIndex"/> (@ 0x80) selects the directional light in
    /// <see cref="Gameplay.DirLights"/> (out of range = ambient only).
    /// </summary>
    public sealed record MobyInstance(
        int Index, int OClass, float Scale,
        (float X, float Y, float Z) Position, (float X, float Y, float Z) Rotation,
        (float R, float G, float B) LightColour, int LightIndex,
        int Uid, int Raw0x14, int PVarIndex, int ModeBits,
        byte[] RawInstance, byte[]? PVarData);

    /// <summary>
    /// A Going Commando directional light — the main light type. Two components
    /// (a + b); each an RGB colour and a unit world-space direction (native
    /// Z-up). <c>DirectionalLightPacked</c> 0x40.
    /// </summary>
    public sealed record DirLight(
        (float X, float Y, float Z) ColourA, (float X, float Y, float Z) DirectionA,
        (float X, float Y, float Z) ColourB, (float X, float Y, float Z) DirectionB);

    /// <summary>A point light — position / radius / RGB colour (native Z-up). Only affects mobies.</summary>
    public sealed record PointLight((float X, float Y, float Z) Position, float Radius, (float R, float G, float B) Colour);

    /// <summary>Fog override carried by an <see cref="EnvSample"/>: colour + world-unit distances + 0..255 visibility intensities.</summary>
    public sealed record EnvFog((float R, float G, float B) Colour, float NearDistance, float FarDistance, float NearIntensity, float FarIntensity);

    /// <summary>
    /// An environment sample point — a spatial probe. The nearest one sets a
    /// point's ambient ("hero") colour + directional light and its fog.
    /// <c>GcUyaDlEnvSamplePointPacked</c> 0x20; position native Z-up, × 1/4.
    /// </summary>
    public sealed record EnvSample(
        (float X, float Y, float Z) Position,
        int HeroLightIndex,
        (float R, float G, float B) HeroColour,
        EnvFog? Fog);

    /// <summary>One end of an <see cref="EnvTransition"/> — the region state on that side of the volume.</summary>
    public sealed record EnvState(
        (float R, float G, float B) HeroColour, int HeroLightIndex,
        (float R, float G, float B) FogColour,
        float FogNearDistance, float FogFarDistance, float FogNearIntensity, float FogFarIntensity);

    /// <summary>
    /// A box volume across which the hero (player) lighting and/or fog blend
    /// from <see cref="StateA"/> to <see cref="StateB"/> — a doorway.
    /// <c>EnvTransitionPacked</c> 0x80: `Mat4 inverseMatrix` (world → box local,
    /// column-major), the two states, then `u32 flags` (bit0 hero, bit1 fog).
    /// <see cref="BoundingSphere"/> = <c>[cx, cy, cz, radius]</c> native Z-up.
    /// </summary>
    public sealed record EnvTransition(
        float[] InverseMatrix,
        (float X, float Y, float Z, float R) BoundingSphere,
        bool EnableHero, bool EnableFog,
        EnvState StateA, EnvState StateB);

    public sealed record Gameplay(
        IReadOnlyList<MatrixInstance> TieInstances,
        IReadOnlyList<MatrixInstance> ShrubInstances,
        IReadOnlyList<MobyInstance> MobyInstances,
        IReadOnlyList<DirLight> DirLights,
        IReadOnlyList<PointLight> PointLights,
        IReadOnlyList<EnvSample> EnvSamples,
        IReadOnlyList<EnvTransition> EnvTransitions,
        int DecompressedSize);

    public static Gameplay Read(IRandomAccessReader gameplayLump, long maxDecompressedBytes = 64L * 1024 * 1024) =>
        Parse(WadLz.ReadBlock(gameplayLump, 0, maxDecompressedBytes).Data);

    public static Gameplay Parse(byte[] data)
    {
        int BlockPointer(int headerOffset)
        {
            if (headerOffset < 0 || headerOffset + 4 > data.Length)
            {
                return 0;
            }

            int value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(headerOffset));
            return value > 0 && value < data.Length ? value : 0;
        }

        int pvarTableBlock = BlockPointer(PVarTablePtr);
        int pvarDataBlock = BlockPointer(PVarDataPtr);

        byte[]? ResolvePVar(int pvarIndex)
        {
            if (pvarIndex < 0 || pvarTableBlock == 0 || pvarDataBlock == 0)
            {
                return null;
            }

            long tableAt64 = (long)pvarTableBlock + pvarIndex * 8L;
            if (tableAt64 < 0 || tableAt64 + 8 > data.Length)
            {
                return null;
            }

            int tableAt = (int)tableAt64;
            int relativeOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableAt));
            int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableAt + 4));
            long dataAt64 = (long)pvarDataBlock + relativeOffset;
            if (relativeOffset < 0 || size < 0 || dataAt64 < 0 || dataAt64 + size > data.Length)
            {
                return null;
            }

            return data.AsSpan((int)dataAt64, size).ToArray();
        }

        List<MatrixInstance> ReadBlock(int pointerOffset, int structSize)
        {
            var outp = new List<MatrixInstance>();
            if (pointerOffset + 4 > data.Length)
            {
                return outp;
            }

            int blockOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pointerOffset));
            if (blockOffset <= 0 || blockOffset + 0x10 > data.Length)
            {
                return outp;
            }

            int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(blockOffset));
            if (count is < 0 or > 200_000)
            {
                return outp;
            }

            for (int i = 0; i < count; i++)
            {
                int at = blockOffset + 0x10 + i * structSize;
                if (at + 0x50 > data.Length)
                {
                    break;
                }

                var matrix = new float[16];
                for (int m = 0; m < 16; m++)
                {
                    matrix[m] = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + 0x10 + m * 4));
                }

                outp.Add(new MatrixInstance(i, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at)), matrix));
            }

            return outp;
        }

        List<MobyInstance> ReadMoby()
        {
            var outp = new List<MobyInstance>();
            if (MobyInstancesPtr + 4 > data.Length)
            {
                return outp;
            }

            int blockOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(MobyInstancesPtr));
            if (blockOffset <= 0 || blockOffset + 0x10 > data.Length)
            {
                return outp;
            }

            int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(blockOffset));
            if (count is < 0 or > 200_000)
            {
                return outp;
            }

            for (int i = 0; i < count; i++)
            {
                int at = blockOffset + 0x10 + i * MobyInstanceSize;
                if (at + MobyInstanceSize > data.Length)
                {
                    break;
                }

                if (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at)) != MobyInstanceSize)
                {
                    break; // `size` field is always 0x88
                }

                float F(int rel) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + rel));
                var position = (F(0x40), F(0x44), F(0x48));
                if (!float.IsFinite(position.Item1) || !float.IsFinite(position.Item2) || !float.IsFinite(position.Item3))
                {
                    continue;
                }

                int S(int rel) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + rel));
                int pvarIndex = S(0x68);
                outp.Add(new MobyInstance(
                    i,
                    S(0x28),
                    F(0x2c),
                    position,
                    (F(0x4c), F(0x50), F(0x54)),
                    (S(0x74) / 255f, S(0x78) / 255f, S(0x7c) / 255f),
                    S(0x80),
                    S(0x10),
                    S(0x14),
                    pvarIndex,
                    S(0x70),
                    data.AsSpan(at, MobyInstanceSize).ToArray(),
                    ResolvePVar(pvarIndex)));
            }

            return outp;
        }

        List<DirLight> ReadDirLights()
        {
            var outp = new List<DirLight>();
            if (DirLightsPtr + 4 > data.Length)
            {
                return outp;
            }

            int blockOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(DirLightsPtr));
            if (blockOffset <= 0 || blockOffset + 0x10 > data.Length)
            {
                return outp;
            }

            int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(blockOffset));
            if (count is < 0 or > 4096)
            {
                return outp;
            }

            for (int i = 0; i < count; i++)
            {
                int at = blockOffset + 0x10 + i * 0x40;
                if (at + 0x40 > data.Length)
                {
                    break;
                }

                (float, float, float) V3(int o) => (
                    BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + o)),
                    BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + o + 4)),
                    BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + o + 8)));
                outp.Add(new DirLight(V3(0x00), V3(0x10), V3(0x20), V3(0x30)));
            }

            return outp;
        }

        List<PointLight> ReadPointLights()
        {
            var outp = new List<PointLight>();
            if (PointLightsPtr + 4 > data.Length)
            {
                return outp;
            }

            int blockOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(PointLightsPtr));
            if (blockOffset <= 0 || blockOffset + 0x10 > data.Length)
            {
                return outp;
            }

            int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(blockOffset));
            if (count is < 0 or > 4096)
            {
                return outp;
            }

            for (int i = 0; i < count; i++)
            {
                int at = blockOffset + 0x10 + 0x800 + i * 0x10;
                if (at + 0x10 > data.Length)
                {
                    break;
                }

                ushort U(int o) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + o));
                float radius = U(0x06) / 64f;
                if (radius <= 0)
                {
                    continue;
                }

                outp.Add(new PointLight(
                    (U(0x00) / 64f, U(0x02) / 64f, U(0x04) / 64f),
                    radius,
                    (U(0x08) / 65535f, U(0x0a) / 65535f, U(0x0c) / 65535f)));
            }

            return outp;
        }

        List<EnvSample> ReadEnvSamples()
        {
            var outp = new List<EnvSample>();
            if (EnvSamplesPtr + 4 > data.Length)
            {
                return outp;
            }

            int blockOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(EnvSamplesPtr));
            if (blockOffset <= 0 || blockOffset + 0x10 > data.Length)
            {
                return outp;
            }

            int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(blockOffset));
            if (count is < 0 or > 4096)
            {
                return outp;
            }

            for (int i = 0; i < count; i++)
            {
                int at = blockOffset + 0x10 + i * 0x20;
                if (at + 0x20 > data.Length)
                {
                    break;
                }

                short S16(int o) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at + o));
                byte B(int o) => data[at + o];
                short nearD = S16(0x1a), farD = S16(0x1c);
                outp.Add(new EnvSample(
                    (S16(0x04) / 4f, S16(0x06) / 4f, S16(0x08) / 4f),
                    BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 0x00)),
                    (B(0x10) / 255f, B(0x11) / 255f, B(0x12) / 255f),
                    farD > nearD
                        ? new EnvFog((B(0x17) / 255f, B(0x18) / 255f, B(0x19) / 255f), nearD, farD, B(0x0e), B(0x0f))
                        : null));
            }

            return outp;
        }

        List<EnvTransition> ReadEnvTransitions()
        {
            var outp = new List<EnvTransition>();
            if (EnvTransitionsPtr + 4 > data.Length)
            {
                return outp;
            }

            int blockOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(EnvTransitionsPtr));
            if (blockOffset <= 0 || blockOffset + 0x10 > data.Length)
            {
                return outp;
            }

            int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(blockOffset));
            if (count is < 0 or > 4096)
            {
                return outp;
            }

            int spheresAt = blockOffset + 0x10;
            int structsAt = spheresAt + count * 0x10;
            for (int i = 0; i < count; i++)
            {
                int so = spheresAt + i * 0x10;
                int at = structsAt + i * 0x80;
                if (at + 0x80 > data.Length)
                {
                    break;
                }

                float F(int o) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + o));
                float Sph(int o) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(so + o));
                (float, float, float) Rgb(int o) => (data[at + o] / 255f, data[at + o + 1] / 255f, data[at + o + 2] / 255f);
                uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 0x50));
                EnvState State(int side) => new(
                    Rgb(0x40 + side * 4),
                    BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 0x48 + side * 4)),
                    Rgb(0x54 + side * 4),
                    F(0x5c + side * 0x10), F(0x64 + side * 0x10), F(0x60 + side * 0x10), F(0x68 + side * 0x10));

                var mat = new float[16];
                for (int j = 0; j < 16; j++)
                {
                    mat[j] = F(j * 4);
                }

                outp.Add(new EnvTransition(
                    mat,
                    (Sph(0), Sph(4), Sph(8), Sph(12)),
                    (flags & 1) != 0, (flags & 2) != 0,
                    State(0), State(1)));
            }

            return outp;
        }

        return new Gameplay(
            ReadBlock(TieInstancesPtr, TieInstanceSize),
            ReadBlock(ShrubInstancesPtr, ShrubInstanceSize),
            ReadMoby(),
            ReadDirLights(),
            ReadPointLights(),
            ReadEnvSamples(),
            ReadEnvTransitions(),
            data.Length);
    }

    /// <summary>Column-major <c>Mat4</c> from position, XYZ-euler rotation (radians) and uniform scale. <c>R = Rz·Ry·Rx</c>.</summary>
    public static double[] MatrixFromPosRotScale((double X, double Y, double Z) position, (double X, double Y, double Z) rotation, double scale)
    {
        double cx = System.Math.Cos(rotation.X), sx = System.Math.Sin(rotation.X);
        double cy = System.Math.Cos(rotation.Y), sy = System.Math.Sin(rotation.Y);
        double cz = System.Math.Cos(rotation.Z), sz = System.Math.Sin(rotation.Z);
        double m00 = cy * cz, m01 = cy * sz, m02 = -sy;
        double m10 = sx * sy * cz - cx * sz, m11 = sx * sy * sz + cx * cz, m12 = sx * cy;
        double m20 = cx * sy * cz + sx * sz, m21 = cx * sy * sz - sx * cz, m22 = cx * cy;
        return
        [
            m00 * scale, m10 * scale, m20 * scale, 0,
            m01 * scale, m11 * scale, m21 * scale, 0,
            m02 * scale, m12 * scale, m22 * scale, 0,
            position.X, position.Y, position.Z, 1,
        ];
    }

    /// <summary>Apply a column-major <c>Mat4</c> to a local point.</summary>
    public static (double X, double Y, double Z) TransformPoint(IReadOnlyList<double> m, double x, double y, double z) =>
        (m[0] * x + m[4] * y + m[8] * z + m[12],
         m[1] * x + m[5] * y + m[9] * z + m[13],
         m[2] * x + m[6] * y + m[10] * z + m[14]);

    public static (double X, double Y, double Z) TransformPoint(float[] m, double x, double y, double z) =>
        (m[0] * x + m[4] * y + m[8] * z + m[12],
         m[1] * x + m[5] * y + m[9] * z + m[13],
         m[2] * x + m[6] * y + m[10] * z + m[14]);
}
