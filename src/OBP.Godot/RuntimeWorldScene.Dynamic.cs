using Godot;
using OBP.Core.Math;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Godot;

public static partial class RuntimeWorldScene
{
    // Authored dynamic entity presentation and its mesh construction.
    public sealed class RuntimeDynamicObjectRoot3D : Node3D
    {
        public RuntimeDynamicObject? Source { get; private set; }

        public void Configure(RuntimeDynamicObject source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }
    }

    public static RuntimeDynamicObjectRoot3D? FindDynamicObjectRoot(Node? node)
    {
        for (Node? current = node; current is not null; current = current.GetParent())
        {
            if (current is RuntimeDynamicObjectRoot3D root)
                return root;
        }

        return null;
    }

    public static RuntimeDynamicObject? FindDynamicObjectOwner(Node? node) =>
        FindDynamicObjectRoot(node)?.Source;

    /// <summary>Godot presentation handle for one preserved gameplay entity.</summary>
    public sealed class DynamicObjectNode
    {
        private readonly IReadOnlyList<MeshInstance3D> _surfaceNodes;
        private RuntimeObjectAnimationClip? _activeClip;
        private double _clipStartedAtSeconds;
        private int _appliedFrame = -1;
        private bool _restPoseApplied = true;

        public RuntimeDynamicObject Source { get; }
        public Node3D Root { get; }
        public RuntimeEntityState State { get; private set; }

        public DynamicObjectNode(
            RuntimeDynamicObject source,
            Node3D root,
            IReadOnlyList<MeshInstance3D>? surfaceNodes = null)
        {
            Source = source;
            Root = root;
            _surfaceNodes = surfaceNodes ?? Array.Empty<MeshInstance3D>();
            State = RuntimeEntityState.FromAuthored(source);
            ApplyState(State);
        }

        public void ApplyState(RuntimeEntityState state)
        {
            state.EnsureMatches(Source);
            bool roleChanged = state.Presentation.AnimationRole != State.Presentation.AnimationRole;
            State = state;
            Root.Visible = state.Presentation.Presence == RuntimeEntityPresence.Active;
            Root.Transform = ToSceneTransform(state.Presentation.Transform);
            if (roleChanged)
            {
                _activeClip = null;
                _appliedFrame = -1;
            }
        }

        /// <summary>Advance a source-backed dynamic-object clip on the shared world clock.</summary>
        public void AdvanceAnimation(double clockSeconds)
        {
            var clip = Source.Animations?.Find(State.Presentation.AnimationRole);
            if (clip is null)
            {
                RestoreRestPose();
                _activeClip = null;
                _appliedFrame = -1;
                return;
            }

            if (_surfaceNodes.Count != Source.Meshes.Count)
                return;
            if (_activeClip?.Id != clip.Id)
            {
                _activeClip = clip;
                _clipStartedAtSeconds = clockSeconds;
                _appliedFrame = -1;
            }

            int frame = clip.FrameIndexAt(Math.Max(0d, clockSeconds - _clipStartedAtSeconds));
            if (frame < 0 || frame == _appliedFrame)
                return;

            foreach (var surface in clip.Surfaces)
            {
                if (surface.SurfaceIndex < 0 || surface.SurfaceIndex >= _surfaceNodes.Count ||
                    frame >= surface.LocalFrames.Count)
                    throw new InvalidDataException($"Dynamic animation {clip.Id} has an invalid surface/frame mapping.");
                UpdateDynamicSurfaceMesh(
                    _surfaceNodes[surface.SurfaceIndex],
                    Source.Meshes[surface.SurfaceIndex],
                    surface.LocalFrames[frame]);
            }
            _appliedFrame = frame;
            _restPoseApplied = false;
        }

        private void RestoreRestPose()
        {
            if (_restPoseApplied || _surfaceNodes.Count != Source.Meshes.Count)
                return;
            for (int i = 0; i < _surfaceNodes.Count; i++)
                UpdateDynamicSurfaceMesh(_surfaceNodes[i], Source.Meshes[i], Source.Meshes[i].Positions);
            _restPoseApplied = true;
        }
    }

    private static void UpdateDynamicSurfaceMesh(
        MeshInstance3D node,
        RuntimeObjectMesh surface,
        IReadOnlyList<double> positions)
    {
        if (positions.Count != surface.Positions.Length)
            throw new InvalidDataException("Dynamic animation frame vertex count does not match its surface.");

        int vertexCount = positions.Count / 3;
        var verts = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
            verts[i] = ToScene(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);

        var indices = new int[surface.Indices.Length];
        for (int t = 0; t + 3 <= surface.Indices.Length; t += 3)
        {
            indices[t] = surface.Indices[t];
            indices[t + 1] = surface.Indices[t + 2];
            indices[t + 2] = surface.Indices[t + 1];
        }

        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        if (surface.Uvs.Length == vertexCount * 2)
        {
            var uv = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                uv[i] = new Vector2(surface.Uvs[i * 2], surface.Uvs[i * 2 + 1]);
            arrays[(int)Mesh.ArrayType.TexUV] = uv;
        }
        if (surface.Colors is { } colors && colors.Length == vertexCount * 4)
        {
            var col = new Color[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                col[i] = new Color(colors[i * 4], colors[i * 4 + 1], colors[i * 4 + 2], colors[i * 4 + 3]);
            arrays[(int)Mesh.ArrayType.Color] = col;
        }

        var mesh = node.Mesh as ArrayMesh ?? new ArrayMesh();
        mesh.ClearSurfaces();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        node.Mesh = mesh;
    }


    private static (int Count, int MeshInstances, int Triangles, IReadOnlyList<DynamicObjectNode> Nodes) BuildDynamicObjects(
        RuntimeWorld world, Node3D root, Options options, WorldMaterialFactory mats)
    {
        int meshInstances = 0;
        int triangles = 0;
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

                var objRoot = new RuntimeDynamicObjectRoot3D
                {
                    Name = $"dyn_{obj.SourceGame}_{obj.NativeClassId}_{obj.InstanceIndex}",
                    Transform = ToSceneTransform(obj.Transform),
                };
                objRoot.Configure(obj);
                int childMeshes = 0;
                var childMeshNodes = new System.Collections.Generic.List<MeshInstance3D>();

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
                    var mat = mats.Instanced(m.AssetKind, m.TextureId, hasColor, m.MaterialPresentation);

                    var childMesh = new MeshInstance3D
                    {
                        Name = $"{m.AssetKind}_{m.TextureId}",
                        Mesh = mesh,
                        MaterialOverride = mat,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    };
                    objRoot.AddChild(childMesh);
                    childMeshNodes.Add(childMesh);
                    childMeshes++;
                    meshInstances++;
                    triangles += m.TriangleCount;
                }

                if (childMeshes > 0)
                {
                    root.AddChild(objRoot);
                    dynamicNodes.Add(new DynamicObjectNode(obj, objRoot, childMeshNodes));
                    dynamicObjectCount++;
                }
            }
        }


        return (dynamicObjectCount, meshInstances, triangles, dynamicNodes);
    }
}
