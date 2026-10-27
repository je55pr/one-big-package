using System.Buffers.Binary;
using System.Numerics;
using OBP.Core;
using OBP.IO;
using OBP.RAC3.Level;
using M = OBP.RAC3.Gameplay.Rac3ParticleMath;

namespace OBP.RAC3.Gameplay;

/// <summary>UYA type-97 sprite descriptor math (TABLE1 0x45ee98), before VU1/GS rasterization.</summary>
public sealed class Rac3ParticleDraw
{
    private readonly float[] _cosines;
    private readonly ulong _tex0Template;

    public Rac3ParticleDraw(ReadOnlySpan<float> cosines, ulong tex0Template)
    {
        if (cosines.Length != 256 || cosines.ContainsAnyExceptInRange(-1f, 1f) || cosines.Contains(float.NaN))
            throw new ArgumentException("UYA sprite drawing requires its 256 finite native cosine entries.", nameof(cosines));
        _cosines = cosines.ToArray(); _tex0Template = tex0Template;
    }

    public static Rac3ParticleDraw ReadVeldin(Rac3LevelCore.OpenedLevel level)
    {
        if (level.Row.TableIndex != 1) throw new NotSupportedException("The measured UYA draw-table placements are TABLE1-qualified.");
        byte[] overlay = level.PrimaryReader.Read(level.Core.Data.Overlay.Offset, level.Core.Data.Overlay.Size);
        byte[] Read(uint address, int size)
        {
            for (int at = 0; at + 16 <= overlay.Length;)
            {
                uint destination = BinaryPrimitives.ReadUInt32LittleEndian(overlay.AsSpan(at));
                uint count = BinaryPrimitives.ReadUInt32LittleEndian(overlay.AsSpan(at + 4));
                uint kind = BinaryPrimitives.ReadUInt32LittleEndian(overlay.AsSpan(at + 8));
                if (count > overlay.Length - at - 16) throw new InvalidDataException("UYA overlay section exceeds its bounded image.");
                if (kind is 1 or 8 && address >= destination && (ulong)address + (uint)size <= (ulong)destination + count)
                    return overlay.AsSpan(at + 16 + checked((int)(address - destination)), size).ToArray();
                at += 16 + (int)count;
            }
            throw new InvalidDataException("UYA native draw table is unavailable in this overlay.");
        }
        byte[] bytes = Read(0x2bfda0, 1024); var cosines = new float[256];
        for (int i = 0; i < cosines.Length; i++) cosines[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(4 * i));
        return new(cosines, BinaryPrimitives.ReadUInt64LittleEndian(Read(0x1da050, 8)));
    }
    /// <summary>Caller supplies the native view's depth row and projection W factor, and admits frustum visibility.
    /// Depth rejection/fade is native; this method does not infer a Godot camera or run VU1.</summary>
    public Sprite? Build(Rac3ParticleSession.Particle p, View view, Rac3PartTextureLayout layout, uint cachePage)
    {
        if (!Finite(view.Eye) || !Finite(view.DepthRow) || !float.IsFinite(view.ProjectionW) || view.ProjectionW <= 0 || view.FarDepthLimit < 1)
            throw new ArgumentException("UYA sprite view inputs must be finite and have a positive depth/projection range.", nameof(view));
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
        int dimensionLog2 = layout.DimensionLog2;
        // 0x45f438's PSMT8 cache TEX0. The physical cache page is supplied, never invented as an asset identity.
        ulong page = cachePage >> 8;
        ulong tex0 = _tex0Template | (ulong)dimensionLog2 << 26 | (ulong)dimensionLog2 << 30 | page << 37 | unchecked(cachePage + 1024u) >> 8;
        return new(p.Owner.Source, p.TextureSource, relative, axes, color, p.Blend, tex0);
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    public readonly record struct View(Vector3 Eye, Vector3 DepthRow, float ProjectionW, int FarDepthLimit);
    public readonly record struct Sprite(ObpSourceRef EmitterSource, ObpSourceRef TextureSource, Vector3 RelativePosition,
        Vector2 ProjectedAxes, uint Color, byte Blend, ulong NativeTex0)
    {
        /// <summary>Derived 64-byte sprite descriptor, with its transient material-cache TEX0 value.</summary>
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
}
