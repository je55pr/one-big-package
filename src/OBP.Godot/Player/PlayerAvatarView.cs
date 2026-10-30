using Godot;
using System.Diagnostics;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;

namespace OBP.Godot.Player;

/// <summary>
/// Presentation-only Godot view of an engine-independent <see cref="PlayerAvatar"/>.
/// Avatar frames remain model-local; this node performs only local axis conversion,
/// optional geometric-base alignment, material construction, and deterministic frame display.
/// </summary>
public sealed class PlayerAvatarView : Node3D, IPlayerAnimationPresentationSink
{
    private sealed record SurfaceState(ArrayMesh Mesh, int[] Indices, Vector2[] Uvs);

    /// <summary>
    /// Pure playback of an already resolved, engine-neutral animation decision.
    /// Source-game sequence selection belongs to the provider/controller layer.
    /// </summary>
    public sealed class Playback : IPlayerAnimationPresentationSink
    {
        private readonly IReadOnlyDictionary<string, PlayerAvatarAnimationClip> _clipsById;
        private double _clockSeconds;

        public Playback(PlayerAvatar avatar, PlayerAnimationPresentation initialPresentation)
        {
            Validate(avatar);
            _clipsById = avatar.AnimationClips.ToDictionary(clip => clip.Id, StringComparer.Ordinal);
            CurrentAnimationPresentation = ValidatePresentation(initialPresentation);
        }

        public PlayerAnimationPresentation CurrentAnimationPresentation { get; private set; }
        public PlayerAnimationState CurrentAnimationState => CurrentAnimationPresentation.SemanticState;
        public PlayerAvatarAnimationClip CurrentClip => _clipsById[CurrentAnimationPresentation.ClipId];
        public double ClipStartedAtSeconds => CurrentAnimationPresentation.ClipStartedAtSeconds;
        public double ClipElapsedSeconds => CurrentAnimationPresentation.ElapsedAt(_clockSeconds);
        public int CurrentFrame => CurrentAnimationPresentation.SampledPose?.FrameIndex ??
            CurrentClip.FrameIndexAt(ClipElapsedSeconds, CurrentAnimationPresentation.Loop);

        public void SetClock(double clockSeconds)
        {
            if (!double.IsFinite(clockSeconds))
                throw new ArgumentOutOfRangeException(nameof(clockSeconds));
            _clockSeconds = Math.Max(0, clockSeconds);
        }

        public void SetAnimationPresentation(PlayerAnimationPresentation presentation)
        {
            CurrentAnimationPresentation = ValidatePresentation(presentation);
        }

        private PlayerAnimationPresentation ValidatePresentation(PlayerAnimationPresentation presentation)
        {
            if (string.IsNullOrWhiteSpace(presentation.ClipId) ||
                !_clipsById.ContainsKey(presentation.ClipId))
            {
                throw new InvalidDataException(
                    $"Player animation presentation references unknown clip '{presentation.ClipId}'.");
            }

            if (!double.IsFinite(presentation.ClipStartedAtSeconds))
                throw new InvalidDataException("Player animation presentation has a non-finite clock origin.");

            if (presentation.SampledPose is { } pose)
            {
                var clip = _clipsById[presentation.ClipId];
                if ((uint)pose.FrameIndex >= (uint)clip.FrameCount ||
                    pose.LocalPositions.Length != clip.LocalFrames[0].Length ||
                    pose.LocalPositions.Any(value => !double.IsFinite(value)))
                {
                    throw new InvalidDataException("Player animation sampled pose has invalid frame/coordinates.");
                }
            }

            return presentation;
        }
    }

    private readonly PlayerAvatar _avatar;
    private readonly IReadOnlyDictionary<string, Vector3[][]> _framesByClipId;
    private readonly List<SurfaceState> _surfaces = [];
    private readonly Vector3[] _sampledPoseVertices;
    private readonly Playback _playback;
    private string? _currentClipId;
    private int _currentFrame = -1;
    private PlayerAvatarSampledPose? _currentSampledPose;
    private readonly bool _profileMeshUpdates = System.Environment.GetEnvironmentVariable("OBP_AVATAR_MESH_PROFILE") == "1";
    private readonly bool _profileRenderTime = System.Environment.GetEnvironmentVariable("OBP_AVATAR_RENDER_PROFILE") == "1";
    private long _profileTicks;
    private long _profileAllocatedBytes;
    private long _profileRebuiltSurfaces;
    private int _profileSamples;
    private SceneTree? _profileSceneTree;
    private int _profileRenderFrames;
    private long _profileViewportCpuSamples, _profileViewportGpuSamples;
    private double _profileViewportCpuMs, _profileViewportGpuMs, _profileFrameSetupMs;

