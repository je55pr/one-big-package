using OBP.RAC1.Gameplay;
using OBP.Runtime.Player;
using System.Numerics;

namespace OBP.RAC1.Player;

/// <summary>
/// Slot-3 animation pairing (231e78/231ff8), idle close/fidgets
/// (22a090/22a364), and class607's glide override (2d37d0).
/// Call once per admitted 60-Hz player update, after Ratchet's selector.
/// </summary>
public sealed class Rac1ClankPackSession
{
    public readonly record struct Input(int Pack, int NativeAction,
        Rac1AnimationFrameClock.Sample Body, double BodySelection, float BodyMultiplier = 1,
        int EntryFrame = 0, int BlendTicks = 7, float HeadYaw = 0, float HeadPitch = 0, float YawVelocity = 0);
    private readonly Rac1ClankPackAssets _assets;
    private readonly Rac1PickupRandom _random;
    private Input? _previous;
    private int _idleDelay;
    private int _actionAge;
    private bool _closing;
    private int _blinkDelay, _blinkStep;
    private float _headYaw, _headYawVelocity, _headOffset, _headOffsetVelocity, _headGain, _headDamping;
    public Rac1ClankPackAnimation Clank { get; }
    public Rac1ClankPackAnimation? Upgrade { get; private set; }
    public int Pack { get; private set; }
    public bool Ready => Upgrade is not null && !_closing;

    public Rac1ClankPackSession(Rac1ClankPackAssets assets, Rac1PickupRandom random)
    {
        _assets = assets; _random = random;
        Clank = new(assets.Models[Rac1ClankPackAssets.ClankClass]);
        _idleDelay = 0; // The hero initializer clears P+22c4; the first idle event arms its interval.
    }

    public static int ResolvePack(int selected, bool hydroAvailable, bool swimming) =>
        swimming && hydroAvailable ? 4 : selected;

