using OBP.Core.Math;

namespace OBP.Runtime.Presentation;

/// <summary>A rigid transform decomposed from a 4x4 object matrix, for display.</summary>
public readonly record struct TransformReadout(Vec3 Translation, (double X, double Y, double Z, double W) Rotation, Vec3 Scale);

/// <summary>One opaque payload surfaced to the inspector — presence, format tag and size only, never decoded.</summary>
public readonly record struct PayloadReadout(string Format, int ByteLength);

/// <summary>Playback state for one animated mesh, for the inspector / HUD.</summary>
public readonly record struct AnimationReadout(string Name, double FramesPerSecond, int FrameCount, int CurrentFrame, bool Playing, bool HasSkeleton);

/// <summary>
/// The engine-independent readout for a picked thing in a loaded world: what the
/// host learned from the ray hit (asset kind, texture, the welded mesh, or a
/// preserved <see cref="RuntimeDynamicObject"/> / <see cref="RuntimeAnimatedMesh"/>)
/// plus the world's provenance. Built by <see cref="WorldObjectDescriptorBuilder"/>;
/// the Godot inspector only renders it. Native payload bytes are surfaced as
/// (format, length) — interpreting them is a decoder chain's job.
/// </summary>
public sealed record WorldObjectDescriptor(
    string Category,
    string AssetKind,
    int TextureId,
    int? TextureWidth,
    int? TextureHeight,
    AlphaProfile? TextureAlpha,
    bool BackFaceCulled,
    bool VertexColoured,
    int? TriangleIndex,
    Vec3? HitPoint,
    int? NativeClassId,
    int? InstanceIndex,
    int? NativeUid,
    string? ModelRef,
    string? InteractionId,
    TransformReadout? Transform,
    ObpBounds? LocalBounds,
    int? TriangleCount,
    System.Collections.Generic.IReadOnlyList<PayloadReadout> Payloads,
    AnimationReadout? Animation,
    string Game,
    string BuildId,
    int LevelId,
    string WorldName);

/// <summary>
/// A hit the Godot <c>WorldPicker</c> resolved against a built scene, in neutral
/// terms — the descriptor builder turns this + the <see cref="RuntimeWorld"/>
/// into a <see cref="WorldObjectDescriptor"/>.
/// </summary>
public sealed record WorldHit(
    string AssetKind,
    int TextureId,
    Vec3? Point = null,
    int? TriangleIndex = null,
    RuntimeDynamicObject? DynamicObject = null,
    RuntimeAnimatedMesh? AnimatedMesh = null,
    AnimationReadout? AnimationState = null);

public static class WorldObjectDescriptorBuilder
{
    public static WorldObjectDescriptor Build(RuntimeWorld world, WorldHit hit)
    {
        var tex = FindTexture(world, hit.AssetKind, hit.TextureId);
        AlphaProfile? alpha = tex is { } t ? AlphaProfile.Analyse(t.Rgba) : null;

        var obj = hit.DynamicObject;
        var anim = hit.AnimatedMesh;

        string category =
            obj is not null ? "dynamic object"
            : anim is not null ? "animated mesh"
            : hit.AssetKind switch
            {
                "moby-marker" => "moby marker",
                "sky" => "sky shell",
                _ => "welded " + hit.AssetKind,
            };

        return new WorldObjectDescriptor(
            Category: category,
            AssetKind: hit.AssetKind,
            TextureId: hit.TextureId,
            TextureWidth: tex?.Width,
            TextureHeight: tex?.Height,
            TextureAlpha: alpha,
            BackFaceCulled: obj is null && anim is null && MaterialModel.BackFaceCull(hit.AssetKind),
            VertexColoured: HasVertexColour(world, hit, obj, anim),
            TriangleIndex: hit.TriangleIndex,
            HitPoint: hit.Point,
            NativeClassId: obj?.NativeClassId,
            InstanceIndex: obj?.InstanceIndex,
            NativeUid: obj?.NativeUid,
            ModelRef: obj?.ModelRef,
            InteractionId: obj?.InteractionId,
            Transform: obj is { } o ? Decompose(o.Transform.Matrix) : null,
            LocalBounds: obj is { } ob ? BoundsOf(ob) : null,
            TriangleCount: obj is { } otc ? otc.Meshes.Sum(m => m.TriangleCount) : anim?.TriangleCount,
            Payloads: (obj?.NativePayloads ?? System.Array.Empty<RuntimeOpaquePayload>())
                .Select(p => new PayloadReadout(p.Format, p.Data.Length)).ToArray(),
            Animation: hit.AnimationState ?? (anim is { } a
                ? new AnimationReadout(a.Name, a.FramesPerSecond, a.Frames.Count, 0, true, a.Skeleton is not null)
                : null),
            Game: world.Game,
            BuildId: world.BuildId,
            LevelId: world.LevelId,
            WorldName: world.DisplayName);
    }