    public PlayerAvatarView(
        PlayerAvatar avatar,
        PlayerAnimationPresentation initialPresentation,
        bool alignGeometricBase = true)
    {
        Validate(avatar);
        _avatar = avatar;
        _sampledPoseVertices = new Vector3[avatar.AnimationClips[0].LocalFrames[0].Length / 3];
        AlignGeometricBase = alignGeometricBase;
        _playback = new Playback(avatar, initialPresentation);
        _framesByClipId = avatar.AnimationClips.ToDictionary(
            clip => clip.Id,
            clip => clip.LocalFrames
                .Select(frame => ConvertFrame(frame, avatar.Axes, avatar.BaseHeight, alignGeometricBase))
                .ToArray(),
            StringComparer.Ordinal);

        Name = $"PlayerAvatar_{avatar.Identity.AvatarId}";
        if (_profileMeshUpdates)
        {
            GD.Print("[avatar-mesh-profile] enabled");
            if (_profileRenderTime) TreeEntered += OnProfileTreeEntered;
            TreeExiting += OnProfileTreeExiting;
        }
        BuildSurfaces();
        ApplyPresentation();
    }

    public PlayerAvatarIdentity Identity => _avatar.Identity;
    public bool AlignGeometricBase { get; }
    public PlayerAnimationPresentation CurrentAnimationPresentation => _playback.CurrentAnimationPresentation;
    public PlayerAnimationState CurrentAnimationState => _playback.CurrentAnimationState;
    public string CurrentClipId => _playback.CurrentClip.Id;
    public float FramesPerSecond => _playback.CurrentClip.ConstantFramesPerSecond ?? 0f;
    public int FrameCount => _playback.CurrentClip.FrameCount;
    public int CurrentFrame => Math.Max(0, _currentFrame);

    /// <summary>Display the frame selected for an absolute local avatar presentation clock.</summary>
    public void SetClock(double clockSeconds)
    {
        _playback.SetClock(clockSeconds);
        ApplyPresentation();
    }

    public void SetAnimationPresentation(PlayerAnimationPresentation presentation)
    {
        _playback.SetAnimationPresentation(presentation);
        ApplyPresentation();
    }

    /// <summary>Display one exact frame of the active clip, useful for deterministic inspection.</summary>
    public void SetFrame(int frame)
    {
        if ((uint)frame >= (uint)_playback.CurrentClip.FrameCount)
            throw new ArgumentOutOfRangeException(nameof(frame));
        ApplyFrame(_playback.CurrentClip.Id, frame);
    }

    /// <summary>Compatibility frame selection for the avatar's neutral standing loop.</summary>
    public static int FrameIndexAt(PlayerAvatar avatar, double clockSeconds) =>
        avatar.RequiredAnimationClip(PlayerAvatarAnimationRole.Standing).FrameIndexAt(clockSeconds, loop: true);

    /// <summary>
    /// Convert a neutral avatar-local point into Godot local space. The neutral
    /// contract names right/forward/up axes explicitly; Godot local forward is -Z.
    /// </summary>
    public static Vector3 ToGodotLocal(
        PlayerAvatarPoint point,
        PlayerAvatarAxes axes,
        double baseHeight = 0,
        bool alignGeometricBase = false)
    {
        double right = Component(point, axes.Right);
        double forward = Component(point, axes.Forward);
        double up = Component(point, axes.Up) - (alignGeometricBase ? baseHeight : 0);
        return new Vector3((float)right, (float)up, (float)-forward);
    }

    public static Vector3[] ConvertFrame(
        double[] xyz,
        PlayerAvatarAxes axes,
        double baseHeight = 0,
        bool alignGeometricBase = false)
    {
        if (xyz.Length == 0 || xyz.Length % 3 != 0)
            throw new InvalidDataException("Player avatar frame is not a non-empty XYZ stream.");

        var result = new Vector3[xyz.Length / 3];
        ConvertFrameInto(xyz, axes, baseHeight, alignGeometricBase, result);
        return result;
    }

    /// <summary>Convert an animated XYZ stream into caller-owned storage without per-pose allocation.</summary>
    public static void ConvertFrameInto(
        double[] xyz, PlayerAvatarAxes axes, double baseHeight, bool alignGeometricBase, Vector3[] result)
    {
        if (xyz.Length == 0 || xyz.Length != result.Length * 3)
            throw new InvalidDataException("Sampled avatar coordinates do not match the vertex stream.");
        for (int i = 0, vertex = 0; i < xyz.Length; i += 3, vertex++)
        {
            result[vertex] = ToGodotLocal(
                new PlayerAvatarPoint(xyz[i], xyz[i + 1], xyz[i + 2]),
                axes,
                baseHeight,
                alignGeometricBase);
        }
    }

