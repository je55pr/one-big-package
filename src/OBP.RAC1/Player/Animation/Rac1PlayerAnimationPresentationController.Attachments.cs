using System.Runtime.CompilerServices;
using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

public sealed partial class Rac1PlayerAnimationPresentationController
{
    // Blend snapshots already retain their vertex array. Keeping the matching
    // attachment under the same weak key transfers both through interrupted
    // blends without an unbounded simulation-history cache.
    private readonly ConditionalWeakTable<double[], Dictionary<string, PlayerAvatarAttachmentPose>> _attachments = new();

    public PlayerAvatarAttachmentPose? CurrentAttachmentPose(string key)
    {
        double[] pose = Current.SampledPose?.LocalPositions ??
            CurrentClip.LocalFrames[CurrentClip.FrameIndexAt(Current.ElapsedAt(_clockSeconds), Current.Loop)];
        return _attachments.TryGetValue(pose, out var entries) && entries.TryGetValue(key, out var attachment)
            ? attachment : null;
    }

    private void RegisterAttachmentFrames()
    {
        foreach (var clip in _avatar.AnimationClips)
        {
            if (clip.AttachmentFrames is null)
                continue;
            foreach (var track in clip.AttachmentFrames)
            {
                if (string.IsNullOrWhiteSpace(track.Key) || track.Value.Count != clip.FrameCount ||
                    track.Value.Any(pose => !pose.IsFinite))
                    throw new InvalidDataException("Avatar attachment track does not match its clip.");
                for (int frame = 0; frame < clip.FrameCount; frame++)
                    _attachments.GetOrCreateValue(clip.LocalFrames[frame])[track.Key] = track.Value[frame];
            }
        }
    }

    private void SampleAttachments(double[] result, double[] source, double[] target, float amount)
    {
        if (!_attachments.TryGetValue(target, out var to))
            return;
        _attachments.TryGetValue(source, out var from);
        var sampled = _attachments.GetOrCreateValue(result);
        foreach (var pair in to)
            sampled[pair.Key] = from is not null && from.TryGetValue(pair.Key, out var old)
                ? PlayerAvatarAttachmentPose.Interpolate(old, pair.Value, amount) : pair.Value;
    }
}
