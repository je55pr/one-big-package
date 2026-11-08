using System.Buffers.Binary;

namespace OBP.RAC1.Level;

/// <summary>
/// Novalis's scrolling water surfaces, read from the level's own executable overlay: the river
/// (class 676), the stream at the plumber entrance (class 678), the waterfalls (class 761) and the
/// pulsing pool glows (class 1225). Each class owns a table of 0x60-byte layer records that its draw
/// callback walks (callbacks 0x2f60f8 / 0x2f6150 / 0x2feb28 / 0x309bf8 call the shared drawer 0x2b96e0
/// with the vertex builder 0x262618). Nothing here is committed retail data; it is read from the
/// player's own disc at load, so the tables are located at the documented Novalis addresses and the
/// structure is validated rather than trusted.
/// </summary>
public static class Rac1NovalisScrollingWater
{
    public const int NativeLevelId = 1;

    /// <summary>The native class that owns a layer set.</summary>
    public static readonly int RiverClass = 676, StreamClass = 678, WaterfallClass = 761, PulseClass = 1225;

    /// <summary>One strip vertex in native Z-up space with its two heights and base texture coordinate.</summary>
    public readonly record struct Vertex(float X, float Y, float Z0, float Z1, float U, float V)
    {
        /// <summary>
        /// The builder's per-vertex wobble seed: the sum of the position's raw float bit patterns,
        /// bits 16..23 (0x262618: <c>srl 0x10 / andi 0xff</c> of <c>x_bits + y_bits</c>).
        /// </summary>
        public int WobbleSeed =>
            (int)((uint)(BitConverter.SingleToInt32Bits(X) + BitConverter.SingleToInt32Bits(Y)) >> 16) & 0xff;
    }

    /// <summary>
    /// One two-texture scrolling strip. The surface is drawn twice with the same triangle strip: once
    /// with <see cref="Texture0"/> / <see cref="Alpha0"/> and the first scroll, once with
    /// <see cref="Texture1"/> / <see cref="Alpha1"/> and the second, both alpha-blended by the
    /// constant GS ALPHA FIX / 128.
    /// </summary>
    public sealed record Layer(
        int NativeClass,
        int SourceAddress,
        IReadOnlyList<Vertex> Vertices,
        float Rate0,
        float Rate1,
        float WobbleAmplitude,
        int FrameStep,
        float Du0, float Dv0, float Du1, float Dv1,
        byte ColourR, byte ColourG, byte ColourB,
        int Texture0,
        int Texture1,
        byte Alpha0,
        byte Alpha1,
        bool PulsesHeight);

    private readonly record struct SetSpec(int NativeClass, int Address, int Count, bool Pulses, bool Ripples);

    // Class 676 (8 layers), 678 (2), 761 (4, rippled on first update), 1225 (3).
    private static readonly SetSpec[] Sets =
    [
        new(676, 0x1e2fc0, 8, false, false),
        new(678, 0x1e3380, 2, false, false),
        new(761, 0x1fbc80, 4, true, true),
        new(1225, 0x202d80, 3, true, false),
    ];

    private const int RecordSize = 0x60;
    private const int MaximumVertices = 256;

    public static IReadOnlyList<Layer> Read(Rac1ExecutableOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var layers = new List<Layer>();
        // Class 761's first update (0x2feb58) lowers every vertex by 0.45 and offsets alternating
        // pairs by +-0.05; the parity counter runs across the four layers.
        int ripple = 1;
        foreach (var set in Sets)
        {
            for (int i = 0; i < set.Count; i++)
            {
                if (set.Ripples) ripple--;
                var layer = ReadLayer(overlay, set, set.Address + i * RecordSize, ref ripple);
                layers.Add(layer);
            }
        }
        return layers;
    }

