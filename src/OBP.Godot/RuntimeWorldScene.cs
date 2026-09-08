using Godot;
using OBP.Core.Math;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.Godot;

/// <summary>
/// Adapter: a neutral <see cref="RuntimeWorld"/> (pure data, no engine or
/// game-format types) → a Godot scene sub-tree. One <see cref="ArrayMesh"/> /
/// <see cref="MeshInstance3D"/> per welded per-material render mesh, decoded RGBA
/// → <see cref="ImageTexture"/> / <see cref="StandardMaterial3D"/>, and a
/// <see cref="StaticBody3D"/> + trimesh <see cref="CollisionShape3D"/> per decoded
/// collision blob. This is the only place Godot and the runtime world model meet,
/// and it is game-independent — the same builder serves the GC importer today and
/// the R&amp;C1 / Up Your Arsenal importers later.
///
/// <para>
/// <c>RuntimeWorld</c> coordinates are "OBP space": native Z-up <c>(x, y, z)</c>
/// mapped to <c>(x, z, y)</c> by the importer. That swap is an <em>odd</em>
/// permutation, i.e. a reflection — the level is a mirror image of the real game.
/// <see cref="ToScene"/> corrects the handedness (negates X) for everything the
/// adapter emits: geometry, collision, camera and the player spawn.
/// </para>
/// </summary>
public static class RuntimeWorldScene
{
    /// <summary>OBP space → Godot: un-mirror by negating X (see the type remarks).</summary>
    public static Vector3 ToScene(double x, double y, double z) => new((float)(-x), (float)y, (float)z);

    /// <summary>OBP-space yaw (radians about +Y) → Godot yaw after the X flip.</summary>
    public static float ToSceneYaw(double obpYaw) => (float)(-obpYaw);

    public sealed record Result(
        Node3D Root,
        Node3D? SkyRoot,
        int MeshInstances,
        int Triangles,
        int Textures,
        int CollisionBodies,
        int CollisionTriangles,
        int TieInstances,
        int ShrubInstances,
        int MobyInstances,
        int AnimatedMobies = 0,
        System.Collections.Generic.IReadOnlyList<AnimatedMesh>? AnimatedMeshes = null,
        int DynamicObjects = 0,
        System.Collections.Generic.IReadOnlyList<DynamicObjectNode>? DynamicObjectNodes = null);

    /// <summary>Godot node plus its neutral runtime identity for one preserved dynamic object.</summary>
    public sealed record DynamicObjectNode(RuntimeDynamicObject Source, Node3D Root);

    /// <summary>Advance every animated moby in <paramref name="result"/> by one render tick — call from the host's <c>_Process</c>.</summary>
    public static void AdvanceAnimated(Result result, double ticks = 1.0)
    {
        if (result.AnimatedMeshes is null)
        {
            return;
        }

        foreach (var am in result.AnimatedMeshes)
        {
            am.Advance(ticks);
        }
    }

    public sealed record Options
    {
        /// <summary>Render the sky shells (grouped under <see cref="Result.SkyRoot"/> so the host can pin them to the camera).</summary>
        public bool IncludeSky { get; init; } = true;

        /// <summary>Render the death-height plane (a large debug quad well below the level).</summary>
        public bool IncludeDeathPlane { get; init; }

        /// <summary>Render the moby instance markers (cubes for triggers / spawners with no decoded mesh).</summary>
        public bool IncludeMobyMarkers { get; init; } = true;

        /// <summary>Build a <see cref="StaticBody3D"/> + trimesh collider per decoded blob — the surface the debug capsule stands on.</summary>
        public bool IncludeCollision { get; init; } = true;

        /// <summary>Also add a translucent debug overlay mesh over each collision body.</summary>
        public bool ShowCollisionDebug { get; init; }

        /// <summary>Build only the animated mobies (skip static geometry, collision, sky) — the MobySequence showcase.</summary>
        public bool OnlyAnimatedMobies { get; init; }
    }

