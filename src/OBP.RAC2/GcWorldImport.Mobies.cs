using System.Buffers.Binary;
using OBP.Core.Math;
using OBP.IO;
using OBP.PS2.Collision;
using OBP.PS2.Compression;
using OBP.PS2.Iso;
using OBP.PS2.Geometry;
using OBP.PS2.Graphics;
using OBP.PS2.Presentation;
using OBP.RAC2.Audio;
using OBP.RAC2.Geometry;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;
using OBP.Runtime;
using OBP.Runtime.Audio;
using OBP.Runtime.Presentation;

namespace OBP.RAC2;

/// <summary>
/// Native world assembly for a Going Commando retail level — the C# equivalent of
/// <c>reference-ts/tools/gc-world.mjs</c>. Opens the level WAD from an ISO,
/// decodes chunk-0 tfrags + octree collision, instances the tie / shrub / moby
/// classes (per-texture grouping + vertex welding, native Z-up → OBP Y-up), adds
/// moby instance markers, the sky shells and a death-height plane, and grows the
/// world bounds.
///
/// <para>
/// Output is a neutral <see cref="RuntimeWorld"/> — no GC or Godot types cross
/// this boundary. This is where GC-specific data conversion terminates.
/// </para>
public static partial class GcWorldImport
{
    private static List<RuntimeAnimatedMesh> BuildAnimatedMeshes(
        GcInstances.Gameplay gameplay,
        Dictionary<int, GcUyaMoby.MobyClass> classes,
        out HashSet<int> animatedInstances)
    {
        const int MaxAnimatedInstances = 12;
        animatedInstances = [];
        var outp = new List<RuntimeAnimatedMesh>();

        foreach (var inst in gameplay.MobyInstances)
        {
            if (animatedInstances.Count >= MaxAnimatedInstances)
            {
                break;
            }

            if (!classes.TryGetValue(inst.OClass, out var cls) || cls.Mesh.Indices.Length == 0)
            {
                continue;
            }

            // Single-joint rigid classes only: their rest pose is reproduced
            // exactly (identity-frame invariant), so playback is retail-faithful
            // without the partially-pinned multi-joint skeleton risk.
            if (cls.Joints.Count != 1)
            {
                continue;
            }

            var seq = cls.Sequences
                .Where(s => s.Frames.Count >= 8)
                .OrderByDescending(s => s.Frames.Count)
                .FirstOrDefault();
            if (seq is null)
            {
                continue;
            }

            double sc = inst.Scale == 0 ? 1 : inst.Scale;
            var m = GcInstances.MatrixFromPosRotScale(
                (inst.Position.X, inst.Position.Y, inst.Position.Z),
                (inst.Rotation.X, inst.Rotation.Y, inst.Rotation.Z), sc);

            var frames = new List<double[]>(seq.Frames.Count);
            foreach (var frame in seq.Frames)
            {
                var posed = OBP.RAC2.Animation.MobyAnimation.Pose(cls.Mesh, cls.Joints, frame);
                var world = new double[posed.Length];
                for (int i = 0; i < posed.Length; i += 3)
                {
                    var (x, y, z) = GcInstances.TransformPoint(m, posed[i], posed[i + 1], posed[i + 2]);
                    world[i] = R2(x);
                    world[i + 1] = R2(z);
                    world[i + 2] = R2(y);
                }

                frames.Add(world);
            }

            // Flat per-vertex shade from the instance's baked ambient.
            int vc = cls.Mesh.Positions.Length / 3;
            var colours = new float[vc * 4];
            for (int v = 0; v < vc; v++)
            {
                colours[v * 4] = (float)System.Math.Clamp(inst.LightColour.R, 0.05, 1.0);
                colours[v * 4 + 1] = (float)System.Math.Clamp(inst.LightColour.G, 0.05, 1.0);
                colours[v * 4 + 2] = (float)System.Math.Clamp(inst.LightColour.B, 0.05, 1.0);
                colours[v * 4 + 3] = 1f;
            }

            float speed = seq.Frames.Count > 0 ? seq.Frames[0].Speed : 0.5f;
            float fps = (float)System.Math.Clamp((speed <= 0 ? 0.5 : speed) * 60.0, 6.0, 60.0);

            // One animated mesh per triangle-texture group (mirrors the static
            // moby split); frames are reference-shared across the groups.
            var bySurface = new Dictionary<(int TextureId, RuntimeMaterialPresentation? Presentation), List<int>>();
            for (int f = 0; f < cls.TriangleTextureIds.Length; f++)
            {
                int tex = cls.TriangleTextureIds[f];
                var presentation = PresentationFor(
                    cls.Mesh.Materials, cls.Mesh.TriangleMaterialStateIndices, null, f,
                    classifyMobySurface: true);
                var surface = (tex, presentation);
                if (!bySurface.TryGetValue(surface, out var list))
                {
                    bySurface[surface] = list = [];
                }

                list.Add(cls.Mesh.Indices[f * 3]);
                list.Add(cls.Mesh.Indices[f * 3 + 1]);
                list.Add(cls.Mesh.Indices[f * 3 + 2]);
            }

            foreach (var (surface, tris) in bySurface
                         .OrderBy(kv => kv.Key.TextureId)
                         .ThenBy(kv => kv.Key.Presentation?.ToString(), StringComparer.Ordinal))
            {
                outp.Add(new RuntimeAnimatedMesh(
                    Name: $"moby{inst.OClass}_i{inst.Index}_t{surface.TextureId}",
                    AssetKind: "moby",
                    TextureId: surface.TextureId,
                    Uvs: cls.Mesh.Uvs,
                    Indices: tris.ToArray(),
                    Colors: colours,
                    Frames: frames,
                    FramesPerSecond: fps,
                    MaterialPresentation: surface.Presentation));
            }

            animatedInstances.Add(inst.Index);
        }

        return outp;
    }