    public static void Validate(PlayerAvatar avatar)
    {
        if (avatar.AnimationClips.Count == 0)
            throw new InvalidDataException("Player avatar has no animation clips.");
        if (avatar.AnimationClips.Any(clip => string.IsNullOrWhiteSpace(clip.Id)) ||
            avatar.AnimationClips.Select(clip => clip.Id).Distinct(StringComparer.Ordinal).Count() != avatar.AnimationClips.Count)
        {
            throw new InvalidDataException("Player avatar animation clip ids must be non-empty and unique.");
        }

        var firstClip = avatar.AnimationClips[0];
        if (firstClip.LocalFrames.Count == 0)
            throw new InvalidDataException($"Player avatar clip '{firstClip.Id}' has no frames.");
        int coordinateCount = firstClip.LocalFrames[0].Length;
        if (coordinateCount == 0 || coordinateCount % 3 != 0)
            throw new InvalidDataException("Player avatar frame is not a non-empty XYZ stream.");

        foreach (var clip in avatar.AnimationClips)
        {
            if (clip.AttachmentFrames?.Any(track => string.IsNullOrWhiteSpace(track.Key) ||
                track.Value.Count != clip.FrameCount || track.Value.Any(pose => !pose.IsFinite)) == true)
                throw new InvalidDataException($"Player avatar clip '{clip.Id}' has invalid attachment frames.");
            if (clip.LocalFrames.Count == 0)
                throw new InvalidDataException($"Player avatar clip '{clip.Id}' has no frames.");
            if (clip.FrameDurationsSeconds.Count != clip.LocalFrames.Count ||
                clip.FrameDurationsSeconds.Any(duration => !(duration > 0) || !double.IsFinite(duration)))
            {
                throw new InvalidDataException($"Player avatar clip '{clip.Id}' has invalid per-frame timing.");
            }
            if (clip.LocalFrames.Any(frame =>
                frame.Length != coordinateCount || frame.Any(value => !double.IsFinite(value))))
            {
                throw new InvalidDataException(
                    $"Player avatar clip '{clip.Id}' does not share the finite XYZ vertex stream.");
            }
        }

        int vertexCount = coordinateCount / 3;
        var textures = avatar.Textures.ToDictionary(texture => texture.MaterialId, StringComparer.Ordinal);
        foreach (var surface in avatar.Surfaces)
        {
            if (surface.Uvs.Length != vertexCount * 2)
                throw new InvalidDataException($"Player avatar surface '{surface.MaterialId}' UV count does not match the vertex stream.");
            if (surface.Indices.Length < 3 || surface.Indices.Length % 3 != 0 || surface.Indices.Any(index => index < 0 || index >= vertexCount))
                throw new InvalidDataException($"Player avatar surface '{surface.MaterialId}' has invalid triangle indices.");
            if (!textures.TryGetValue(surface.MaterialId, out var texture) || texture.TextureId != surface.TextureId)
                throw new InvalidDataException($"Player avatar surface '{surface.MaterialId}' has no matching texture payload.");
        }

        if (avatar.Surfaces.Count == 0)
            throw new InvalidDataException("Player avatar has no render surfaces.");
        foreach (var texture in avatar.Textures)
        {
            if (texture.Width <= 0 || texture.Height <= 0 || texture.Rgba.Length != texture.Width * texture.Height * 4)
                throw new InvalidDataException($"Player avatar texture '{texture.MaterialId}' has an invalid RGBA payload.");
        }
    }

