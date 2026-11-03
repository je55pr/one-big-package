using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Animation;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1RedPlantAnimationTests
{
    [SkippableFact]
    public void VeldinRedPlantsExposeOnlyRetailAdmittedReactionClip()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, 0);
        var dynamicObjects = Assert.IsAssignableFrom<IReadOnlyList<RuntimeDynamicObject>>(world.DynamicObjects);

        var redPlants = dynamicObjects
            .Where(o => o.NativeClassId == Rac1MobyAnimationProvider.RedPlantClassId)
            .OrderBy(o => o.InstanceIndex)
            .ToArray();
        Assert.Equal(33, redPlants.Length);

        var specimen = redPlants[0];
        Assert.NotNull(specimen.Animations);
        Assert.Equal(RuntimeObjectAnimationRole.Rest, specimen.Animations!.InitialRole);
        var live = RuntimeEntityState.FromAuthored(specimen);
        Assert.Equal(RuntimeEntityPresence.Active, live.Presentation.Presence);
        Assert.Equal(RuntimeObjectAnimationRole.Rest, live.Presentation.AnimationRole);
        var reacting = Rac1MobyAnimationProvider.AdvanceRedPlantEntityState(
            specimen, live, playerDistance: 0.5, playerMotionPerTick: 0.1, nativeAnimationFlags: 0);
        Assert.Equal(RuntimeObjectAnimationRole.Reaction, reacting.Presentation.AnimationRole);
        Assert.Equal(RuntimeEntityPresence.Active, reacting.Presentation.Presence);
        var reaction = Assert.Single(specimen.Animations.Clips);
        Assert.Equal(("reaction", RuntimeObjectAnimationRole.Reaction), (reaction.Id, reaction.Role));
        Assert.Equal(20, reaction.FrameCount);
        Assert.Equal(30f, reaction.ConstantFramesPerSecond);
        Assert.Equal(specimen.Meshes.Count, reaction.Surfaces.Count);
        Assert.All(reaction.Surfaces, surface =>
        {
            Assert.InRange(surface.SurfaceIndex, 0, specimen.Meshes.Count - 1);
            var mesh = specimen.Meshes[surface.SurfaceIndex];
            Assert.Equal(20, surface.FrameCount);
            Assert.All(surface.LocalFrames, frame =>
            {
                Assert.Equal(mesh.Positions.Length, frame.Length);
                Assert.All(frame, value => Assert.True(double.IsFinite(value)));
            });
            Assert.Contains(surface.LocalFrames,
                frame => !frame.SequenceEqual(mesh.Positions));
        });

        Assert.All(redPlants, plant =>
        {
            Assert.NotNull(plant.Animations);
            var clip = Assert.Single(plant.Animations!.Clips);
            Assert.Equal(RuntimeObjectAnimationRole.Reaction, clip.Role);
            Assert.Equal(20, clip.FrameCount);
            Assert.Equal(plant.Meshes.Count, clip.Surfaces.Count);
        });

        var greenPlants = dynamicObjects.Where(o => o.NativeClassId == 1782).ToArray();
        Assert.Equal(36, greenPlants.Length);
        Assert.All(greenPlants, plant => Assert.Null(plant.Animations));
        Assert.All(dynamicObjects.Where(o => o.NativeClassId != Rac1MobyAnimationProvider.RedPlantClassId),
            obj => Assert.Null(obj.Animations));
    }
}
