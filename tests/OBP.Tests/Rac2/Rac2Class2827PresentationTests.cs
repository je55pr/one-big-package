using OBP.IO;
using OBP.RAC2;
using OBP.RAC2.Animation;
using OBP.RAC2.Gameplay;

namespace OBP.Tests;

public sealed class Rac2Class2827PresentationTests
{
    [SkippableFact]
    public void RetailOpeningActorUsesItsWitnessedChaseAttackAndRecoveryClips()
    {
        var path = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(path), "OBP_GC_ISO not set");
        using var reader = new FileRandomAccessReader(path!);
        var world = Rac2WorldImport.Build(reader, 0);
        var source = world.DynamicObjects!.First(actor => actor.NativeClassId == Rac2Class2827HostileSession.NativeClassId);
        var asset = Rac2Class2827Presentation.Load(path!, source.Meshes,
            nativePosePreview: OBP.RAC2.Level.Rac2MobyClasses.NativePoseEnabledForLevel(0));
        Assert.Equal(new[] { 11, 16, 17, 27 }, asset.Clips.Keys.Order());
        foreach (var clip in asset.Clips.Values)
        {
            Assert.True(clip.FrameCount > 1);
            Assert.Equal(source.Meshes.Count, clip.Surfaces.Count);
            Assert.All(clip.FrameDurationsSeconds, duration => Assert.True(double.IsFinite(duration) && duration > 0));
            foreach (var surface in clip.Surfaces)
            {
                Assert.All(surface.LocalFrames, frame => Assert.Equal(source.Meshes[surface.SurfaceIndex].Positions.Length, frame.Length));
                Assert.Contains(surface.LocalFrames.Skip(1), frame => !frame.SequenceEqual(surface.LocalFrames[0]));
            }
        }
        var chase = asset.Clips[11];
        int firstLoopTicks = checked((int)Math.Ceiling(chase.DurationSeconds * 60));
        Assert.InRange(Rac2Class2827Presentation.Sample(asset, 11, firstLoopTicks).Frame, 0, 1);
        var finishedAttack = Rac2Class2827Presentation.Sample(asset, 27, 100_000);
        Assert.Equal(asset.Clips[27].FrameCount - 1, finishedAttack.Frame);
        Assert.Equal(finishedAttack.Frame, finishedAttack.NextFrame);
    }
}
