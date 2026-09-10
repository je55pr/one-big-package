using Godot;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;

namespace OBP.Godot.Player;

/// <summary>
/// Presentation-only Godot view of an engine-independent <see cref="PlayerAvatar"/>.
/// Avatar frames remain model-local; this node performs only local axis conversion,
/// optional geometric-base alignment, material construction, and deterministic frame display.
/// </summary>
public sealed class PlayerAvatarView : Node3D, IPlayerAnimationStateSink
{
    private sealed record SurfaceState(ArrayMesh Mesh, int[] Indices, Vector2[] Uvs);

    /// <summary>
    /// Pure semantic-to-clip state machine used by the view. It deliberately
    /// chooses only neutral clip roles and never sees source-game sequence ids.
    /// </summary>
    public sealed class Playback : IPlayerAnimationStateSink
    {
        private readonly PlayerAvatar _avatar;
        private PlayerAvatarAnimationClip _currentClip;
        private double _clockSeconds;
        private double _clipStartedAtSeconds;
        private PlayerAnimationState _airborneReturnState = PlayerAnimationState.Idle;
        private PlayerAnimationState _attackReturnState = PlayerAnimationState.Idle;

        public Playback(PlayerAvatar avatar)
        {
            Validate(avatar);
            _avatar = avatar;
            _currentClip = avatar.RequiredAnimationClip(PlayerAvatarAnimationRole.Standing);
        }

        public PlayerAnimationState CurrentAnimationState { get; private set; } = PlayerAnimationState.Idle;
        public PlayerAvatarAnimationClip CurrentClip => _currentClip;
        public double ClipStartedAtSeconds => _clipStartedAtSeconds;
        public double ClipElapsedSeconds => Math.Max(0, _clockSeconds - _clipStartedAtSeconds);
        public int CurrentFrame => _currentClip.FrameIndexAt(ClipElapsedSeconds, ShouldLoop(_currentClip));

        public void SetClock(double clockSeconds)
        {
            if (!double.IsFinite(clockSeconds))
                throw new ArgumentOutOfRangeException(nameof(clockSeconds));
            _clockSeconds = Math.Max(0, clockSeconds);
            CompleteLocomotionStartIfNeeded();
            CompleteAttackIfNeeded();
        }

