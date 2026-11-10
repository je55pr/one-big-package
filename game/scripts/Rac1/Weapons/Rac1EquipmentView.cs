using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Game;

/// <summary>Decoded retail skin, either attached to a sampled Ratchet hand or placed in the world.</summary>
public sealed partial class Rac1EquipmentView : Node3D
{
    private readonly Rac1EquipmentAssets.Model _model;
    private readonly PlayerAvatar? _avatar;
    private readonly List<(ArrayMesh Mesh, Vector2[] Uvs, int[] Indices)> _surfaces = [];
    private readonly Rac1AnimationFrameClock _clock;
    private readonly PlayerSkeletalSampler? _gloveSampler;
    private int _sequence;
    private int _returnSequence;
    private bool _loop = true;
    private long _generation = -1;
    private bool _soundPending;
    private float _position;
    private double[]? _transitionFrom;
    private int _transitionTicks, _transitionRemaining;
    public Vector3 MuzzleLocal { get; private set; }
    public Vector3? HandOriginLocal { get; private set; }
    public PlayerAvatarAttachmentPose? PayloadPose { get; private set; }
    private readonly Dictionary<int, Vector3> _socketOrigins = [];
    public Vector3 MuzzleAt(int socket) => _socketOrigins.GetValueOrDefault(socket, MuzzleLocal);
    public int Sequence => _sequence;

    /// <summary>
    /// True from <see cref="Build"/> until its one-shot construction clip has finished. This is explicit
    /// rather than read from the sequence, because sequence 0 is also where a model with no ready clip rests.
    /// </summary>
    public bool Constructing { get; private set; }
    public int NativeClass => _model.NativeClass;
    public float FootOffset { get; private set; }