    public static Result Build(RuntimeWorld world, string name = "World", Options? options = null)
    {
        options ??= new Options();
        var root = new Node3D { Name = name };
        var skyRoot = new Node3D { Name = "Sky" };

        // Decoded textures + every Godot material for this world come from the
        // single factory; material decisions live in OBP.Runtime.MaterialModel.
        var mats = new WorldMaterialFactory(world);

        int meshInstances = 0;
        int triangles = 0;
        int skyMeshes = 0;

        foreach (var m in world.Meshes)
        {
            if (options.OnlyAnimatedMobies)
            {
                break;
            }

            if (m.Indices.Length < 3 || m.Positions.Length < 9)
            {
                continue;
            }

            bool isSky = m.AssetKind == "sky";
            if (isSky && !options.IncludeSky)
            {
                continue;
            }

            // Missing texture is not normally permission to render a sky surface —
            // without its per-vertex colours a bare shell renders as a flat faceted
            // blob that swallows the view. A source importer may explicitly
            // authorise a materialless surface when retail evidence supplies its
            // presentation semantics (UYA's gouraud backdrop with importer-provided
            // vertex colour); the camera-followed textured cloud layers stay, and
            // the background clear colour is the backdrop.
            if (isSky &&
                mats.TextureFor(m.AssetKind, m.TextureId) is null &&
                !m.RenderWithoutTexture)
            {
                continue;
            }

            if (m.AssetKind == "death-plane" && !options.IncludeDeathPlane)
            {
                continue;
            }

            if (m.AssetKind == "moby-marker" && !options.IncludeMobyMarkers)
            {
                continue;
            }

            int vertexCount = m.Positions.Length / 3;
            var verts = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                verts[i] = ToScene(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
            }

            // ToScene negates X — a reflection — which reverses every triangle's
            // winding. Swap two corners back so front-faces point outward again
            // (lets solid geometry cull its back-faces; keeps normals sane).
            var indices = new int[m.Indices.Length];
            for (int t = 0; t + 3 <= m.Indices.Length; t += 3)
            {
                indices[t] = m.Indices[t];
                indices[t + 1] = m.Indices[t + 2];
                indices[t + 2] = m.Indices[t + 1];
            }

            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Index] = indices;

            bool hasUv = m.Uvs.Length == vertexCount * 2;
            if (hasUv)
            {
                var uv = new Vector2[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    uv[i] = new Vector2(m.Uvs[i * 2], m.Uvs[i * 2 + 1]);
                }

                arrays[(int)Mesh.ArrayType.TexUV] = uv;
            }

            bool hasColor = m.Colors is { } cc && cc.Length == vertexCount * 4;
            if (hasColor)
            {
                // tfrag baked colours are the level's static lighting in a dark
                // gamma space — MaterialModel.BakeCurve lifts them so shadowed
                // terrain doesn't crush and lit terrain still lifts. Moby vertex
                // colours are an already-linear normal shade — passed straight.
                bool bake = MaterialModel.UsesBakeCurve(m.AssetKind);
                var col = new Color[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    float r = m.Colors![i * 4], g = m.Colors[i * 4 + 1], b = m.Colors[i * 4 + 2];
                    if (bake)
                    {
                        r = MaterialModel.BakeCurve(r);
                        g = MaterialModel.BakeCurve(g);
                        b = MaterialModel.BakeCurve(b);
                    }

                    col[i] = new Color(r, g, b, m.Colors[i * 4 + 3]);
                }

                arrays[(int)Mesh.ArrayType.Color] = col;
            }

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            var mat = mats.StaticMesh(m.AssetKind, m.TextureId, hasUv, hasColor, isSky, m.RenderWithoutTexture, m.TriangleCount);

            var mi = new MeshInstance3D
            {
                Name = $"{m.AssetKind}_{m.TextureId}",
                Mesh = mesh,
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };

            if (isSky)
            {
                mi.Layers = 1;
                skyRoot.AddChild(mi);
                skyMeshes++;
            }
            else
            {
                root.AddChild(mi);
            }

            meshInstances++;
            triangles += m.TriangleCount;
        }

        if (skyMeshes > 0)
        {
            root.AddChild(skyRoot);
        }
        else
        {
            // Never parented (no sky, or IncludeSky = false) — free it now so it
            // isn't left as an orphan node for the lifetime of the process.
            skyRoot.Free();
        }