        public void SetAnimationState(PlayerAnimationState state)
        {
            if (state == CurrentAnimationState)
                return;

            // AttackRequested is a one-frame semantic pulse from the controller.
            // Keep the native one-shot playing while subsequent movement facts
            // update only the state we should return to when it completes.
            if (CurrentAnimationState == PlayerAnimationState.Attack)
            {
                _attackReturnState = GroundContext(state);
                return;
            }

            // LocomotionStart is a native-timed one-shot. Walk/Run are semantic
            // controller facts, so pulses between them update return context
            // without replacing or restarting the start clip.
            if (_currentClip.Role == PlayerAvatarAnimationRole.LocomotionStart && IsLocomotion(state))
            {
                CurrentAnimationState = state;
                _airborneReturnState = state;
                return;
            }

            PlayerAnimationState previous = CurrentAnimationState;
            switch (state)
            {
                case PlayerAnimationState.Idle:
                    CurrentAnimationState = state;
                    _airborneReturnState = PlayerAnimationState.Idle;
                    Select(PlayerAvatarAnimationRole.Standing, restart: _currentClip.Role != PlayerAvatarAnimationRole.Standing);
                    break;

                case PlayerAnimationState.Walk:
                case PlayerAnimationState.Run:
                    CurrentAnimationState = state;
                    _airborneReturnState = state;
                    bool startFromGroundedIdle = previous == PlayerAnimationState.Idle &&
                                                 _currentClip.Role == PlayerAvatarAnimationRole.Standing;
                    Select(
                        startFromGroundedIdle
                            ? PlayerAvatarAnimationRole.LocomotionStart
                            : PlayerAvatarAnimationRole.SustainedLocomotion,
                        restart: startFromGroundedIdle ||
                                 _currentClip.Role != PlayerAvatarAnimationRole.SustainedLocomotion);
                    break;

                case PlayerAnimationState.JumpRise:
                    bool moving = IsLocomotion(previous) ||
                                  _currentClip.Role == PlayerAvatarAnimationRole.SustainedLocomotion;
                    _airborneReturnState = moving
                        ? LocomotionContext(previous)
                        : PlayerAnimationState.Idle;
                    CurrentAnimationState = state;
                    Select(
                        moving ? PlayerAvatarAnimationRole.MovingJump : PlayerAvatarAnimationRole.StationaryJump,
                        restart: true);
                    break;

                case PlayerAnimationState.Fall:
                    CurrentAnimationState = state;
                    if (_currentClip.Role is not PlayerAvatarAnimationRole.StationaryJump and
                        not PlayerAvatarAnimationRole.MovingJump)
                    {
                        bool movingFall = IsLocomotion(previous) ||
                                          _currentClip.Role == PlayerAvatarAnimationRole.SustainedLocomotion;
                        _airborneReturnState = movingFall
                            ? LocomotionContext(previous)
                            : PlayerAnimationState.Idle;
                        Select(
                            movingFall ? PlayerAvatarAnimationRole.MovingJump : PlayerAvatarAnimationRole.StationaryJump,
                            restart: true);
                    }
                    break;

                case PlayerAnimationState.Land:
                    CurrentAnimationState = state;
                    Select(
                        IsLocomotion(_airborneReturnState)
                            ? PlayerAvatarAnimationRole.SustainedLocomotion
                            : PlayerAvatarAnimationRole.Standing,
                        restart: true);
                    break;

                case PlayerAnimationState.Attack:
                    _attackReturnState = GroundContext(previous);
                    CurrentAnimationState = state;
                    Select(PlayerAvatarAnimationRole.PrimaryAttack, restart: true);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        private void CompleteLocomotionStartIfNeeded()
        {
            if (!IsLocomotion(CurrentAnimationState) ||
                _currentClip.Role != PlayerAvatarAnimationRole.LocomotionStart ||
                ClipElapsedSeconds < _currentClip.DurationSeconds)
            {
                return;
            }

            double completedAt = _clipStartedAtSeconds + _currentClip.DurationSeconds;
            Select(
                PlayerAvatarAnimationRole.SustainedLocomotion,
                restart: true,
                startAtSeconds: completedAt);
        }

        private void CompleteAttackIfNeeded()
        {
            if (CurrentAnimationState != PlayerAnimationState.Attack ||
                ClipElapsedSeconds < _currentClip.DurationSeconds)
            {
                return;
            }

            double completedAt = _clipStartedAtSeconds + _currentClip.DurationSeconds;
            CurrentAnimationState = _attackReturnState;
            Select(
                IsLocomotion(_attackReturnState)
                    ? PlayerAvatarAnimationRole.SustainedLocomotion
                    : PlayerAvatarAnimationRole.Standing,
                restart: true,
                startAtSeconds: completedAt);
        }

        private PlayerAnimationState GroundContext(PlayerAnimationState previous)
        {
            if (IsLocomotion(previous))
                return previous;
            if (_currentClip.Role == PlayerAvatarAnimationRole.SustainedLocomotion &&
                IsLocomotion(_airborneReturnState))
            {
                return _airborneReturnState;
            }
            return PlayerAnimationState.Idle;
        }

        private PlayerAnimationState LocomotionContext(PlayerAnimationState previous) =>
            IsLocomotion(previous)
                ? previous
                : IsLocomotion(_airborneReturnState)
                    ? _airborneReturnState
                    : PlayerAnimationState.Run;

        private void Select(
            PlayerAvatarAnimationRole role,
            bool restart,
            double? startAtSeconds = null)
        {
            var target = _avatar.RequiredAnimationClip(role);
            if (restart || !string.Equals(_currentClip.Id, target.Id, StringComparison.Ordinal))
                _clipStartedAtSeconds = startAtSeconds ?? _clockSeconds;
            _currentClip = target;
        }

        private static bool IsLocomotion(PlayerAnimationState state) =>
            state is PlayerAnimationState.Walk or PlayerAnimationState.Run;

        private static bool ShouldLoop(PlayerAvatarAnimationClip clip) =>
            clip.Role is PlayerAvatarAnimationRole.Standing or PlayerAvatarAnimationRole.SustainedLocomotion;
    }

    private readonly PlayerAvatar _avatar;
    private readonly IReadOnlyDictionary<string, Vector3[][]> _framesByClipId;
    private readonly List<SurfaceState> _surfaces = [];
    private readonly Playback _playback;
    private string? _currentClipId;
    private int _currentFrame = -1;

    public PlayerAvatarView(PlayerAvatar avatar, bool alignGeometricBase = true)
    {
        Validate(avatar);
        _avatar = avatar;
        AlignGeometricBase = alignGeometricBase;
        _playback = new Playback(avatar);
        _framesByClipId = avatar.AnimationClips.ToDictionary(
            clip => clip.Id,
            clip => clip.LocalFrames
                .Select(frame => ConvertFrame(frame, avatar.Axes, avatar.BaseHeight, alignGeometricBase))
                .ToArray(),
            StringComparer.Ordinal);

        Name = $"PlayerAvatar_{avatar.Identity.AvatarId}";
        BuildSurfaces();
        ApplyFrame(_playback.CurrentClip.Id, 0);
    }

    public PlayerAvatarIdentity Identity => _avatar.Identity;
    public bool AlignGeometricBase { get; }
    public PlayerAnimationState CurrentAnimationState => _playback.CurrentAnimationState;
    public string CurrentClipId => _playback.CurrentClip.Id;
    public float FramesPerSecond => _playback.CurrentClip.ConstantFramesPerSecond ?? 0f;
    public int FrameCount => _playback.CurrentClip.FrameCount;
    public int CurrentFrame => Math.Max(0, _currentFrame);

    /// <summary>Display the frame selected for an absolute local avatar presentation clock.</summary>
    public void SetClock(double clockSeconds)
    {
        _playback.SetClock(clockSeconds);
        ApplyFrame(_playback.CurrentClip.Id, _playback.CurrentFrame);
    }

    public void SetAnimationState(PlayerAnimationState state)
    {
        _playback.SetAnimationState(state);
        ApplyFrame(_playback.CurrentClip.Id, _playback.CurrentFrame);
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
        for (int i = 0, vertex = 0; i < xyz.Length; i += 3, vertex++)
        {
            result[vertex] = ToGodotLocal(
                new PlayerAvatarPoint(xyz[i], xyz[i + 1], xyz[i + 2]),
                axes,
                baseHeight,
                alignGeometricBase);
        }
        return result;
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

        PlayerAvatarAnimationRole[] requiredRoles =
        [
            PlayerAvatarAnimationRole.Standing,
            PlayerAvatarAnimationRole.LocomotionStart,
            PlayerAvatarAnimationRole.SustainedLocomotion,
            PlayerAvatarAnimationRole.StationaryJump,
            PlayerAvatarAnimationRole.MovingJump,
            PlayerAvatarAnimationRole.PrimaryAttack,
        ];
        foreach (var role in requiredRoles)
        {
            if (avatar.AnimationClips.Count(clip => clip.Role == role) != 1)
                throw new InvalidDataException($"Player avatar requires exactly one '{role}' animation clip.");
        }

        var firstClip = avatar.AnimationClips[0];
        if (firstClip.LocalFrames.Count == 0)
            throw new InvalidDataException($"Player avatar clip '{firstClip.Id}' has no frames.");
        int coordinateCount = firstClip.LocalFrames[0].Length;
        if (coordinateCount == 0 || coordinateCount % 3 != 0)
            throw new InvalidDataException("Player avatar frame is not a non-empty XYZ stream.");

        foreach (var clip in avatar.AnimationClips)
        {
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

    private void ApplyFrame(string clipId, int frame)
    {
        if (string.Equals(clipId, _currentClipId, StringComparison.Ordinal) && frame == _currentFrame)
            return;
        _currentClipId = clipId;
        _currentFrame = frame;
        Vector3[] vertices = _framesByClipId[clipId][frame];
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