    /// <summary>
    /// Lift selected native Mobies out of the welded static render soup while
    /// preserving their per-instance identity, transform, render surfaces and
    /// opaque authored state. This is the gameplay bridge: RuntimeWorld stays
    /// game-neutral while RAC2 decides which native classes need individuality.
    /// </summary>
    private static List<RuntimeDynamicObject> BuildDynamicMobyObjects(
        int level,
        GcInstances.Gameplay gameplay,
        Dictionary<int, GcUyaMoby.MobyClass> classes,
        IReadOnlyList<GcInstances.DirLight> dirLights,
        IReadOnlyList<GcInstances.PointLight> pointLights,
        IReadOnlySet<int> dynamicClasses,
        out HashSet<int> dynamicInstances)
    {
        dynamicInstances = [];
        var outp = new List<RuntimeDynamicObject>();
        var plYUp = pointLights
            .Select(p => (Pos: YUp(p.Position), p.Radius, p.Colour))
            .ToArray();

        foreach (var inst in gameplay.MobyInstances)
        {
            if (!dynamicClasses.Contains(inst.OClass))
            {
                continue;
            }

            if (!classes.TryGetValue(inst.OClass, out var cls) || cls.Mesh.Indices.Length == 0)
            {
                continue;
            }

            double sc = inst.Scale == 0 ? 1 : inst.Scale;
            var nativeMatrix = GcInstances.MatrixFromPosRotScale(
                (inst.Position.X, inst.Position.Y, inst.Position.Z),
                (inst.Rotation.X, inst.Rotation.Y, inst.Rotation.Z), sc);
            var obpMatrix = ToObpMatrix(nativeMatrix);

            var amb = inst.LightColour;
            GcInstances.DirLight? dl = inst.LightIndex >= 0 && inst.LightIndex < dirLights.Count
                ? dirLights[inst.LightIndex]
                : null;
            var (dAx, dAy, dAz) = dl is { } a ? YUp(a.DirectionA) : (0, 0, 0);
            var (dBx, dBy, dBz) = dl is { } b ? YUp(b.DirectionB) : (0, 0, 0);
            var instYUp = YUp(inst.Position);
            var nearPl = plYUp
                .Where(p => Dist2(p.Pos, instYUp) < (p.Radius + 40.0) * (p.Radius + 40.0))
                .ToArray();

            var meshData = cls.Mesh;
            bool haveNormals = meshData.Normals.Length == meshData.Positions.Length;
            var localPositions = new double[meshData.Positions.Length];
            var colours = new float[meshData.Positions.Length / 3 * 4];
            for (int i = 0; i < meshData.Positions.Length; i += 3)
            {
                // Class-local native Z-up -> class-local OBP Y-up. The full
                // instance transform remains separate in RuntimeObjectTransform.
                localPositions[i] = R2(meshData.Positions[i]);
                localPositions[i + 1] = R2(meshData.Positions[i + 2]);
                localPositions[i + 2] = R2(meshData.Positions[i + 1]);

                var (x, y, z) = GcInstances.TransformPoint(
                    nativeMatrix, meshData.Positions[i], meshData.Positions[i + 1], meshData.Positions[i + 2]);
                double lr = amb.R, lg = amb.G, lb = amb.B;
                if (haveNormals)
                {
                    var (nx, ny, nz) = WorldNormal(
                        nativeMatrix, meshData.Normals[i], meshData.Normals[i + 1], meshData.Normals[i + 2]);
                    if (dl is { } light)
                    {
                        double ka = System.Math.Max(0.0, -(nx * dAx + ny * dAy + nz * dAz));
                        double kb = System.Math.Max(0.0, -(nx * dBx + ny * dBy + nz * dBz));
                        lr += light.ColourA.X * ka + light.ColourB.X * kb;
                        lg += light.ColourA.Y * ka + light.ColourB.Y * kb;
                        lb += light.ColourA.Z * ka + light.ColourB.Z * kb;
                    }

                    foreach (var p in nearPl)
                    {
                        double vx = x - p.Pos.X, vy = z - p.Pos.Y, vz = y - p.Pos.Z;
                        double dist = System.Math.Sqrt(vx * vx + vy * vy + vz * vz);
                        if (dist >= p.Radius || dist < 1e-4)
                        {
                            continue;
                        }

                        double atten = 1.0 - dist / p.Radius;
                        double ndl = System.Math.Max(0.0, -(nx * vx + ny * vy + nz * vz) / dist);
                        double c = atten * atten * ndl;
                        lr += p.Colour.R * c;
                        lg += p.Colour.G * c;
                        lb += p.Colour.B * c;
                    }
                }

                int v = i / 3;
                colours[v * 4] = (float)System.Math.Clamp(lr, 0.03, 1.0);
                colours[v * 4 + 1] = (float)System.Math.Clamp(lg, 0.03, 1.0);
                colours[v * 4 + 2] = (float)System.Math.Clamp(lb, 0.03, 1.0);
                colours[v * 4 + 3] = 1f;
            }

            var bySurface = new Dictionary<(int TextureId, RuntimeMaterialPresentation? Presentation), List<int>>();
            for (int f = 0; f < cls.TriangleTextureIds.Length; f++)
            {
                int tex = cls.TriangleTextureIds[f];
                var presentation = PresentationFor(
                    meshData.Materials, meshData.TriangleMaterialStateIndices, null, f,
                    classifyMobySurface: true);
                var surface = (tex, presentation);
                if (!bySurface.TryGetValue(surface, out var tris))
                {
                    bySurface[surface] = tris = [];
                }

                tris.Add(meshData.Indices[f * 3]);
                tris.Add(meshData.Indices[f * 3 + 1]);
                tris.Add(meshData.Indices[f * 3 + 2]);
            }

            var surfaces = bySurface
                .OrderBy(kv => kv.Key.TextureId)
                .ThenBy(kv => kv.Key.Presentation?.ToString(), StringComparer.Ordinal)
                .Select(kv => new RuntimeObjectMesh(
                    "moby", kv.Key.TextureId, localPositions, meshData.Uvs, kv.Value.ToArray(), colours,
                    kv.Key.Presentation))
                .ToArray();

            var payloads = new List<RuntimeOpaquePayload>
            {
                new("rac2-moby-instance-0x88", inst.RawInstance),
            };
            if (inst.PVarData is { } pvar)
            {
                payloads.Add(new RuntimeOpaquePayload("rac2-pvar", pvar));
            }

            outp.Add(new RuntimeDynamicObject(
                SourceGame: "rac2",
                NativeClassId: inst.OClass,
                InstanceIndex: inst.Index,
                NativeUid: inst.Uid,
                ModelRef: $"moby:{inst.OClass}",
                InteractionId: $"level:{level}:moby:{inst.Index}",
                Transform: new RuntimeObjectTransform(obpMatrix),
                Meshes: surfaces,
                NativePayloads: payloads,
                Animations: BuildAdmittedDynamicAnimationSet(cls, surfaces)));
            dynamicInstances.Add(inst.Index);
        }

        return outp;
    }

