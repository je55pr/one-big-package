using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Retail tables and constants of the class-806 Nanotech orb cluster (update 0x2d89f0, draw callback
/// 0x2d9810). Every level links the same compiled object at its own addresses, so the blocks are located
/// by content and authenticated against payload-free SHA-256 witnesses taken from the NTSC-U original.
/// Nothing here is committed retail data; it is read from the player's own disc at load.
/// </summary>
public sealed class Rac1NanotechOrbAssets
{
    public const int ParticleTextureTable = 62, GlintTextureTable = 53;
    public const int ClusterPointCount = 290, RingVertexCount = 96, RingQuadCount = 32, ChromeQuadCount = 4;

    /// <summary>Named $gp-relative floats/words of the orb code; the comment gives their offset below $gp.</summary>
    public sealed record Constants(
        float SpinStep,            // 0x5210: shared orbit rotation per tick (4 degrees)
        int OrbFrameStep,          // 0x520c: orb sprite rotation byte step per tick
        float SparkleSize,         // 0x5208: raw particle size of trail sparkles
        float OrbSize,             // 0x5204: raw particle size of an orb
        uint SparkleColor,         // 0x5200
        float BobStep,             // 0x51f4: bob phase per tick
        float BobAmplitude,        // 0x51f0
        float OrbRadius,           // 0x51ec
        int TrailFrames,           // 0x51a8
        float BreathAmplitude,     // 0x51a4: orb distance = amplitude * sin(phase) + OrbRadius while resting
        float GlintSpeedLow,       // 0x51a0
        float GlintSpeedHigh,      // 0x519c
        int GlintLife,             // 0x5198
        uint GlintColorA,          // 0x5194
        uint GlintColorB,          // 0x5190
        float GlintSizeA,          // 0x518c
        float GlintSizeB,          // 0x5188
        uint TrailColorA,          // 0x5184
        uint TrailColorB,          // 0x5180
        float[] TrailFrameSteps,   // 0x5170..: sprite rotation step per tick, four glows
        float[] TrailSizes,        // 0x5160..
        float[] TrailHeights,      // 0x5150..
        float TargetOffset,        // 0x51ac: flight target is the hero less this along the hero axis
        float CurveSide,           // 0x51b0: length of the sideways control offset
        int FlightLifeLow,         // 0x51bc
        int FlightLifeHigh,        // 0x51b8
        int FlightTimer,           // 0x51c4: frames before the cluster retires after launch
        int GlowTexture,           // 0x512c / 0x511c: Fx texture of the cluster mesh and chrome pass
        float RingScale,           // 0x5124
        uint RingColorLow,         // 0x5120
        float RingOuterScale);     // 0x5114

    public Constants K { get; }
    public IReadOnlyList<Vector3> ClusterPoints { get; }
    public IReadOnlyList<Vector2> ClusterUv { get; }
    public IReadOnlyList<uint> ClusterColors { get; }
    public IReadOnlyList<Vector3> RingVertices { get; }
    public IReadOnlyList<short[]> RingQuads { get; }
    public IReadOnlyList<float> PhaseSpeeds { get; }
    /// <summary>The 290-point mesh as its three overlapping GS triangle strips (146/109/36 vertices after the first), cached once.</summary>
    public IReadOnlyList<Strip> Strips { get; }
    public sealed record Strip(int Start, int Count, Vector3[] Points, Vector2[] Uv, uint[] Colors);
    private static readonly (int Start, int Count)[] StripRanges = [(0, 148), (146, 109), (254, 36)];
    public IReadOnlyDictionary<int, RuntimeTexture> FxTextures { get; }
    public Rac1EffectAssets Particles { get; }

    /// <summary>Retail loading goes through <see cref="Decode"/>; tests build synthetic instances through this constructor.</summary>
    internal Rac1NanotechOrbAssets(Constants k, Vector3[] points, Vector2[] uvs, uint[] colors, Vector3[] ring, short[][] ringQuads,
        float[] speeds, IReadOnlyDictionary<int, RuntimeTexture> fx, Rac1EffectAssets particles)
    {
        K = k; ClusterPoints = points; ClusterUv = uvs; ClusterColors = colors; RingVertices = ring; RingQuads = ringQuads;
        PhaseSpeeds = speeds; FxTextures = fx; Particles = particles;
        Strips = StripRanges.Select(range => new Strip(range.Start, range.Count, points[range.Start..(range.Start + range.Count)],
            uvs[range.Start..(range.Start + range.Count)], colors[range.Start..(range.Start + range.Count)])).ToArray();
    }

    // Bit patterns of the eight orbit phase speeds (2.5 / 4.5 / 1 .. 6 degrees per tick): the locating key.
    private static readonly uint[] SpeedWords =
        [0x3d8efa35, 0x3d0efa35, 0x3dd67750, 0x3d567750, 0x3c8efa35, 0x3db2b8c2, 0x3d32b8c2, 0x3da0d97c];
    private static readonly uint[] ConstantsKey = [0x3d0efa35, 0x3d75c28f, 0x3e800000]; // bob step, bob amplitude, orb radius
    private const int ConstantsKeyOffset = 0x1c, ConstantsSize = 0x100;

    // (offset from the speeds block, size, SHA-256) measured on the authority overlay (Veldin 0x1e14a0..).
    private static readonly (string Name, int Offset, int Size, string Sha256)[] Blocks =
    [
        ("speeds", 0x0000, 0x20, "e72cedd7e708f828017d898a40d4295a9e5dd12a396839549ea53f919cd31e2d"),
        ("points", 0x0020, 290 * 16, "d7de0bb8b147d1445ee2efd13bc2050ac89450c681228bd32cdb5bdb6cd214f5"),
        ("st", 0x1240, 290 * 8, "0219cbba0b4e77438c33131b6232bb756f496b46f4b2ace9291d582a12e8c9e3"),
        ("rgba", 0x1b50, 290 * 4, "357d1e7f41ae964f6a04d6250b1d738fb0ac3a5f4cef4c0cc9f7b805b8eb1913"),
        ("ring vertices", 0x1fe0, 0x600, "5cad1af2504101b24e1b260398ede777b7c68f99b54692e7e63519c8d3aacfc7"),
        ("ring indices", 0x25e0, 0x100, "5e1208c491ba649e78c57ce397eb8a53032579c7a6fa677a41c8f2df50f48f95"),
        ("chrome vertices", 0x26e0, 0x100, "a86546e50c1656e16d4af46dcc0425ccfbbf78b36026cb60a3195fc35d943453"),
        ("chrome indices", 0x27e0, 0x20, "6d393bf799abce5f21c38500f916d075404119fadf5383f3af73732107cf0e08"),
    ];
    private const string ConstantsSha256 = "94ab39140091a542a2c5580b73bed1434eedafe9d096e9bc9fba9432c0cd9836";

    private static byte[] Words(IEnumerable<uint> words)
    {
        var bytes = new byte[words.Count() * 4];
        int at = 0;
        foreach (uint word in words) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), word); at += 4; }
        return bytes;
    }

    private static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>Locates and authenticates the tables in <paramref name="overlay"/>; throws if this level's object differs from the retail witness.</summary>
    public static Rac1NanotechOrbAssets Decode(Rac1ExecutableOverlay overlay, Rac1LevelCore.Core core)
    {
        var candidates = overlay.FindBlock(Words(SpeedWords));
        var speedMatches = candidates.Where(address => Blocks.All(block =>
        {
            try { return Hash(overlay.Read(address + block.Offset, block.Size)) == block.Sha256; }
            catch (InvalidDataException) { return false; }
        })).ToArray();
        if (speedMatches.Length == 0) throw new InvalidDataException("This level's overlay does not contain the authenticated class-806 orb tables.");
        if (speedMatches.Length > 1) throw new InvalidDataException("This level's overlay contains more than one authenticated copy of the class-806 orb tables.");
        int? speeds = speedMatches[0];
        var constantMatches = overlay.FindBlock(Words(ConstantsKey)).Select(hit => hit - ConstantsKeyOffset)
            .Where(address =>
            {
                try { return Hash(overlay.Read(address, ConstantsSize)) == ConstantsSha256; }
                catch (InvalidDataException) { return false; }
            }).ToArray();
        if (constantMatches.Length == 0) throw new InvalidDataException("This level's overlay does not contain the authenticated class-806 constants.");
        if (constantMatches.Length > 1) throw new InvalidDataException("This level's overlay contains more than one authenticated copy of the class-806 constants.");
        int? constants = constantMatches[0];

        byte[] At(string name) { var block = Blocks.Single(b => b.Name == name); return overlay.Read(speeds.Value + block.Offset, block.Size).ToArray(); }
        static float F(byte[] data, int at) => BitConverter.ToSingle(data, at);
        var points = At("points"); var st = At("st"); var rgba = At("rgba"); var ring = At("ring vertices");
        var clusterPoints = Enumerable.Range(0, ClusterPointCount).Select(i => new Vector3(F(points, i * 16), F(points, i * 16 + 4), F(points, i * 16 + 8))).ToArray();
        var clusterUv = Enumerable.Range(0, ClusterPointCount).Select(i => new Vector2(F(st, i * 8), F(st, i * 8 + 4))).ToArray();
        var clusterColors = Enumerable.Range(0, ClusterPointCount).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(rgba.AsSpan(i * 4, 4))).ToArray();
        var ringVertices = Enumerable.Range(0, RingVertexCount).Select(i => new Vector3(F(ring, i * 16), F(ring, i * 16 + 4), F(ring, i * 16 + 8))).ToArray();
        var indices = At("ring indices");
        var ringQuads = Enumerable.Range(0, RingQuadCount).Select(q => Enumerable.Range(0, 4)
            .Select(j => BinaryPrimitives.ReadInt16LittleEndian(indices.AsSpan(q * 8 + j * 2, 2))).ToArray()).ToArray();
        if (ringQuads.Any(quad => quad.Any(index => (uint)index >= RingVertexCount)))
            throw new InvalidDataException("Orb ring quad references a missing vertex.");
        var speedValues = Enumerable.Range(0, 8).Select(i => F(At("speeds"), i * 4)).ToArray();

        // $gp-relative block: the word at "K below $gp" sits at constants + (0x5210 - K).
        var block = overlay.Read(constants.Value, ConstantsSize).ToArray();
        int Slot(int k) => 0x5210 - k;
        float KF(int k) => F(block, Slot(k));
        int KI(int k) => BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(Slot(k), 4));
        uint KU(int k) => BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(Slot(k), 4));
        float[] Floats(int first) => Enumerable.Range(0, 4).Select(i => KF(first - i * 4)).ToArray();
        var constantsRecord = new Constants(KF(0x5210), KI(0x520c), KF(0x5208), KF(0x5204), KU(0x5200), KF(0x51f4), KF(0x51f0), KF(0x51ec),
            KI(0x51a8), KF(0x51a4), KF(0x51a0), KF(0x519c), KI(0x5198), KU(0x5194), KU(0x5190), KF(0x518c), KF(0x5188), KU(0x5184), KU(0x5180),
            Floats(0x5170), Floats(0x5160), Floats(0x5150), KF(0x51ac), KF(0x51b0), KI(0x51bc), KI(0x51b8), KI(0x51c4), KI(0x512c),
            KF(0x5124), KU(0x5120), KF(0x5114));
        if (KI(0x512c) != KI(0x511c)) throw new InvalidDataException("Orb cluster mesh and chrome pass no longer share one texture.");
        var fx = Rac1FxTextures.Read(core, new HashSet<int> { constantsRecord.GlowTexture, Rac1BeamSlot.EndpointTexture });
        return new(constantsRecord, clusterPoints, clusterUv, clusterColors, ringVertices, ringQuads, speedValues, fx,
            Rac1EffectAssets.DecodeNanotech(core));
    }
}