    /// <summary>20f7b0 removes the plain attached Moby's scale, retaining its joint translation.</summary>
    public static PlayerAvatarAttachmentPose AttachmentPose(PlayerAvatarAttachmentPose pose)
    {
        static PlayerAvatarPoint Unit(PlayerAvatarPoint axis)
        {
            double length = Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y + axis.Z * axis.Z);
            if (length < 1e-8) throw new InvalidDataException("Clank attachment has a singular axis.");
            return new(axis.X / length, axis.Y / length, axis.Z / length);
        }
        return pose with { X = Unit(pose.X), Y = Unit(pose.Y), Z = Unit(pose.Z) };
    }

    public void Step(Input input)
    {
        StepSlot(input);
        StepClankHead(input);
    }

    private void StepSlot(Input input)
    {
        if (input.Pack is not (0 or 2 or 3 or 4) || !float.IsFinite(input.BodyMultiplier) || input.BodyMultiplier < 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        if (Upgrade is not null && input.Pack != Pack && !_closing)
        {
            // 20fea0 closes only the upgrade; 20fca8 retains Clank.
            // Pending requests resolve after this update's Moby advance.
            Upgrade.Advance(); Clank.Advance();
            _closing = true;
            Upgrade.Select(2, 0, 2);
            _previous = input;
            return;
        }
        if (_closing)
        {
            Upgrade!.Advance(); Clank.Advance();
            if (Upgrade.Wrapped)
            {
                Upgrade = null; Pack = 0; _closing = false;
            }
            _previous = input;
            return;
        }
        bool changed = false;
        if (Upgrade is null)
        {
            if (input.Pack == 0) { Clank.Advance(); _previous = input; return; }
            Pack = input.Pack;
            Upgrade = new(_assets.Models[605 + Pack]);
            Upgrade.Select(0, 0, 1);
            changed = true;
        }
        var pack = Upgrade!;
        _actionAge = _previous?.NativeAction == input.NativeAction ? _actionAge + 1 : 0;
#pragma warning disable S1244 // BodySelection is an unchanged selection identity stamp, not a measured duration.
        bool selection = !changed && (_previous is not { } old || old.Body.Sequence != input.Body.Sequence ||
            old.BodySelection != input.BodySelection || !old.Body.IsBlending && input.Body.IsBlending);
#pragma warning restore S1244
        if (selection)
        {
            var pairs = _assets.AnimationPairs[Pack].Where(pair => pair.RatchetSequence == input.Body.Sequence).ToArray();
            if (input.Body.Sequence != 0 && pairs.Length > 0)
            {
                var pair = pairs[_random.Below(pairs.Length)];
                if (pack.Sample.Sequence != pair.PackSequence)
                {
                    int blend = input.BlendTicks < 0 ? _assets.DefaultBlends[-input.BlendTicks - 1] : input.BlendTicks;
                    pack.Select(pair.PackSequence, input.EntryFrame, blend);
                    Clank.Select(pair.ClankSequence, input.EntryFrame, blend);
                }
            }
            else
            {
                // Idle events (table key 0) finish before closing; a body
                // selection in any other action returns both mobys to set 1.
                bool retain = input.NativeAction == 0 && _assets.AnimationPairs[Pack]
                    .Any(pair => pair.PackSequence == pack.Sample.Sequence && pair.RatchetSequence == 0);
                if (!retain) Close(_previous?.NativeAction == 8 && Pack == 2 ? 19 : 7);
            }
        }
        bool paired = _assets.AnimationPairs[Pack].Any(pair => pair.RatchetSequence == input.Body.Sequence);
        pack.Multiplier = Clank.Multiplier = paired && !changed ? input.BodyMultiplier : 1;
        // Slots advance before the standing close and per-class updater.
        pack.Advance(); Clank.Advance();
        if (pack.Wrapped && pack.Sample.Sequence == 0) pack.Select(1, 0, 2);
        if (input.NativeAction == 0)
        {
            if (pack.Wrapped || Clank.Wrapped) Close(7);
            if (input.HeadYaw > .61086524f || input.HeadPitch > .43633232f) Close(12);
            if (_actionAge > 10 && Clank.Sample.Sequence == 1 && --_idleDelay <= 0 &&
                input.HeadYaw < .55850536f && input.HeadPitch < .34906584f)
            {
                _idleDelay = NextIdleDelay();
                var idle = _assets.AnimationPairs[Pack].Where(pair => pair.RatchetSequence == 0).ToArray();
                if (pack.Sample.Sequence == 1 && idle.Length > 0)
                {
                    var pair = idle[_random.Below(idle.Length)];
                    pack.Select(pair.PackSequence, 0, 7); Clank.Select(pair.ClankSequence, 0, 7);
                }
            }
        }
        if (Pack == 2 && input.NativeAction == 8 && pack.Sample.Sequence != 6)
            pack.Select(6, 0, 10);
        _previous = input;
    }

    private int NextIdleDelay() => 110 + _random.Below(161);

    // 215ef8 / 205168: Clank's head follows the run yaw through its own
    // angular and translation springs; this is not a second Ratchet body clock.
    private void StepClankHead(Input input)
    {
        bool running = input.NativeAction == 2;
        if (running) { _headGain = .02f; _headDamping = .35f; }
        float target = running ? Math.Clamp(-Math.Clamp(input.YawVelocity, -1.4f, 1.4f) * 1.2f, -1.3089969f, 1.3089969f) : 0;
        Rac1RatchetJointEases.SpringAngle(ref _headYaw, ref _headYawVelocity, target, _headGain, _headDamping);
        float difference = (running ? .07f : 0) - _headOffset;
        _headOffsetVelocity += _headGain * difference - _headDamping * _headOffsetVelocity;
        _headOffsetVelocity = Math.Clamp(_headOffsetVelocity, -MathF.Abs(difference), MathF.Abs(difference));
        _headOffset += _headOffsetVelocity;
        if (_blinkDelay > 0) _blinkDelay--;
        if (_blinkDelay == 0 && Clank.Sample.Sequence == 1)
        {
            _blinkDelay = _random.Inclusive(50, 200);
            _blinkStep = 1;
        }
        if (_blinkStep > 0 && ++_blinkStep >= _assets.BlinkWeights.Count) _blinkStep = 0;
        float weight = _blinkStep == 0 ? 0 : _assets.BlinkWeights[_blinkStep];
        Clank.ApplyHead(_headYaw, _headOffset, _assets.BlinkLayers, weight);
    }
    private void Close(int ticks)
    {
        if (Upgrade is { } upgrade && upgrade.Sample.Sequence != 1) upgrade.Select(1, 0, ticks);
        if (Clank.Sample.Sequence != 1) Clank.Select(1, 0, ticks);
    }
}