        // --- preserved dynamic objects: one root per native gameplay instance.
        // Their meshes stay local to that root, so the host can address/hide/move
        // one object without rebuilding the welded static world. ---
        int dynamicObjectCount = 0;
        var dynamicNodes = new System.Collections.Generic.List<DynamicObjectNode>();
        if (!options.OnlyAnimatedMobies)
        {
            foreach (var obj in world.DynamicObjects ?? System.Array.Empty<RuntimeDynamicObject>())
            {
                if (obj.Transform.Matrix.Length != 16 || obj.Meshes.Count == 0)
                {
                    continue;
                }

                var objRoot = new Node3D
                {
                    Name = $"dyn_{obj.SourceGame}_{obj.NativeClassId}_{obj.InstanceIndex}",
                    Transform = ToSceneTransform(obj.Transform),
                };
                int childMeshes = 0;

                foreach (var m in obj.Meshes)
                {
                    if (m.Indices.Length < 3 || m.Positions.Length < 9)
                    {
                        continue;
                    }

                    int vertexCount = m.Positions.Length / 3;
                    var verts = new Vector3[vertexCount];
                    for (int i = 0; i < vertexCount; i++)
                    {
                        verts[i] = ToScene(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
                    }

                    var indices = new int[m.Indices.Length];
                    for (int t = 0; t + 3 <= m.Indices.Length; t += 3)
                    {
                        indices[t] = m.Indices[t];
                        indices[t + 1] = m.Indices[t + 2];
                        indices[t + 2] = m.Indices[t + 1];
                    }

                    var arrays = new global::Godot.Collections.Array();
                    arrays.Resize((int)Mesh.ArrayType.Max);
                    arrays[(int)Mesh.ArrayType.Vertex] = verts;
                    arrays[(int)Mesh.ArrayType.Index] = indices;

                    bool hasUv = m.Uvs.Length == vertexCount * 2;
                    if (hasUv)
                    {
                        var uv = new Vector2[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                        {
                            uv[i] = new Vector2(m.Uvs[i * 2], m.Uvs[i * 2 + 1]);
                        }

                        arrays[(int)Mesh.ArrayType.TexUV] = uv;
                    }

                    bool hasColor = m.Colors is { } cc && cc.Length == vertexCount * 4;
                    if (hasColor)
                    {
                        var col = new Color[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                        {
                            col[i] = new Color(
                                m.Colors![i * 4], m.Colors[i * 4 + 1], m.Colors[i * 4 + 2], m.Colors[i * 4 + 3]);
                        }

                        arrays[(int)Mesh.ArrayType.Color] = col;
                    }

                    var mesh = new ArrayMesh();
                    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                    var mat = mats.Instanced(m.AssetKind, m.TextureId, hasColor);

                    objRoot.AddChild(new MeshInstance3D
                    {
                        Name = $"{m.AssetKind}_{m.TextureId}",
                        Mesh = mesh,
                        MaterialOverride = mat,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    });
                    childMeshes++;
                    meshInstances++;
                    triangles += m.TriangleCount;
                }

                if (childMeshes > 0)
                {
                    root.AddChild(objRoot);
                    dynamicNodes.Add(new DynamicObjectNode(obj, objRoot));
                    dynamicObjectCount++;
                }
            }
        }

        // --- animated mobies: one CPU-skinned node per instance/texture group,
        // kept out of the merged static soup so it can swap frames over time ---
        int animatedMeshInstances = 0;
        var animatedPlayers = new System.Collections.Generic.List<AnimatedMesh>();
        foreach (var am in world.AnimatedMeshes ?? System.Array.Empty<RuntimeAnimatedMesh>())
        {
            if (am.Frames.Count == 0 || am.Indices.Length < 3 || am.VertexCount < 3)
            {
                continue;
            }

            // ToScene negates X (a reflection) — reverse every triangle's winding
            // to match, exactly as the static mesh loop does.
            var idx = new int[am.Indices.Length];
            for (int t = 0; t + 3 <= am.Indices.Length; t += 3)
            {
                idx[t] = am.Indices[t];
                idx[t + 1] = am.Indices[t + 2];
                idx[t + 2] = am.Indices[t + 1];
            }

            int vc = am.VertexCount;
            var uv = new Vector2[vc];
            for (int i = 0; i < vc && i * 2 + 1 < am.Uvs.Length; i++)
            {
                uv[i] = new Vector2(am.Uvs[i * 2], am.Uvs[i * 2 + 1]);
            }

            bool hasCol = am.Colors.Length == vc * 4;
            var col = hasCol ? new Color[vc] : null;
            for (int i = 0; hasCol && i < vc; i++)
            {
                col![i] = new Color(am.Colors[i * 4], am.Colors[i * 4 + 1], am.Colors[i * 4 + 2], am.Colors[i * 4 + 3]);
            }

            // Per-frame scene-space vertex arrays (X un-mirrored).
            var frames = new Vector3[am.Frames.Count][];
            for (int f = 0; f < am.Frames.Count; f++)
            {
                var src = am.Frames[f];
                var dst = new Vector3[vc];
                for (int v = 0; v < vc; v++)
                {
                    dst[v] = ToScene(src[v * 3], src[v * 3 + 1], src[v * 3 + 2]);
                }

                frames[f] = dst;
            }

            var mat = mats.Instanced(am.AssetKind, am.TextureId, hasCol);

            var animMesh = new ArrayMesh();
            var mi = new MeshInstance3D
            {
                Name = am.Name,
                Mesh = animMesh,
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            root.AddChild(mi);
            animatedPlayers.Add(new AnimatedMesh(mi, animMesh, frames, idx, uv, col, am.FramesPerSecond));
            animatedMeshInstances++;
            triangles += am.TriangleCount;
        }

        if (mats.UntexturedTriangleReport.Count > 0)
        {
            string report = string.Join(", ", mats.UntexturedTriangleReport.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:N0}"));
            GD.Print($"[RuntimeWorldScene] untextured triangles (fallback tint): {report}");
        }

        int collisionBodies = 0;
        int collisionTriangles = 0;
        if (options.IncludeCollision && !options.OnlyAnimatedMobies)
        {
            for (int i = 0; i < world.CollisionMeshes.Count; i++)
            {
                var blob = world.CollisionMeshes[i];
                if (blob.Indices.Length < 3 || blob.Positions.Length < 9)
                {
                    continue;
                }

                // ConcavePolygonShape3D wants a flat triangle soup: 3 vertices per
                // face, un-indexed. The RC octree triangulation has inconsistent
                // winding, which makes a CharacterBody3D fall through faces whose
                // normal points away — so emit every triangle twice, both windings.
                int triCount = blob.Indices.Length / 3;
                var faces = new Vector3[triCount * 6];
                for (int t = 0; t < triCount; t++)
                {
                    var a = Vertex(blob, blob.Indices[t * 3]);
                    var b = Vertex(blob, blob.Indices[t * 3 + 1]);
                    var c = Vertex(blob, blob.Indices[t * 3 + 2]);
                    faces[t * 6] = a;
                    faces[t * 6 + 1] = b;
                    faces[t * 6 + 2] = c;
                    faces[t * 6 + 3] = c;
                    faces[t * 6 + 4] = b;
                    faces[t * 6 + 5] = a;
                }

                var body = new StaticBody3D { Name = $"Collision{i}" };
                body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces } });
                root.AddChild(body);
                collisionBodies++;
                collisionTriangles += blob.Triangles;

                if (options.ShowCollisionDebug)
                {
                    body.AddChild(CollisionDebugMesh(blob, i));
                }
            }
        }

        int Kind(string k) => world.Meshes.Count(m => m.AssetKind == k);

        return new Result(
            root,
            skyMeshes > 0 ? skyRoot : null,
            meshInstances,
            triangles,
            mats.TextureCount,
            collisionBodies,
            collisionTriangles,
            Kind("tie"),
            Kind("shrub"),
            Kind("moby"),
            animatedMeshInstances,
            animatedPlayers,
            dynamicObjectCount,
            dynamicNodes);
    }

