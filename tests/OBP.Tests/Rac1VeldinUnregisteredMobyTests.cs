using OBP.RAC1;
using OBP.RAC1.Level;

namespace OBP.Tests;

/// <summary>Source-backed Veldin untextured Moby presentation regression (#397).</summary>
public sealed class Rac1VeldinUnregisteredMobyTests
{
    [SkippableFact]
    public void Level0_UnregisteredClass1964KeepsNativeIdentityWithoutOpaqueFakePanels()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");

        using var reader = Rac1VerifiedDisc.Open(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(entry => entry.LevelId == 0);
        var core = Rac1LevelCore.Open(reader, level);
        var classes = Rac1StaticClasses.Read(core);
        var instances = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level));

        // Authorised NTSC-U level 0 contains a single class-1964 instance
        // (native index 295). The overlay's registration list does not include
        // class 1964 and its model has no source-textured material: all thirty
        // triangles select slot -1. The fallback beige tint is invented host
        // presentation, not an admitted native surface.
        var source = classes.Mobies[1964];
        Assert.Equal(30, source.Mesh.Indices.Length / 3);
        Assert.All(source.TriangleTextureIds, id => Assert.Equal(-1, id));
        var placement = Assert.Single(instances.MobyInstances, i => i.OClass == 1964);
        Assert.Equal(295, placement.Index);

        var world = Rac1WorldImport.Build(reader, 0);
        var dynamicObjects = Assert.IsAssignableFrom<IReadOnlyList<OBP.Runtime.RuntimeDynamicObject>>(
            world.DynamicObjects);
        var runtime = Assert.Single(dynamicObjects, obj => obj.NativeClassId == 1964);
        Assert.Equal(295, runtime.InstanceIndex);
        Assert.Equal("moby:1964", runtime.ModelRef);
        Assert.Equal("moby:295", runtime.InteractionId);
        Assert.Empty(runtime.Meshes);
        Assert.Contains(runtime.NativePayloads!, payload => payload.Format == "rac1-moby-instance-0x78");

        // Neither the authored object registry nor the surrounding textured
        // fence/ship renderers may be removed to mask the problem.
        Assert.Equal(instances.MobyInstances.Count, dynamicObjects.Count);
        Assert.Contains(world.Meshes, mesh => mesh.AssetKind == "tie" &&
            mesh.TextureId == 41 && mesh.TriangleCount > 0);
        Assert.Contains(dynamicObjects, obj => obj.NativeClassId == 530 &&
            obj.InstanceIndex == 142 && obj.Meshes.Count > 0);
    }
}