/// <summary>
/// Forward Moby clock 250480 and positive selector 257880. Rates are native
/// sequence/frame bytes; blend overflow is rescaled into the target frame.
/// Joint channels blend before skinning, including interrupted transitions.
/// </summary>
public sealed class Rac1ClankPackAnimation
{
    private readonly Rac1ClankPackAssets.Model _model;
    private readonly PlayerSkeletalSampler _sampler;
    private PlayerSkeletalPose _pose, _source;
    private float _rate;
    private double[]? _positions;
    public Rac1AnimationFrameClock.Sample Sample { get; private set; }
    public float Multiplier { get; set; } = 1;
    public bool Wrapped { get; private set; }
    public double[] Positions => _positions ??= _sampler.Skin();
    public int NativeClass => _model.NativeClass;
    public PlayerAvatarAttachmentPose Attachment(int socket)
    {
        _ = Positions; // Resolve this pose once; all semantic sockets reuse its hierarchy.
        return _sampler.Attachment(_model.Skeletal.Skeleton.Attachments[socket].Joint);
    }

    public void ApplyHead(float yaw, float offset, IReadOnlyList<Rac1ClankPackAssets.BlinkLayer> blink, float weight)
    {
        _positions = null;
        _sampler.LoadPose(_pose);
        int head = _model.Skeletal.Skeleton.Attachments[0].Joint;
        _sampler.ComposeLocalRotation(head, Rac1RatchetJointEases.EulerQuaternion(0, 0, yaw));
        var root = _sampler.GetLocal(head);
        _sampler.SetLocal(head, root with { Translation = root.Translation!.Value + new Vector3(0, 0, offset) });
        foreach (var layer in blink)
        {
            var from = _sampler.GetLocal(layer.Joint); var to = layer.Target;
            _sampler.SetLocal(layer.Joint, new(Quaternion.Lerp(from.Rotation, to.Rotation, weight),
                Vector3.Lerp(from.Scale!.Value, to.Scale!.Value, weight), Vector3.Lerp(from.Translation!.Value, to.Translation!.Value, weight)));
        }
    }

    public Rac1ClankPackAnimation(Rac1ClankPackAssets.Model model)
    {
        _model = model; _sampler = new(model.Skeletal.Skeleton, model.Skeletal.Skin);
        _sampler.Reset(model.Skeletal.Clips[1], new(0, 0, 0));
        _pose = _source = _sampler.CapturePose();
        Sample = new(1, 0, 1 % model.Sequences[1].Frames.Count, 0, false);
        _rate = Rate(0);
    }

    public void Select(int sequence, int frame, int blend)
    {
        if (!_model.Sequences.TryGetValue(sequence, out var clip) || frame < 0 || blend < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        frame = Math.Min(frame, clip.Frames.Count - 1); // Native selector clamps the entry key.
        _source = _pose;
        Sample = new(sequence, frame, frame, 0, true);
        _rate = blend > 0 ? 1f / blend : 1;
        Multiplier = 1;
        Wrapped = false;
        if (blend == 0) Advance(); else Evaluate();
    }

    public void Advance()
    {
        Wrapped = false;
#pragma warning disable S1244 // EE 250480 freezes at exact zero; near-zero rates must continue advancing.
        if (_rate == 0 || Multiplier == 0) return;
#pragma warning restore S1244
        var sample = Sample;
        float fraction = sample.Fraction + (sample.IsBlending ? _rate : Multiplier * _rate);
        int bits = BitConverter.SingleToInt32Bits(fraction);
        // EE 2504f8 snaps the narrow bit interval surrounding 1.0.
        if (bits >= 0x3f7f0000 && bits <= 0x3f808000) fraction = 1;
        if (fraction >= 1)
        {
            do
            {
                int frame = sample.NextFrame;
                int next = (frame + 1) % _model.Sequences[sample.Sequence].Frames.Count;
                Wrapped |= next == 0;
                fraction = (fraction - 1) / _rate;
                sample = new(sample.Sequence, frame, next, 0, false);
                _rate = Rate(frame);
                fraction *= _rate;
            } while (fraction > 1);
        }
        Sample = sample with { Fraction = fraction };
        Evaluate();
    }

    private float Rate(int frame)
    {
        var clip = _model.Sequences[Sample.Sequence];
        return clip.ConstantTransitionRate > 0 ? clip.ConstantTransitionRate : clip.Frames[frame].TransitionRate;
    }

    private void Evaluate()
    {
        _positions = null;
        var s = Sample;
        _sampler.Reset(_model.Skeletal.Clips[s.Sequence],
            s.IsBlending ? new(s.NextFrame, s.NextFrame, 0) : new(s.CurrentFrame, s.NextFrame, s.Fraction));
        if (s.IsBlending) _sampler.BlendFrom(_source, s.Fraction);
        _pose = _sampler.CapturePose();
    }
}
