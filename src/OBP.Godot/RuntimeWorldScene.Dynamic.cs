using Godot;
using System.Buffers.Binary;
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
        private RuntimeObjectAnimationSet? _configuredAnimations;
        private readonly Dictionary<int, byte[]> _animationVertexBuffers = [];
        public RuntimeObjectAnimationSample? AnimationSample { get; private set; }

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
            if (AnimationSample is not null) return;
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

        /// <summary>Attach source-admitted clips to the existing authored render surfaces.</summary>
        public void ConfigureAnimations(RuntimeObjectAnimationSet animations) =>
            _configuredAnimations = animations ?? throw new ArgumentNullException(nameof(animations));

        public void ApplyAnimationSample(RuntimeObjectAnimationSample sample)
        {
            if (sample == AnimationSample) return;
            var animations = _configuredAnimations ?? Source.Animations
                ?? throw new InvalidOperationException("Object has no source-admitted animation clips.");
            if (_surfaceNodes.Count != Source.Meshes.Count)
                throw new InvalidDataException("Animated object render surfaces do not match its source.");
            for (int surface = 0; surface < _surfaceNodes.Count; surface++)
            {
                var positions = Evaluate(sample, surface, 0);
                if (!_animationVertexBuffers.TryGetValue(surface, out var buffer))
                {
                    UpdateDynamicSurfaceMesh(_surfaceNodes[surface], Source.Meshes[surface], positions, dynamic: true);
                    buffer = new byte[checked(positions.Length * sizeof(float))];
                    _animationVertexBuffers.Add(surface, buffer);
                }
                UpdateDynamicSurfacePositions(_surfaceNodes[surface], positions, buffer);
            }
            AnimationSample = sample;
            _restPoseApplied = false;

            double[] Evaluate(RuntimeObjectAnimationSample pose, int surfaceIndex, int depth)
            {
                if (depth > 64 || !float.IsFinite(pose.Fraction) || pose.Fraction is < 0 or > 1 ||
                    !float.IsFinite(pose.BlendFraction) || pose.BlendFraction is < 0 or > 1)
                    throw new InvalidDataException("Object animation sample has invalid interpolation/blend state.");
                var clip = animations.Clips.Single(clip => clip.Id == pose.ClipId);
                var frames = clip.Surfaces.Single(surface => surface.SurfaceIndex == surfaceIndex).LocalFrames;
                if ((uint)pose.Frame >= frames.Count || (uint)pose.NextFrame >= frames.Count)
                    throw new InvalidDataException("Object animation sample references a missing frame.");
                var from = frames[pose.Frame];
                var to = frames[pose.NextFrame];
                var result = new double[from.Length];
                for (int at = 0; at < result.Length; at++) result[at] = from[at] + (to[at] - from[at]) * pose.Fraction;
                if (pose.BlendFrom is { } previous && pose.BlendFraction < 1)
                {
                    var old = Evaluate(previous, surfaceIndex, depth + 1);
                    for (int at = 0; at < result.Length; at++) result[at] = old[at] + (result[at] - old[at]) * pose.BlendFraction;
                }
                return result;
            }
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
        IReadOnlyList<double> positions,
        bool dynamic = false)
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
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays,
            flags: dynamic ? Mesh.ArrayFormat.FlagUseDynamicUpdate : 0);
        node.Mesh = mesh;
    }

    private static void UpdateDynamicSurfacePositions(MeshInstance3D node, double[] positions, byte[] buffer)
    {
        if (positions.Length == 0 || positions.Length % 3 != 0 || buffer.Length != positions.Length * sizeof(float))
            throw new InvalidDataException("Animated vertex buffer differs from its admitted surface.");
        Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int at = 0; at < positions.Length; at += 3)
        {
            Vector3 point = ToScene(positions[at], positions[at + 1], positions[at + 2]);
            minimum = minimum.Min(point); maximum = maximum.Max(point);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(at * sizeof(float)), point.X);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan((at + 1) * sizeof(float)), point.Y);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan((at + 2) * sizeof(float)), point.Z);
        }
        var mesh = (ArrayMesh)node.Mesh;
        mesh.SurfaceUpdateVertexRegion(0, 0, buffer);
        mesh.CustomAabb = new(minimum, maximum - minimum);
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
                    Name = $"dyn_{OBP.Core.SourceIdentityWire.FormatSource(obj.SourceGame)}_{obj.NativeClassId}_{obj.InstanceIndex}",
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
