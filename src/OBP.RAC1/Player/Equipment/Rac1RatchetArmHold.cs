using System.Numerics;
using OBP.Core;
using OBP.RAC1.Animation;
using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>
/// Source data for the held-weapon arm layers. Retail runs two animation slots on Ratchet whenever the
/// selected item has arm animations (item table +0x18): slot 0 uses moby class 1 over joint list 12
/// (the right arm) and slot 1 uses class 2 over list 13 (the left arm). Each class carries 23
/// sequences with the same frame counts as Ratchet's sequences 0..22, so while the body plays one of
/// those, the arms play the same sequence and frame in their "holding a gun" version
/// (<c>0x20dd20</c>). A weapon is therefore held in the idle, run and jump poses, not only while firing.
/// </summary>
public sealed record Rac1RatchetArmHoldSource(
    IReadOnlyList<IReadOnlyList<int>> BankJoints,
    IReadOnlyList<IReadOnlyList<Rac1MobyAnimation.SequenceSlot>> Sequences);

internal static class Rac1RatchetArmHold
{
    public const int SlotCount = 2;
    /// <summary>The arm classes only carry sequences below this id (<c>0x20dd20</c>: <c>id &lt; 0x17</c>).</summary>
    public const int SequenceLimit = 23;

    /// <summary>One clip dictionary per slot, keyed by the shared body sequence id.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<int, PlayerSkeletalClip>> BuildClips(
        Rac1RatchetArmHoldSource source, PlayerSkeletalAsset body, float vectorScale)
    {
        if (source.BankJoints.Count != SlotCount || source.Sequences.Count != SlotCount)
            throw new InvalidDataException("R&C1 arm hold needs exactly two slots.");
        var result = new List<IReadOnlyDictionary<int, PlayerSkeletalClip>>(SlotCount);
        for (int slot = 0; slot < SlotCount; slot++)
        {
            var bank = source.BankJoints[slot];
            var clips = new Dictionary<int, PlayerSkeletalClip>();
            for (int sequence = 0; sequence < SequenceLimit && sequence < source.Sequences[slot].Count; sequence++)
            {
                if (source.Sequences[slot][sequence].Value is not { } native ||
                    !body.Clips.TryGetValue(sequence, out var reference) ||
                    native.Frames.Count != reference.Frames.Count)
                    continue; // Only sequences that line up with the body clock can follow it.
                var frames = native.Frames.Select((frame, i) =>
                {
                    if (frame.JointRotations.Count != bank.Count)
                        throw new InvalidDataException($"R&C1 arm class sequence {sequence} joint count differs from its bank.");
                    var channels = new PlayerJointChannels[bank.Count];
                    for (int k = 0; k < bank.Count; k++)
                    {
                        var q = frame.JointRotations[k];
                        channels[k] = new(new(q.Xf, q.Yf, q.Zf, q.Wf), Vector3.One, body.Skeleton.Joints[bank[k]].Translation);
                    }
                    foreach (ulong word in frame.Thing1)
                    {
                        int k = (int)(word >> 48) & 0x7fff;
                        if (k < channels.Length)
                            channels[k] = channels[k] with
                            {
                                Scale = new Vector3((short)word, (short)(word >> 16), (short)(word >> 32)) / 4096,
                            };
                    }
                    foreach (ulong word in frame.Thing2)
                    {
                        int k = (int)(word >> 48);
                        if (k < channels.Length)
                            channels[k] = channels[k] with
                            {
                                Translation = new Vector3((short)word, (short)(word >> 16), (short)(word >> 32)) * vectorScale,
                            };
                    }
                    return new PlayerSkeletalFrame(channels, reference.Frames[i].DurationSeconds, reference.Frames[i].TimelinePosition);
                }).ToArray();
                clips[sequence] = new PlayerSkeletalClip(ObpSourceGame.Rac1, reference.BuildId, slot + 1, sequence,
                    bank.ToArray(), frames);
            }
            result.Add(clips);
        }
        return result;
    }
}