    private static RuntimeObjectAnimationSet? BuildAdmittedDynamicAnimationSet(
        GcUyaMoby.MobyClass cls,
        IReadOnlyList<RuntimeObjectMesh> surfaces)
    {
        if (cls.OClass != GcAranosOpeningDoorSession.NativeClassId)
            return null;

        if (cls.Joints.Count != 7 || cls.Mesh.Indices.Length / 3 != 120)
            throw new InvalidDataException("Aranos class-2755 door model drifted from the retail witness.");

        var opening = cls.Sequences.SingleOrDefault(sequence => sequence.Index == 1)
            ?? throw new InvalidDataException("Aranos class-2755 opening sequence 1 is absent.");
        var open = cls.Sequences.SingleOrDefault(sequence => sequence.Index == 2)
            ?? throw new InvalidDataException("Aranos class-2755 open sequence 2 is absent.");
        if (opening.Frames.Count != GcAranosOpeningDoorSession.OpeningSequenceFrames ||
            opening.Frames.Any(frame => frame.Speed != 0.5f) ||
            open.Frames.Count != 1)
            throw new InvalidDataException("Aranos class-2755 door animation no longer matches the retail witness.");

        var posedFrames = opening.Frames.Concat(open.Frames).Select(frame =>
        {
            var posed = OBP.RAC2.Animation.MobyAnimation.Pose(cls.Mesh, cls.Joints, frame);
            var local = new double[posed.Length];
            for (int i = 0; i < posed.Length; i += 3)
            {
                local[i] = R2(posed[i]);
                local[i + 1] = R2(posed[i + 2]);
                local[i + 2] = R2(posed[i + 1]);
            }
            return local;
        }).ToArray();

        var animatedSurfaces = Enumerable.Range(0, surfaces.Count)
            .Select(index => new RuntimeObjectAnimationSurface(index, posedFrames))
            .ToArray();
        double secondsPerFrame = 1d / GcAranosOpeningDoorSession.OpeningFrameRate;
        var durations = Enumerable.Repeat(secondsPerFrame, posedFrames.Length).ToArray();
        return new RuntimeObjectAnimationSet([
            new RuntimeObjectAnimationClip(
                "opening", RuntimeObjectAnimationRole.Reaction, animatedSurfaces, durations)
        ]);
    }