    private static Layer ReadLayer(Rac1ExecutableOverlay overlay, SetSpec set, int record, ref int ripple)
    {
        var r = overlay.Read(record, RecordSize);
        int verts = BinaryPrimitives.ReadInt32LittleEndian(r), uvs = BinaryPrimitives.ReadInt32LittleEndian(r[4..]);
        int count = BinaryPrimitives.ReadInt32LittleEndian(r[0xc..]);
        if (count is < 4 or > MaximumVertices || (count & 1) != 0)
            throw new InvalidDataException($"Novalis scrolling water record 0x{record:x} has an invalid vertex count {count}.");
        var vertexBytes = overlay.Read(verts, count * 16);
        var uvBytes = overlay.Read(uvs, count * 8);
        float F(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadSingleLittleEndian(bytes[at..]);
        var vertices = new float[count][];
        for (int v = 0; v < count; v++)
            vertices[v] = [F(vertexBytes, v * 16), F(vertexBytes, v * 16 + 4), F(vertexBytes, v * 16 + 8), F(vertexBytes, v * 16 + 12)];

        if (set.Ripples)
        {
            for (int v = 0; v < count; v++)
                vertices[v][3] = vertices[v][2] -= 0.45f;
            for (int v = 0; v < count; v += 2)
            {
                float sign = (ripple & 1) != 0 ? 1f : -1f;
                vertices[v][2] += 0.05f * sign;
                vertices[v][3] -= 0.05f * sign;
                vertices[v + 1][2] -= 0.05f * sign;
                vertices[v + 1][3] += 0.05f * sign;
                ripple++;
            }
        }

        var result = new Vertex[count];
        for (int v = 0; v < count; v++)
        {
            var p = vertices[v];
            if (!float.IsFinite(p[0]) || !float.IsFinite(p[1]) || !float.IsFinite(p[2]) || !float.IsFinite(p[3]))
                throw new InvalidDataException($"Novalis scrolling water record 0x{record:x} has a non-finite vertex.");
            result[v] = new(p[0], p[1], p[2], p[3], F(uvBytes, v * 8), F(uvBytes, v * 8 + 4));
        }

        uint colour = BinaryPrimitives.ReadUInt32LittleEndian(r[0x30..]);
        return new Layer(
            set.NativeClass, record, result,
            Rate0: F(r, 0x10), Rate1: F(r, 0x14), WobbleAmplitude: F(r, 0x18),
            FrameStep: BinaryPrimitives.ReadInt32LittleEndian(r[0x1c..]),
            Du0: F(r, 0x20), Dv0: F(r, 0x24), Du1: F(r, 0x28), Dv1: F(r, 0x2c),
            ColourR: (byte)colour, ColourG: (byte)(colour >> 8), ColourB: (byte)(colour >> 16),
            Texture0: BinaryPrimitives.ReadInt32LittleEndian(r[0x34..]),
            Texture1: BinaryPrimitives.ReadInt32LittleEndian(r[0x38..]),
            Alpha0: r[0x3c], Alpha1: r[0x3d],
            PulsesHeight: set.Pulses);
    }

    /// <summary>
    /// The height blend the vertex builder reads from record +0x5c: <c>z0 * mix + z1 * (1 - mix)</c>.
    /// Classes 761 and 1225 set it to <c>0.5 + 0.5 sin((frame &amp; 63) - 32) * pi / 32)</c>; the other
    /// classes leave it at zero, so their surface sits at the second height.
    /// </summary>
    public static float HeightMix(bool pulses, long frame) =>
        pulses ? (float)(Math.Sin(((int)(frame & 0x3f) - 32) * 0.09817477) * 0.5 + 0.5) : 0f;

    /// <summary>The scroll accumulator after <paramref name="frame"/> frames, wrapped to the native [-1, 1] range.</summary>
    public static float Scroll(float direction, float rate, long frame)
    {
        double value = direction * (double)rate * frame;
        value -= Math.Floor(value / 2d) * 2d;      // [0, 2)
        return (float)(value > 1d ? value - 2d : value);
    }
}