    /// <summary>Convert an OBP-space object matrix to Godot's X-unmirrored scene transform.</summary>
    private static Transform3D ToSceneTransform(RuntimeObjectTransform transform)
    {
        var m = transform.Matrix;
        if (m.Length != 16)
        {
            return Transform3D.Identity;
        }

        // Scene conversion is F*M*F where F = diag(-1, 1, 1, 1): local
        // vertices are X-flipped by ToScene(), so the basis must be conjugated
        // by the same reflection and the world-space translation X-flipped.
        var x = new Vector3((float)m[0], (float)-m[1], (float)-m[2]);
        var y = new Vector3((float)-m[4], (float)m[5], (float)m[6]);
        var z = new Vector3((float)-m[8], (float)m[9], (float)m[10]);
        var origin = new Vector3((float)-m[12], (float)m[13], (float)m[14]);
        return new Transform3D(new Basis(x, y, z), origin);
    }

    private static Vector3 Vertex(RuntimeCollisionBlob blob, int index)
    {
        int v = index * 3;
        return ToScene(blob.Positions[v], blob.Positions[v + 1], blob.Positions[v + 2]);
    }

    private static MeshInstance3D CollisionDebugMesh(RuntimeCollisionBlob blob, int index)
    {
        var verts = new Vector3[blob.VertexCount];
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i] = ToScene(blob.Positions[i * 3], blob.Positions[i * 3 + 1], blob.Positions[i * 3 + 2]);
        }

        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Index] = blob.Indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var tint = index == 0 ? new Color(0.20f, 0.85f, 1.00f) : new Color(1.00f, 0.55f, 0.20f);
        return new MeshInstance3D
        {
            Name = $"CollisionDebug{index}",
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(tint.R, tint.G, tint.B, 0.35f),
                RenderPriority = 1,
            },
        };
    }

    /// <summary>Park a camera outside the world bounds, looking at their centre.</summary>
    public static void FrameCamera(Camera3D camera, ObpBounds bounds, float azimuthDegrees = 35f, float elevationDegrees = 28f)
    {
        var a = ToScene(bounds.Min.X, bounds.Min.Y, bounds.Min.Z);
        var b = ToScene(bounds.Max.X, bounds.Max.Y, bounds.Max.Z);
        var min = new Vector3(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y), Mathf.Min(a.Z, b.Z));
        var max = new Vector3(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y), Mathf.Max(a.Z, b.Z));
        var centre = (min + max) * 0.5f;
        float radius = Mathf.Max(1f, (max - min).Length() * 0.5f);

        float az = Mathf.DegToRad(azimuthDegrees);
        float el = Mathf.DegToRad(elevationDegrees);
        var dir = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));

        camera.Position = centre + dir * (radius * 1.0f);
        camera.LookAt(centre, Vector3.Up);
        camera.Far = radius * 12f;
        camera.Near = Mathf.Max(0.05f, radius * 0.01f);
    }
}

