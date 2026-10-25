using Godot;
using OBP.Godot.Player;
using OBP.RAC1.Player;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;

namespace OBP.Game;

/// <summary>
/// Godot presentation of the recovered retail class-71 Wrench mesh.
/// Follows the recovered joint56 attachment and the wrench's own animation.
/// Vertex/affine interpolation shares the player's baked-pose approximation.
/// </summary>
public sealed partial class Rac1WrenchView : Node3D
{
    public const string AttachmentProvenance = "retail-semantic-joint0-ratchet-joint56";
    private readonly PlayerAvatar _avatar;
    private readonly Dictionary<int, Rac1WrenchAsset.AnimationClip> _clips;
    private readonly List<(ArrayMesh Mesh, Vector2[] Uvs, int[] Indices)> _surfaces = [];
    private Rac1AnimationFrameClock _clock;
    private double[]? _blendSource;
    private PlayerAvatarPoint _tipBlendSource;
    private long _generation = -1;
    private int _sequence = 1;
    private double[] _localPositions;
    public Vector3 HandLocal { get; private set; }
    public Vector3 TipLocal { get; private set; }
    public int NativeSequence => _sequence;

    public Rac1WrenchView(Rac1WrenchAsset.Asset asset, PlayerAvatar avatar)
    {
        _avatar = avatar;
        _clips = new[] { 1, 3, 4, 5, 6, 10 }.ToDictionary(sequence => sequence,
            sequence => Rac1WrenchAsset.DecodeAnimation(asset, sequence));
        _clock = new(_clips.ToDictionary(pair => pair.Key, pair => pair.Value.Durations), 1, 0, 1);
        _localPositions = _clips[1].Frames[0];
        _tipBlendSource = _clips[1].TipFrames[0];
        Name = "Rac1RetailWrench_AnimatedHand";
        BuildSurfaces(asset, avatar.Axes);
    }

    public void StepAttached(PlayerAvatarAttachmentPose hand, Rac1WrenchAttackSession.Frame attack, bool wasThrown)
    {
        Transform = Transform3D.Identity;
        bool recovery = attack.ProfileId == 3 && attack.WrenchReleased;
        int sequence = attack.Active && !wasThrown && !recovery ? Rac1WrenchAttackProfile.Profiles[attack.ProfileId].WrenchSequence : 1;
        long generation = attack.Active && !wasThrown ? attack.Generation : 0;
        if (_sequence != sequence || _generation != generation)
        {
            _blendSource = _localPositions;
            _tipBlendSource = CurrentLocalTip();
            var profile = attack.Active && !wasThrown && !recovery ? Rac1WrenchAttackProfile.Profiles[attack.ProfileId] : null;
            _clock.Select(sequence, profile?.EntryFrame ?? 0, profile?.BlendTicks ?? 5);
            _sequence = sequence;
            _generation = generation;
        }
        else
            _clock.Advance();
        _clock.PlaybackMultiplier = attack.Animation?.PlaybackMultiplier ?? 1;
        _localPositions = SampleLocal();
        var localTip = CurrentLocalTip();
        var positions = new double[_localPositions.Length];
        for (int i = 0; i < positions.Length; i += 3)
        {
            var point = hand.Transform(new(_localPositions[i], _localPositions[i + 1], _localPositions[i + 2]));
            positions[i] = point.X; positions[i + 1] = point.Y; positions[i + 2] = point.Z;
        }
        HandLocal = Convert(hand.Origin, true);
        TipLocal = Convert(hand.Transform(localTip), true);
        UpdateSurfaces(PlayerAvatarView.ConvertFrame(positions, _avatar.Axes, _avatar.BaseHeight, true));
    }

    public void StepThrown(Vector3 worldPosition, float spin)
    {
        GlobalTransform = new Transform3D(new Basis(Vector3.Up, spin), worldPosition);
        _localPositions = _clips[6].Frames[0];
        UpdateSurfaces(PlayerAvatarView.ConvertFrame(_localPositions, _avatar.Axes));
        HandLocal = Vector3.Zero;
        TipLocal = Convert(_clips[6].TipFrames[0], false);
    }

    private Vector3 Convert(PlayerAvatarPoint point, bool alignBase) =>
        PlayerAvatarView.ToGodotLocal(point, _avatar.Axes, _avatar.BaseHeight, alignBase);

    private double[] SampleLocal()
    {
        var sample = _clock.Current;
        var clip = _clips[sample.Sequence];
        double[] from = sample.IsBlending ? _blendSource ?? clip.Frames[sample.CurrentFrame] : clip.Frames[sample.CurrentFrame];
        double[] to = clip.Frames[sample.NextFrame];
        var result = new double[to.Length];
        for (int i = 0; i < result.Length; i++) result[i] = from[i] + (to[i] - from[i]) * sample.Fraction;
        return result;
    }

    private PlayerAvatarPoint CurrentLocalTip()
    {
        var sample = _clock.Current;
        var clip = _clips[sample.Sequence];
        var from = sample.IsBlending ? _tipBlendSource : clip.TipFrames[sample.CurrentFrame];
        var to = clip.TipFrames[sample.NextFrame];
        return new(from.X + (to.X - from.X) * sample.Fraction,
            from.Y + (to.Y - from.Y) * sample.Fraction, from.Z + (to.Z - from.Z) * sample.Fraction);
    }

    private void UpdateSurfaces(Vector3[] vertices)
    {
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

    private void BuildSurfaces(Rac1WrenchAsset.Asset asset, PlayerAvatarAxes axes)
    {
        Vector3[] vertices = PlayerAvatarView.ConvertFrame(_localPositions, axes);
        var textures = asset.Textures.ToDictionary(texture => texture.TextureId);
        foreach (var surface in asset.Surfaces)
        {
            if (!textures.TryGetValue(surface.TextureId, out var texture))
                throw new InvalidDataException(
                    $"R&C1 Wrench surface texture {surface.TextureId} is missing.");

            var image = Image.CreateFromData(
                texture.Width, texture.Height, false, Image.Format.Rgba8, texture.Rgba);
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

            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            arrays[(int)Mesh.ArrayType.TexUV] = ToUvs(surface.Uvs);
            arrays[(int)Mesh.ArrayType.Index] = surface.Indices;

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            _surfaces.Add((mesh, ToUvs(surface.Uvs), surface.Indices));
            AddChild(new MeshInstance3D
            {
                Name = $"RetailClass71_Surface_{surface.TextureId}",
                Mesh = mesh,
                MaterialOverride = material,
            });
        }
    }

    private static Vector2[] ToUvs(float[] uv)
    {
        var result = new Vector2[uv.Length / 2];
        for (int i = 0, vertex = 0; i < uv.Length; i += 2, vertex++)
            result[vertex] = new Vector2(uv[i], uv[i + 1]);
        return result;
    }
}
