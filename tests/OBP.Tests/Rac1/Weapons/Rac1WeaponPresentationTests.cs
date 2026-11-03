using OBP.IO;
using OBP.RAC1.Animation;
using OBP.RAC1.Level;
using OBP.RAC1.Player;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1WeaponPresentationTests
{
    [SkippableFact]
    public void RetailWeaponSoundRolesResolveIncludingAllTauntsAndTheTwoPhaseMorphLoop()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(path!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(level => level.LevelId == 0);
        var voices = Rac1CrateAudio.Decode(disc, level, Rac1LevelCore.Open(disc, level));
        foreach (var key in new[] { (0, 26), (0, 30), (0, 53), (176, 1), (168, 1), (849, 2), (157, 0), (177, 4), (185, 0), (186, 4), (457, 1), (203, 1) })
        {
            Assert.True(voices.TryGetValue(key, out var voice), $"Missing source sound {key}");
            Assert.Contains(voice.Clip.InterleavedPcm16, sample => sample != 0);
            Assert.True(voice.Gain > 0);
        }
        for (int step = 0; step < 6; step++)
        {
            var sound = voices[(175, step)];
            Assert.Equal(123 + step, sound.BankSoundId);
            Assert.Contains(sound.Clip.InterleavedPcm16, sample => sample != 0);
        }
        var blaster = voices[(168, 1)];
        Assert.True(blaster.Loop); Assert.Equal(116, blaster.BankSoundId);
        Assert.InRange(blaster.Clip.DurationSeconds, .44, .46); // A four-shot sustained sample, not one-shot playback.
        Assert.Equal(28, blaster.Clip.LoopStartFrame);
        var morph = voices[(185, 0)];
        Assert.True(morph.Loop); Assert.True(morph.Clip.LoopStartFrame > 0);
        Assert.True(morph.Clip.LoopEndFrame > morph.Clip.LoopStartFrame);
        var grunts = voices[(0, 53)].Alternatives!;
        Assert.Contains(grunts, clip => clip.FrameCount == 0);
        Assert.Contains(grunts, clip => clip.FrameCount > 0);
    }

    [SkippableFact]
    public void EquipmentLayersUseTheSourceArmJointListsAndRebuildHierarchyBeforeSkinning()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(path!);
        var core = Rac1LevelCore.Open(disc, Rac1DiscIndex.Read(disc).Levels.Single(level => level.LevelId == 0));
        var cls = Rac1StaticClasses.Read(core).Mobies[0];
        var payload = core.Assets.AsSpan(cls.AssetOffset, cls.AssetLength);
        var right = Rac1MobyAttachment.ReadAnimationLayerJoints(payload, 12);
        var left = Rac1MobyAttachment.ReadAnimationLayerJoints(payload, 13);
        Assert.Equal(Enumerable.Range(52, 20), right);
        Assert.Equal(Enumerable.Range(72, 20), left);
        var hero = Rac1MobyAnimation.ReadRatchetSequences(core.Assets, core.Index, core.Header.RatchetSequencesOffset, 111);
        Assert.Equal(new[] { (2.5f, (ushort)53), (4f, (ushort)30) },
            hero[103].Value!.TimedSoundCues.Select(cue => (cue.PositionFrames, cue.SoundId)));
        var asset = Rac1RatchetAvatar.Decode(core);
        var rig = new Rac1RatchetEquipmentRig(asset, right.Concat(left).ToArray());
        var standing = asset.Clip(0).LocalFrames[0];
        foreach (int sequence in new[] { 51, 54, 56, 81, 103 })
        {
            var clip = asset.Clip(sequence);
            foreach (int frame in new[] { 0, clip.FrameCount - 1 })
            {
                var actual = rig.Sample(new(0, 0, 0, 0, false), new(sequence, frame, frame, 0, false), 1, true, standing);
                Assert.All(actual.Zip(clip.LocalFrames[frame]), pair => Assert.InRange(Math.Abs(pair.First - pair.Second), 0, .00001));
            }
        }
        var mask = right.Concat(left).ToHashSet();
        var run = asset.Clip(4).LocalFrames[10];
        var moving = rig.Sample(new(4, 10, 10, 0, false), new(57, 2, 2, 0, false), 1, false, run);
        int unaffected = 0;
        for (int vertex = 0; vertex < moving.Length / 3; vertex++)
        {
            if (Enumerable.Range(0, 3).Any(influence => asset.Mesh.VertexWeights[vertex * 3 + influence] > 0 &&
                mask.Contains(asset.Mesh.VertexJoints[vertex * 3 + influence]))) continue;
            unaffected++;
            for (int axis = 0; axis < 3; axis++) Assert.Equal(run[vertex * 3 + axis], moving[vertex * 3 + axis]);
        }
        Assert.True(unaffected > 2500); // Includes the head/neck, torso and both legs.
        Assert.False(moving.SequenceEqual(run));
    }
}