    /// <summary>Convert a native Z-up local-to-world matrix to the equivalent OBP Y-up matrix.</summary>
    private static double[] ToObpMatrix(IReadOnlyList<double> native)
    {
        int[] p = [0, 2, 1, 3];
        var outp = new double[16];
        for (int c = 0; c < 4; c++)
        {
            for (int r = 0; r < 4; r++)
            {
                outp[r + c * 4] = native[p[r] + p[c] * 4];
            }
        }

        return outp;
    }

    /// <summary>Package the gameplay lights into the neutral runtime lighting model (native Z-up → OBP Y-up).</summary>
    private static void PlaceMobyInstances(
        IReadOnlyList<GcInstances.MobyInstance> instances,
        Dictionary<int, GcUyaMoby.MobyClass> classes,
        IReadOnlyList<GcInstances.DirLight> dirLights,
        IReadOnlyList<GcInstances.PointLight> pointLights,
        List<RuntimeMesh> meshes,
        HashSet<int> skipInstances)
    {
        var bySurface = new Dictionary<(int TextureId, RuntimeMaterialPresentation? Presentation),
            (List<double> P, List<float> U, List<float> C, List<int> I,
                Dictionary<(double, double, double, double, double, double, double, double), int> Weld)>();

        // Point lights in OBP Y-up space (they are stored native Z-up).
        var plYUp = pointLights
            .Select(p => (Pos: YUp(p.Position), p.Radius, p.Colour))
            .ToArray();

        foreach (var inst in instances)
        {
            if (skipInstances.Contains(inst.Index))
            {
                continue;
            }

            if (!classes.TryGetValue(inst.OClass, out var cls) || cls.Mesh.Indices.Length == 0)
            {
                continue;
            }

            double sc = inst.Scale == 0 ? 1 : inst.Scale;
            var m = GcInstances.MatrixFromPosRotScale(
                (inst.Position.X, inst.Position.Y, inst.Position.Z),
                (inst.Rotation.X, inst.Rotation.Y, inst.Rotation.Z), sc);

            // The instance's static ambient, plus the one directional light it
            // references (native Z-up dirs -> OBP Y-up). See GcInstances.DirLight.
            var amb = inst.LightColour;
            GcInstances.DirLight? dl = inst.LightIndex >= 0 && inst.LightIndex < dirLights.Count
                ? dirLights[inst.LightIndex]
                : null;
            var (dAx, dAy, dAz) = dl is { } a ? YUp(a.DirectionA) : (0, 0, 0);
            var (dBx, dBy, dBz) = dl is { } b ? YUp(b.DirectionB) : (0, 0, 0);

            // Point lights whose sphere plausibly reaches this instance (only
            // mobies are point-lit). Instance origin in OBP Y-up.
            var instYUp = YUp(inst.Position);
            var nearPl = plYUp
                .Where(p => Dist2(p.Pos, instYUp) < (p.Radius + 40.0) * (p.Radius + 40.0))
                .ToArray();

            var meshData = cls.Mesh;
            bool haveNormals = meshData.Normals.Length == meshData.Positions.Length;
            var worldPos = new double[meshData.Positions.Length];
            var litR = new float[meshData.Positions.Length / 3];
            var litG = new float[meshData.Positions.Length / 3];
            var litB = new float[meshData.Positions.Length / 3];
            for (int i = 0; i < meshData.Positions.Length; i += 3)
            {
                var (x, y, z) = GcInstances.TransformPoint(m, meshData.Positions[i], meshData.Positions[i + 1], meshData.Positions[i + 2]);
                worldPos[i] = R2(x);
                worldPos[i + 1] = R2(z);
                worldPos[i + 2] = R2(y);

                double lr = amb.R, lg = amb.G, lb = amb.B;
                if (haveNormals)
                {
                    var (nx, ny, nz) = WorldNormal(m, meshData.Normals[i], meshData.Normals[i + 1], meshData.Normals[i + 2]);
                    if (dl is { } light)
                    {
                        // surface faces -direction to be lit (light-travel convention)
                        double ka = System.Math.Max(0.0, -(nx * dAx + ny * dAy + nz * dAz));
                        double kb = System.Math.Max(0.0, -(nx * dBx + ny * dBy + nz * dBz));
                        lr += light.ColourA.X * ka + light.ColourB.X * kb;
                        lg += light.ColourA.Y * ka + light.ColourB.Y * kb;
                        lb += light.ColourA.Z * ka + light.ColourB.Z * kb;
                    }

                    foreach (var p in nearPl)
                    {
                        double vx = x - p.Pos.X, vy = z - p.Pos.Y, vz = y - p.Pos.Z; // vertex is (x,z,y) in Y-up
                        double dist = System.Math.Sqrt(vx * vx + vy * vy + vz * vz);
                        if (dist >= p.Radius || dist < 1e-4)
                        {
                            continue;
                        }

                        double atten = 1.0 - dist / p.Radius;
                        double ndl = System.Math.Max(0.0, -(nx * vx + ny * vy + nz * vz) / dist); // toward the light
                        double c = atten * atten * ndl;
                        lr += p.Colour.R * c;
                        lg += p.Colour.G * c;
                        lb += p.Colour.B * c;
                    }
                }

                int vi3 = i / 3;
                litR[vi3] = (float)System.Math.Clamp(lr, 0.03, 1.0);
                litG[vi3] = (float)System.Math.Clamp(lg, 0.03, 1.0);
                litB[vi3] = (float)System.Math.Clamp(lb, 0.03, 1.0);
            }

            for (int f = 0; f < cls.TriangleTextureIds.Length; f++)
            {
                int tex = cls.TriangleTextureIds[f];
                var presentation = PresentationFor(
                    meshData.Materials, meshData.TriangleMaterialStateIndices, null, f,
                    classifyMobySurface: true);
                var surface = (tex, presentation);
                if (!bySurface.TryGetValue(surface, out var g))
                {
                    bySurface[surface] = g = ([], [], [], [],
                        new Dictionary<(double, double, double, double, double, double, double, double), int>());
                }

                for (int k = 0; k < 3; k++)
                {
                    int vi = meshData.Indices[f * 3 + k];
                    double px = worldPos[vi * 3], py = worldPos[vi * 3 + 1], pz = worldPos[vi * 3 + 2];
                    float s = meshData.Uvs[vi * 2], t = meshData.Uvs[vi * 2 + 1];
                    double rs = R4(s), rt = R4(t);
                    double cr = R2(litR[vi]), cg = R2(litG[vi]), cb = R2(litB[vi]);
                    var key = (px, py, pz, rs, rt, cr, cg, cb);
                    if (!g.Weld.TryGetValue(key, out int idx))
                    {
                        idx = g.P.Count / 3;
                        g.Weld[key] = idx;
                        g.P.Add(px);
                        g.P.Add(py);
                        g.P.Add(pz);
                        g.U.Add((float)rs);
                        g.U.Add((float)rt);
                        g.C.Add((float)cr);
                        g.C.Add((float)cg);
                        g.C.Add((float)cb);
                        g.C.Add(1f);
                    }

                    g.I.Add(idx);
                }
            }
        }

        foreach (var (surface, g) in bySurface
                     .OrderBy(kv => kv.Key.TextureId)
                     .ThenBy(kv => kv.Key.Presentation?.ToString(), StringComparer.Ordinal))
        {
            meshes.Add(new RuntimeMesh(
                "moby", surface.TextureId, g.P.ToArray(), g.U.ToArray(), g.I.ToArray(), g.C.ToArray(),
                MaterialPresentation: surface.Presentation));
        }
    }

