using System.Buffers.Binary;
using System.Numerics;
using OBP.PS2.Graphics;
using OBP.RAC2.Level;
using M = OBP.RAC2.Gameplay.Rac2ParticleMath;

namespace OBP.RAC2.Gameplay;

/// <summary>GC type-85 sprite descriptor math (Museum 0x3310e8), before VU1/GS rasterization.</summary>
public sealed class Rac2ParticleDraw
{
    private readonly float[] _cosines;
    private readonly ulong _tex0Template;

    public Rac2ParticleDraw(ReadOnlySpan<float> cosines, ulong tex0Template)
    {
        if (cosines.Length != 256 || cosines.ContainsAnyExceptInRange(-1f, 1f) || cosines.Contains(float.NaN))
            throw new ArgumentException("GC sprite drawing requires its 256 finite native cosine entries.", nameof(cosines));
        _cosines = cosines.ToArray(); _tex0Template = tex0Template;
    }

    public static Rac2ParticleDraw ReadMuseum(Rac2LevelOverlay.Image overlay)
    {
        byte[] bytes = overlay.ReadVirtual(0x265860, 1024);
        var cosines = new float[256];
        for (int i = 0; i < cosines.Length; i++) cosines[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4));
        return new(cosines, BinaryPrimitives.ReadUInt64LittleEndian(overlay.ReadVirtual(0x1ac950, 8)));
    }

    /// <summary>Caller supplies the native view's depth row and projection W factor, and admits frustum visibility.
    /// Depth rejection/fade is native; this method does not infer a Godot camera or run VU1.</summary>
    public RcParticleSpriteDescriptor? Build(Rac2ParticleSession.Particle p, View view, Rac2PartTextureLayout layout, uint cachePage)
    {
        if (!Finite(view.Eye) || !Finite(view.DepthRow) || !float.IsFinite(view.ProjectionW) || view.ProjectionW <= 0 || view.FarDepthLimit < 1)
            throw new ArgumentException("GC sprite view inputs must be finite and have a positive depth/projection range.", nameof(view));
        Vector3 relative = new(M.Subtract(p.Position.X, view.Eye.X), M.Subtract(p.Position.Y, view.Eye.Y), M.Subtract(p.Position.Z, view.Eye.Z));
        float depth = M.Add(M.Add(M.Multiply(view.DepthRow.X, relative.X), M.Multiply(view.DepthRow.Y, relative.Y)), M.Multiply(view.DepthRow.Z, relative.Z));
        double scaledDepth = (double)depth * 4096;
        int z = (int)Math.Clamp(scaledDepth, int.MinValue, int.MaxValue);
        int near = (p.DepthRange & 15) << 10, far = Math.Min((p.DepthRange & 0xf0) << 13, view.FarDepthLimit);
        if (z <= near || z >= far) return null;
        int fade = Math.Min(4096, Math.Min(z - near, (far - z) >> 4));
        uint color = p.Color & 0xffffff | (uint)(fade * (p.Color >> 24) >> 12) << 24;
        float q = M.VuDivide(M.Multiply(p.NativeSize, view.ProjectionW), M.Add(depth, .5f));
        Vector2 axes = new(M.Multiply(_cosines[p.Angle], q), M.Multiply(_cosines[(p.Angle + 192) & 255], q));
        int dimensionLog2 = BitOperations.Log2((uint)layout.Dimension);
        if (layout.Dimension is < 1 or > 1024 || (1 << dimensionLog2) != layout.Dimension)
            throw new ArgumentException("GC Part sprite dimensions must be powers of two in the admitted slice.", nameof(layout));
        // 0x331688's PSMT8 cache TEX0. The physical cache page is supplied, never invented as an asset identity.
        ulong page = cachePage >> 8;
        ulong tex0 = _tex0Template | (ulong)dimensionLog2 << 26 | (ulong)dimensionLog2 << 30 | page << 37 | unchecked(cachePage + 1024u) >> 8;
        return new(p.Source, p.TextureSource, relative, axes, color, p.Blend, tex0);
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    public readonly record struct View(Vector3 Eye, Vector3 DepthRow, float ProjectionW, int FarDepthLimit);
}