    private static RuntimeTexture? FindTexture(RuntimeWorld world, string kind, int id)
    {
        foreach (var t in world.Textures)
        {
            if (t.TextureId == id && string.Equals(t.AssetKind, kind, System.StringComparison.Ordinal))
            {
                return t;
            }
        }

        return null;
    }

    private static bool HasVertexColour(RuntimeWorld world, WorldHit hit, RuntimeDynamicObject? obj, RuntimeAnimatedMesh? anim)
    {
        if (anim is not null)
        {
            return anim.Colors.Length > 0;
        }

        if (obj is not null)
        {
            return obj.Meshes.Any(m => m.Colors is { Length: > 0 });
        }

        foreach (var m in world.Meshes)
        {
            if (m.TextureId == hit.TextureId && m.AssetKind == hit.AssetKind)
            {
                return m.Colors is { Length: > 0 } && MaterialModel.VertexColourAsAlbedo(hit.AssetKind, true);
            }
        }

        return false;
    }

    private static ObpBounds BoundsOf(RuntimeDynamicObject obj)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        foreach (var m in obj.Meshes)
        {
            for (int i = 0; i + 2 < m.Positions.Length; i += 3)
            {
                minX = System.Math.Min(minX, m.Positions[i]);
                maxX = System.Math.Max(maxX, m.Positions[i]);
                minY = System.Math.Min(minY, m.Positions[i + 1]);
                maxY = System.Math.Max(maxY, m.Positions[i + 1]);
                minZ = System.Math.Min(minZ, m.Positions[i + 2]);
                maxZ = System.Math.Max(maxZ, m.Positions[i + 2]);
            }
        }

        return double.IsFinite(minX)
            ? new ObpBounds(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ))
            : new ObpBounds(Vec3.Zero, Vec3.Zero);
    }

    /// <summary>Decompose a column-major 4x4 (OBP Y-up) into translation, a unit quaternion and per-axis scale.</summary>
    public static TransformReadout Decompose(double[] m)
    {
        if (m.Length != 16)
        {
            return new TransformReadout(Vec3.Zero, (0, 0, 0, 1), new Vec3(1, 1, 1));
        }

        var translation = new Vec3(m[12], m[13], m[14]);
        var cx = new Vec3(m[0], m[1], m[2]);
        var cy = new Vec3(m[4], m[5], m[6]);
        var cz = new Vec3(m[8], m[9], m[10]);
        double sx = Len(cx), sy = Len(cy), sz = Len(cz);

        double[] r =
        {
            sx > 0 ? cx.X / sx : 1, sx > 0 ? cx.Y / sx : 0, sx > 0 ? cx.Z / sx : 0,
            sy > 0 ? cy.X / sy : 0, sy > 0 ? cy.Y / sy : 1, sy > 0 ? cy.Z / sy : 0,
            sz > 0 ? cz.X / sz : 0, sz > 0 ? cz.Y / sz : 0, sz > 0 ? cz.Z / sz : 1,
        };

        var q = QuaternionFromRotation(r);
        return new TransformReadout(translation, q, new Vec3(sx, sy, sz));
    }

    private static double Len(Vec3 v) => System.Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

    private static (double X, double Y, double Z, double W) QuaternionFromRotation(double[] r)
    {
        double m00 = r[0], m10 = r[1], m20 = r[2];
        double m01 = r[3], m11 = r[4], m21 = r[5];
        double m02 = r[6], m12 = r[7], m22 = r[8];
        double trace = m00 + m11 + m22;

        double x, y, z, w;
        if (trace > 0)
        {
            double s = System.Math.Sqrt(trace + 1.0) * 2;
            w = 0.25 * s;
            x = (m21 - m12) / s;
            y = (m02 - m20) / s;
            z = (m10 - m01) / s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            double s = System.Math.Sqrt(1.0 + m00 - m11 - m22) * 2;
            w = (m21 - m12) / s;
            x = 0.25 * s;
            y = (m01 + m10) / s;
            z = (m02 + m20) / s;
        }
        else if (m11 > m22)
        {
            double s = System.Math.Sqrt(1.0 + m11 - m00 - m22) * 2;
            w = (m02 - m20) / s;
            x = (m01 + m10) / s;
            y = 0.25 * s;
            z = (m12 + m21) / s;
        }
        else
        {
            double s = System.Math.Sqrt(1.0 + m22 - m00 - m11) * 2;
            w = (m10 - m01) / s;
            x = (m02 + m20) / s;
            y = (m12 + m21) / s;
            z = 0.25 * s;
        }

        double n = System.Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));
        return n > 0 ? (x / n, y / n, z / n, w / n) : (0, 0, 0, 1);
    }
}
