using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OneBigPackage;

/// <summary>Decoded retail skin, either attached to a sampled Ratchet hand or placed in the world.</summary>
public sealed partial class Rac1EquipmentView : Node3D
{
    private readonly Rac1EquipmentAssets.Model _model;
    private readonly PlayerAvatar? _avatar;
    private readonly List<(ArrayMesh Mesh, Vector2[] Uvs, int[] Indices)> _surfaces = [];
    private readonly Rac1AnimationFrameClock _clock;
    private int _sequence;
    private int _returnSequence;
    private bool _loop = true;
    private long _generation = -1;
    private bool _soundPending;
    private float _position;
    private double[]? _transitionFrom;
    private int _transitionTicks, _transitionRemaining;
    public Vector3 MuzzleLocal { get; private set; }
    private readonly Dictionary<int, Vector3> _socketOrigins = [];
    public Vector3 MuzzleAt(int socket) => _socketOrigins.GetValueOrDefault(socket, MuzzleLocal);
    public int Sequence => _sequence;
    public int NativeClass => _model.NativeClass;
    public float FootOffset { get; private set; }

    public Rac1EquipmentView(Rac1EquipmentAssets.Model model, WorldMaterialFactory materials, PlayerAvatar? avatar = null)
    {
        _model = model; _avatar = avatar;
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
        _sequence = sequence; _loop = loop; _generation = generation;
        _returnSequence = _model.Clips.ContainsKey(returnSequence) ? returnSequence : 0;
        _clock.ResumePlayback(sequence, 0, 0, 1);
        _position = _model.Clips[sequence].Positions[0]; _soundPending = true;
    }

    public void BlendTo(int sequence, int ticks, long generation)
    {
        var sample = _clock.Current; var clip = _model.Clips[_sequence];
        var a = clip.Frames[sample.CurrentFrame]; var b = clip.Frames[sample.NextFrame];
        _transitionFrom = a.Select((value, at) => value + (b[at] - value) * sample.Fraction).ToArray();
        _transitionTicks = _transitionRemaining = ticks;
        Play(sequence, true, generation);
    }

    public IReadOnlyList<int> Step(PlayerAvatarAttachmentPose? hand = null)
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
        float end = sample.NextFrame == 0 ? clip.Positions[sample.CurrentFrame] + (float)clip.Durations[sample.CurrentFrame] * 30 :
            clip.Positions[sample.NextFrame];
        _position = clip.Positions[sample.CurrentFrame] + (end - clip.Positions[sample.CurrentFrame]) * sample.Fraction;
        foreach (var cue in clip.Sounds)
            if (!wrapped && cue.PositionFrames > before && cue.PositionFrames <= _position ||
                wrapped && (cue.PositionFrames > before || _loop && cue.PositionFrames <= _position)) sounds.Add(cue.SoundId);
        if (!_loop && wrapped)
        {
            _sequence = _returnSequence; _loop = true;
            _clock.ResumePlayback(_sequence, 0, 0, 1);
            _position = _model.Clips[_sequence].Positions[0]; _soundPending = true;
        }
        Sample(hand);
        return sounds;
    }

    public void RefreshAttachment(PlayerAvatarAttachmentPose? hand) => Sample(hand);

    private void Sample(PlayerAvatarAttachmentPose? hand)
    {
        var sample = _clock.Current;
        var clip = _model.Clips[_sequence];
        var a = clip.Frames[sample.CurrentFrame]; var b = clip.Frames[sample.NextFrame];
        var vertices = new Vector3[a.Length / 3];
        float minimumHeight = float.PositiveInfinity;
        for (int i = 0; i < vertices.Length; i++)
        {
            int at = i * 3;
            var point = new PlayerAvatarPoint(a[at] + (b[at] - a[at]) * sample.Fraction,
                a[at + 1] + (b[at + 1] - a[at + 1]) * sample.Fraction,
                a[at + 2] + (b[at + 2] - a[at + 2]) * sample.Fraction);
            if (_transitionFrom is { } previous && _transitionRemaining > 0)
            {
                float amount = 1 - _transitionRemaining / (float)_transitionTicks;
                point = new(previous[at] + (point.X - previous[at]) * amount,
                    previous[at + 1] + (point.Y - previous[at + 1]) * amount,
                    previous[at + 2] + (point.Z - previous[at + 2]) * amount);
            }
            if (hand is not null) point = hand.Transform(point);
            vertices[i] = _avatar is null ? new(-(float)point.X, (float)point.Z, (float)point.Y) :
                PlayerAvatarView.ToGodotLocal(point, _avatar.Axes, _avatar.BaseHeight, true);
            minimumHeight = MathF.Min(minimumHeight, vertices[i].Y);
        }
        FootOffset = MathF.Max(0, -minimumHeight);
        var origin = System.Numerics.Vector3.Lerp(clip.Origins[sample.CurrentFrame], clip.Origins[sample.NextFrame], sample.Fraction);
        var muzzle = new PlayerAvatarPoint(origin.X, origin.Y, origin.Z);
        if (hand is not null) muzzle = hand.Transform(muzzle);
        MuzzleLocal = _avatar is null ? new(-(float)muzzle.X, (float)muzzle.Z, (float)muzzle.Y) :
            PlayerAvatarView.ToGodotLocal(muzzle, _avatar.Axes, _avatar.BaseHeight, true);
        _socketOrigins.Clear();
        if (clip.SocketOrigins is { } sockets)
            foreach (var pair in sockets)
            {
                var p = System.Numerics.Vector3.Lerp(pair.Value[sample.CurrentFrame], pair.Value[sample.NextFrame], sample.Fraction);
                var point = new PlayerAvatarPoint(p.X, p.Y, p.Z);
                if (hand is not null) point = hand.Transform(point);
                _socketOrigins[pair.Key] = _avatar is null ? new(-(float)point.X, (float)point.Z, (float)point.Y) :
                    PlayerAvatarView.ToGodotLocal(point, _avatar.Axes, _avatar.BaseHeight, true);
            }
        foreach (var surface in _surfaces)
        {
            var arrays = new global::Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.TexUV] = surface.Uvs;
            arrays[(int)Mesh.ArrayType.Index] = surface.Indices;
            surface.Mesh.ClearSurfaces(); surface.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
    }
}