/// <summary>
/// One CPU-skinned animated moby: cycles a pre-baked list of per-frame
/// scene-space vertex arrays into a single <see cref="MeshInstance3D"/>.
///
/// <para>This is a plain object, not a <see cref="Node"/> — <c>OBP.Godot</c> has
/// no Godot source generator, so an engine <c>_Process</c> callback on a node
/// subclass defined here would never fire. The host ticks it every frame via
/// <see cref="RuntimeWorldScene.AdvanceAnimated"/>.</para>
/// </summary>
public sealed class AnimatedMesh
{
    private readonly Vector3[][] _frames;
    private readonly int[] _indices;
    private readonly Vector2[] _uv;
    private readonly Color[]? _colors;
    private readonly ArrayMesh _mesh;
    private readonly float _framesPerTick;
    private double _cursor;
    private int _current = -1;

    internal AnimatedMesh(
        MeshInstance3D instance, ArrayMesh mesh, Vector3[][] frames,
        int[] indices, Vector2[] uv, Color[]? colors, float framesPerSecond)
    {
        Instance = instance;
        _mesh = mesh;
        _frames = frames;
        _indices = indices;
        _uv = uv;
        _colors = colors;
        _framesPerTick = Mathf.Clamp(framesPerSecond / 60f, 0.02f, 2f);
        Apply(0);
    }

    public MeshInstance3D Instance { get; }

    /// <summary>Advance the animation; <paramref name="ticks"/> = elapsed render frames (1 per <c>_Process</c>).</summary>
    public void Advance(double ticks = 1.0)
    {
        if (_frames.Length < 2)
        {
            return;
        }

        _cursor += _framesPerTick * ticks;
        Apply((int)(_cursor % _frames.Length));
    }

    private void Apply(int frame)
    {
        if (frame == _current || frame < 0 || frame >= _frames.Length)
        {
            return;
        }

        _current = frame;
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _frames[frame];
        arrays[(int)Mesh.ArrayType.Index] = _indices;
        if (_uv.Length == _frames[frame].Length)
        {
            arrays[(int)Mesh.ArrayType.TexUV] = _uv;
        }

        if (_colors is not null && _colors.Length == _frames[frame].Length)
        {
            arrays[(int)Mesh.ArrayType.Color] = _colors;
        }

        _mesh.ClearSurfaces();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
}