    public Rac1EquipmentView(Rac1EquipmentAssets.Model model, WorldMaterialFactory materials, PlayerAvatar? avatar = null)
    {
        _model = model; _avatar = avatar;
        if (model.GloveSkin is { } skin) _gloveSampler = new(skin.Skeleton, skin.Skin);
        _sequence = model.Clips.ContainsKey(1) ? 1 : 0;
        _returnSequence = _sequence;
        _clock = new(model.Clips.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<double>)pair.Value.Durations), _sequence, 0, 1);
        Name = $"Rac1RetailEquipment_Class{model.NativeClass}";
        foreach (var surface in model.Surfaces)
        {
            int[] indices = (int[])surface.Indices.Clone();
            if (avatar is null)
                for (int i = 0; i < indices.Length; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
            var mesh = new ArrayMesh();
            _surfaces.Add((mesh, Enumerable.Range(0, surface.Uvs.Length / 2)
                .Select(i => new Vector2(surface.Uvs[i * 2], surface.Uvs[i * 2 + 1])).ToArray(), indices));
            AddChild(new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = materials.Instanced("moby", surface.TextureId, false, surface.MaterialPresentation)
            });
        }
        Sample(null);
    }

    public void Play(int sequence, bool loop = true, long generation = 0, int returnSequence = 1)
    {
        if (!_model.Clips.ContainsKey(sequence) || _sequence == sequence && _generation == generation) return;
        Constructing = false;
        _sequence = sequence; _loop = loop; _generation = generation;
        _returnSequence = _model.Clips.ContainsKey(returnSequence) ? returnSequence : 0;
        _clock.ResumePlayback(sequence, 0, 0, 1);
        _position = _model.Clips[sequence].Positions[0]; _soundPending = true;
    }

    /// <summary>Play the weapon's construction (sequence 0) once, then settle to its ready pose.</summary>
    public void Build(long generation)
    {
        Play(0, false, generation, 1);
        Constructing = _sequence == 0 && !_loop;
    }

    public void BlendTo(int sequence, int ticks, long generation)
    {
        var sample = _clock.Current; var clip = _model.Clips[_sequence];
        var a = clip.Frames[sample.CurrentFrame]; var b = clip.Frames[sample.NextFrame];
        _transitionFrom = a.Select((value, at) => value + (b[at] - value) * sample.Fraction).ToArray();
        _transitionTicks = _transitionRemaining = ticks;
        Play(sequence, true, generation);
    }

    public IReadOnlyList<int> Step(PlayerAvatarAttachmentPose? hand = null, Rac1RatchetHandPose? handPose = null)
    {
        var sounds = new List<int>();
        var clip = _model.Clips[_sequence];
        if (_soundPending && clip.DirectSound is { } direct) sounds.Add(direct);
        _soundPending = false;
        if (_transitionRemaining > 0) _transitionRemaining--;
        float before = _position;
        var previousFrame = _clock.Current;
        _clock.Advance();
        var sample = _clock.Current;
        // The shared player clock flags the transition *towards* frame0 when
        // entering the final key. Sound cue wrap must wait until frame0 is
        // actually reached; otherwise every early cue plays again at the end.
        bool wrapped = sample.CurrentFrame < previousFrame.CurrentFrame ||
            clip.Frames.Length == 1 && _clock.WrappedThisUpdate;
        _position = PositionAt(clip, sample);
        foreach (var cue in clip.Sounds)
            if (CueCrossed(cue.PositionFrames, before, wrapped)) sounds.Add(cue.SoundId);
        if (!_loop && wrapped) ReturnToLoop();
        Sample(hand, handPose);
        return sounds;
    }

    private static float PositionAt(Rac1EquipmentAssets.Clip clip, Rac1AnimationFrameClock.Sample sample)
    {
        float start = clip.Positions[sample.CurrentFrame];
        float end = sample.NextFrame == 0 ? start + (float)clip.Durations[sample.CurrentFrame] * 30 : clip.Positions[sample.NextFrame];
        return start + (end - start) * sample.Fraction;
    }

    private bool CueCrossed(double cuePosition, float before, bool wrapped) =>
        wrapped ? cuePosition > before || _loop && cuePosition <= _position : cuePosition > before && cuePosition <= _position;

    private void ReturnToLoop()
    {
        Constructing = false;
        _sequence = _returnSequence; _loop = true;
        _clock.ResumePlayback(_sequence, 0, 0, 1);
        _position = _model.Clips[_sequence].Positions[0]; _soundPending = true;
    }

    public void RefreshAttachment(PlayerAvatarAttachmentPose? hand, Rac1RatchetHandPose? handPose = null) => Sample(hand, handPose);

    private Vector3 ToLocal(PlayerAvatarPoint point) => _avatar is null ? new(-(float)point.X, (float)point.Z, (float)point.Y) :
        PlayerAvatarView.ToGodotLocal(point, _avatar.Axes, _avatar.BaseHeight, true);

    private void Sample(PlayerAvatarAttachmentPose? hand, Rac1RatchetHandPose? handPose = null)
    {
        var sample = _clock.Current;
        var clip = _model.Clips[_sequence];
        Rac1GloveSkin.Sample? copied = null;
        if (hand is not null && _model.GloveSkin is { } skin)
            copied = skin.Pose(_gloveSampler!, handPose ??
                throw new InvalidOperationException("The held glove needs Ratchet's current hand pose."));
        HandOriginLocal = hand is null ? null : ToLocal(hand.Origin);
        // A clip that plays once holds its last key; the clock reports the wrap towards frame 0 there,
        // which would collapse a construction animation into its folded first pose for two updates.
        int next = !_loop && sample.NextFrame < sample.CurrentFrame ? sample.CurrentFrame : sample.NextFrame;
        var vertices = BuildVertices(copied?.Positions ?? clip.Frames[sample.CurrentFrame],
            copied?.Positions ?? clip.Frames[next], sample.Fraction, hand);
        var origin = copied is null
            ? System.Numerics.Vector3.Lerp(clip.Origins[sample.CurrentFrame], clip.Origins[next], sample.Fraction)
            : new((float)copied.PayloadPose.Origin.X, (float)copied.PayloadPose.Origin.Y, (float)copied.PayloadPose.Origin.Z);
        var muzzle = new PlayerAvatarPoint(origin.X, origin.Y, origin.Z);
        if (hand is not null) muzzle = hand.Transform(muzzle);
        MuzzleLocal = ToLocal(muzzle);
        PayloadPose = null;
        if (copied is not null) PayloadPose = hand is null ? copied.PayloadPose : PlaceOnHand(copied.PayloadPose, hand);
        UpdateSocketOrigins(clip, sample.CurrentFrame, next, sample.Fraction, hand);
        UploadSurfaces(vertices);
    }

    private Vector3[] BuildVertices(double[] a, double[] b, float fraction, PlayerAvatarAttachmentPose? hand)
    {
        var vertices = new Vector3[a.Length / 3];
        float minimumHeight = float.PositiveInfinity;
        var previous = _transitionRemaining > 0 ? _transitionFrom : null;
        float amount = previous is null ? 0 : 1 - _transitionRemaining / (float)_transitionTicks;
        for (int i = 0; i < vertices.Length; i++)
        {
            int at = i * 3;
            var point = new PlayerAvatarPoint(a[at] + (b[at] - a[at]) * fraction,
                a[at + 1] + (b[at + 1] - a[at + 1]) * fraction,
                a[at + 2] + (b[at + 2] - a[at + 2]) * fraction);
            if (previous is not null)
            {
                point = new(previous[at] + (point.X - previous[at]) * amount,
                    previous[at + 1] + (point.Y - previous[at + 1]) * amount,
                    previous[at + 2] + (point.Z - previous[at + 2]) * amount);
            }
            if (hand is not null) point = hand.Transform(point);
            vertices[i] = ToLocal(point);
            minimumHeight = MathF.Min(minimumHeight, vertices[i].Y);
        }
        FootOffset = MathF.Max(0, -minimumHeight);
        return vertices;
    }

    private static PlayerAvatarAttachmentPose PlaceOnHand(PlayerAvatarAttachmentPose pose, PlayerAvatarAttachmentPose hand)
    {
        PlayerAvatarPoint Direction(PlayerAvatarPoint p)
        {
            var end = hand.Transform(p);
            return new(end.X - hand.Origin.X, end.Y - hand.Origin.Y, end.Z - hand.Origin.Z);
        }
        return new(hand.Transform(pose.Origin), Direction(pose.X), Direction(pose.Y), Direction(pose.Z));
    }

    private void UpdateSocketOrigins(Rac1EquipmentAssets.Clip clip, int current, int next, float fraction,
        PlayerAvatarAttachmentPose? hand)
    {
        _socketOrigins.Clear();
        if (clip.SocketOrigins is not { } sockets) return;
        foreach (var pair in sockets)
        {
            var p = System.Numerics.Vector3.Lerp(pair.Value[current], pair.Value[next], fraction);
            var point = new PlayerAvatarPoint(p.X, p.Y, p.Z);
            if (hand is not null) point = hand.Transform(point);
            _socketOrigins[pair.Key] = ToLocal(point);
        }
    }

    private void UploadSurfaces(Vector3[] vertices)
    {
        foreach (var surface in _surfaces)
        {
            var arrays = new global::Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.TexUV] = surface.Uvs;
            arrays[(int)Mesh.ArrayType.Index] = surface.Indices;
            surface.Mesh.ClearSurfaces(); surface.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
    }
}