    /// <summary>Native Z-up direction/vector -> OBP Y-up (swap y/z).</summary>
    private static void AddMobyMarkers(
        IReadOnlyList<GcInstances.MobyInstance> instances,
        Dictionary<int, GcUyaMoby.MobyClass> classes,
        List<RuntimeMesh> meshes,
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        if (instances.Count == 0 || double.IsPositiveInfinity(minX))
        {
            return;
        }

        double pad = 1.5 * System.Math.Max(maxX - minX, System.Math.Max(maxY - minY, maxZ - minZ));
        bool InRange(double x, double y, double z) =>
            x > minX - pad && x < maxX + pad && z > minY - pad && z < maxY + pad && y > minZ - pad && y < maxZ + pad;

        var positions = new List<double>();
        var indices = new List<int>();
        foreach (var moby in instances)
        {
            if (!InRange(moby.Position.X, moby.Position.Y, moby.Position.Z))
            {
                continue;
            }

            if (classes.TryGetValue(moby.OClass, out var cls) && cls.Mesh.Indices.Length > 0)
            {
                continue;
            }

            double sc = System.Math.Max(0.4, System.Math.Min(4, moby.Scale)) * 0.9;
            var m = GcInstances.MatrixFromPosRotScale(
                (moby.Position.X, moby.Position.Y, moby.Position.Z),
                (moby.Rotation.X, moby.Rotation.Y, moby.Rotation.Z), sc);
            int baseV = positions.Count / 3;
            foreach (var c in MarkerCube)
            {
                var (x, y, z) = GcInstances.TransformPoint(m, c[0], c[1], c[2]);
                positions.Add(R2(x));
                positions.Add(R2(z));
                positions.Add(R2(y));
            }

            foreach (var face in MarkerFaces)
            {
                indices.Add(baseV + face[0]);
                indices.Add(baseV + face[1]);
                indices.Add(baseV + face[2]);
            }
        }

        if (indices.Count > 0)
        {
            meshes.Add(new RuntimeMesh("moby-marker", -1, positions.ToArray(), [], indices.ToArray()));
        }
    }

}