    private void BuildSurfaces()
    {
        var textures = _avatar.Textures.ToDictionary(texture => texture.MaterialId, StringComparer.Ordinal);
        foreach (var surface in _avatar.Surfaces)
        {
            var texture = textures[surface.MaterialId];
            var image = Image.CreateFromData(texture.Width, texture.Height, false, Image.Format.Rgba8, texture.Rgba);
            var imageTexture = ImageTexture.CreateFromImage(image);
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                AlbedoColor = Colors.White,
                AlbedoTexture = imageTexture,
            };
            if (AlphaProfile.Analyse(texture.Rgba).AnyBelowHalf)
            {
                material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                material.AlphaScissorThreshold = 0.5f;
            }

            var mesh = new ArrayMesh();
            AddChild(new MeshInstance3D
            {
                Name = $"Surface_{surface.TextureId}",
                Mesh = mesh,
                MaterialOverride = material,
            });
            _surfaces.Add(new SurfaceState(mesh, surface.Indices, ToUvs(surface.Uvs)));
        }
    }

    private void ApplyPresentation() => ApplyFrame(
        _playback.CurrentClip.Id, _playback.CurrentFrame, _playback.CurrentAnimationPresentation.SampledPose);

    private void ApplyFrame(string clipId, int frame, PlayerAvatarSampledPose? pose = null)
    {
        if (string.Equals(clipId, _currentClipId, StringComparison.Ordinal) && frame == _currentFrame &&
            ReferenceEquals(pose, _currentSampledPose))
            return;
        _currentClipId = clipId;
        _currentFrame = frame;
        _currentSampledPose = pose;
        long startTicks = _profileMeshUpdates ? Stopwatch.GetTimestamp() : 0;
        long startAllocation = _profileMeshUpdates ? GC.GetAllocatedBytesForCurrentThread() : 0;
        Vector3[] vertices;
        if (pose is null)
            vertices = _framesByClipId[clipId][frame];
        else
        {
            ConvertFrameInto(pose.LocalPositions, _avatar.Axes, _avatar.BaseHeight, AlignGeometricBase, _sampledPoseVertices);
            vertices = _sampledPoseVertices;
        }
        foreach (var surface in _surfaces)
        {
            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            arrays[(int)Mesh.ArrayType.TexUV] = surface.Uvs;
            arrays[(int)Mesh.ArrayType.Index] = surface.Indices;
            surface.Mesh.ClearSurfaces();
            surface.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
        if (_profileMeshUpdates)
        {
            _profileTicks += Stopwatch.GetTimestamp() - startTicks;
            _profileAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - startAllocation;
            _profileRebuiltSurfaces += _surfaces.Count;
            _profileSamples++;
            if (_profileSamples % 250 == 0) PrintProfile();
        }
    }

    // Signal callbacks work for this engine-neutral view without turning it
    // into a generated Godot script. The scene-tree subscription exists only
    // while the view is attached, so world changes cannot retain the avatar.
    private void OnProfileTreeEntered()
    {
        if (_profileSceneTree is not null) return;
        var viewport = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
        _profileSceneTree = GetTree();
        _profileSceneTree.ProcessFrame += OnProfileProcessFrame;
    }

    private void OnProfileTreeExiting()
    {
        if (_profileSceneTree is not null)
        {
            _profileSceneTree.ProcessFrame -= OnProfileProcessFrame;
            _profileSceneTree = null;
            RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), false);
        }
        if (_profileSamples > 0) PrintProfile();
    }

    /// <summary>Sample the prior rendered frame exactly once per scene-tree process tick.</summary>
    private void OnProfileProcessFrame()
    {
        if (++_profileRenderFrames <= 120) return;
        var viewport = GetViewport().GetViewportRid();
        double cpu = RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport);
        double gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
        if (cpu > 0)
        {
            _profileViewportCpuMs += cpu;
            _profileFrameSetupMs += RenderingServer.GetFrameSetupTimeCpu();
            _profileViewportCpuSamples++;
        }
        if (gpu > 0)
        {
            _profileViewportGpuMs += gpu;
            _profileViewportGpuSamples++;
        }
    }

    private void PrintProfile()
    {
        GD.Print($"[avatar-mesh-profile] source={Identity.SourceGame} updates={_profileSamples} " +
            $"surfaceRebuilds={_profileRebuiltSurfaces} " +
            $"avgUpdateUs={_profileTicks * 1_000_000d / Stopwatch.Frequency / _profileSamples:0.000} " +
            $"avgManagedBytes={_profileAllocatedBytes / (double)_profileSamples:0.0} " +
            $"viewportCpuMs={(_profileViewportCpuSamples > 0 ? _profileViewportCpuMs / _profileViewportCpuSamples : -1):0.000} " +
            $"viewportGpuMs={(_profileViewportGpuSamples > 0 ? _profileViewportGpuMs / _profileViewportGpuSamples : -1):0.000} " +
            $"renderSetupMs={(_profileViewportCpuSamples > 0 ? _profileFrameSetupMs / _profileViewportCpuSamples : -1):0.000} " +
            $"renderFrames={_profileRenderFrames} renderSamples={_profileViewportCpuSamples} gpuSamples={_profileViewportGpuSamples} " +
            $"gc0={GC.CollectionCount(0)} gc1={GC.CollectionCount(1)} gc2={GC.CollectionCount(2)}");
    }

    private static Vector2[] ToUvs(float[] uv)
    {
        var result = new Vector2[uv.Length / 2];
        for (int i = 0, vertex = 0; i < uv.Length; i += 2, vertex++)
            result[vertex] = new Vector2(uv[i], uv[i + 1]);
        return result;
    }

    private static double Component(PlayerAvatarPoint point, PlayerAvatarAxisDirection axis) => axis switch
    {
        PlayerAvatarAxisDirection.PositiveX => point.X,
        PlayerAvatarAxisDirection.PositiveY => point.Y,
        PlayerAvatarAxisDirection.PositiveZ => point.Z,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };
}
