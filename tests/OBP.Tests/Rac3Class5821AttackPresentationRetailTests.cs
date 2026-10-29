using OBP.IO;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

/// <summary>
/// Source-native animation clip witness. This does not claim the absent
/// TABLE1 autonomous state clock, motion, reaction or crowd choreography.
/// </summary>
public sealed class Rac3Class5821AttackPresentationRetailTests
{
    [SkippableFact]
    public void StateTenActionTwelveUsesOriginalSurfaceRemapsAndTwentyNativeFrames()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_UYA_ISO unavailable.");

        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac3WorldImport.BuildObserved(reader, 1, nativeMobyPoseOverride: true).World;
        var class5821 = world.DynamicObjects!
            .Where(actor => actor.NativeClassId == Rac3Class5821Actor.NativeClassId)
            .ToArray();
        Assert.Equal(62, class5821.Length);
        var actor = class5821.First(source => source.Meshes.Count > 0);

        var clip = Rac3WorldImport.LoadClass5821State10Clip(iso!, actor.Meshes);

        Assert.Equal("uya-class5821-state10-action12", clip.Id);
        Assert.Equal(RuntimeObjectAnimationRole.Reaction, clip.Role);
        Assert.Equal(20, clip.FrameCount);
        Assert.Equal(20, clip.FrameDurationsSeconds.Count);
        Assert.All(clip.FrameDurationsSeconds, seconds => Assert.True(seconds > 0 && double.IsFinite(seconds)));
        Assert.Equal(actor.Meshes.Count, clip.Surfaces.Count);

        // All native sequence payloads are now individually visual-auditable.
        // Their *role* in the class update is NOT inferred from their indices.
        var actions = Rac3WorldImport.LoadClass5821VisualAuditClips(iso!, actor.Meshes);
        Assert.Equal(13, actions.Count);
        int[] frameCounts = [12, 18, 18, 18, 15, 8, 31, 10, 11, 8, 13, 10, 20];
        for (int sequence = 0; sequence < actions.Count; sequence++)
        {
            var action = actions[sequence];
            Assert.Equal(frameCounts[sequence], action.FrameCount);
            Assert.Equal(actor.Meshes.Count, action.Surfaces.Count);
            Assert.Equal(sequence == 12 ? clip.Id : $"uya-class5821-unassigned-sequence{sequence:D2}", action.Id);
            Assert.All(action.Surfaces, surface =>
                Assert.All(surface.LocalFrames, frame =>
                {
                    Assert.Equal(actor.Meshes[surface.SurfaceIndex].Positions.Length, frame.Length);
                    Assert.All(frame, value => Assert.True(double.IsFinite(value)));
                }));
        }
        Assert.Equal(clip.Surfaces[0].LocalFrames[0], actions[12].Surfaces[0].LocalFrames[0]);

        foreach (var surface in clip.Surfaces)
        {
            int index = surface.SurfaceIndex;
            Assert.InRange(index, 0, actor.Meshes.Count - 1);
            Assert.Equal(20, surface.FrameCount);
            Assert.All(surface.LocalFrames, frame =>
            {
                Assert.Equal(actor.Meshes[index].Positions.Length, frame.Length);
                Assert.All(frame, value => Assert.True(double.IsFinite(value)));
            });
        }

        // Recovered UYA action12 is NOT an idle loop. The clip must not
        // assert any autonomous transition into state 8 or out of state 10.
        Assert.Throws<InvalidDataException>(() =>
            Rac3WorldImport.LoadClass5821State10Clip(iso!, actor.Meshes.Skip(1).ToArray()));
    }
}
