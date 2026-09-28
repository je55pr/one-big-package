using OBP.IO;
using OBP.RAC3.Level;

namespace OBP.Tests;

public sealed class UyaTargetGroupTests
{
    [SkippableFact]
    public void RetailTable1TargetGroupsMatchRecoveredNativeLoader()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        UyaLevelCore.OpenedLevel opened = UyaLevelCore.Open(reader, 1);
        UyaGameplay.Gameplay gameplay = UyaGameplay.Read(opened.GameplayReader);

        Assert.Equal(125, gameplay.TargetVolumes.Count);
        Assert.All(
            gameplay.TargetVolumes,
            volume => Assert.Equal(0x80, volume.RawRecord.Length));
        Assert.Equal(159, gameplay.TargetPolygons.Count);
        Assert.Equal(106, gameplay.TargetGroups.Count);

        UyaGameplay.TargetVolume volume9 = gameplay.TargetVolumes[9];
        Assert.Equal(344.132233f, volume9.Center.X, precision: 4);
        Assert.Equal(268.649445f, volume9.Center.Y, precision: 4);
        Assert.Equal(79.191124f, volume9.Center.Z, precision: 4);
        Assert.Equal(0.491899f, volume9.InverseColumnX.X, precision: 5);
        Assert.Equal(-0.125271f, volume9.InverseColumnX.Y, precision: 5);
        Assert.Equal(0f, volume9.InverseColumnX.Z);
        Assert.Equal(0.870652f, volume9.InverseColumnY.X, precision: 5);
        Assert.Equal(0.070775f, volume9.InverseColumnY.Y, precision: 5);
        Assert.Equal(0f, volume9.InverseColumnY.Z);
        Assert.Equal(0f, volume9.InverseColumnZ.X);
        Assert.Equal(0f, volume9.InverseColumnZ.Y);
        Assert.Equal(0.318239f, volume9.InverseColumnZ.Z, precision: 5);
        Assert.True(volume9.Contains(344.132233f, 268.649445f, 79.191124f));
        Assert.True(volume9.Contains(344.132233f + 0.99f * 0.491899f, 268.649445f + 0.99f * 0.870652f, 79.191124f));
        Assert.False(volume9.Contains(344.132233f + 1.01f * 0.491899f, 268.649445f + 1.01f * 0.870652f, 79.191124f));

        Assert.Equal(12, gameplay.TargetPolygons[8].Vertices.Count);
        Assert.Equal(12, gameplay.TargetPolygons[17].Vertices.Count);
        Assert.Equal(7, gameplay.TargetPolygons[18].Vertices.Count);
        Assert.Equal(24, gameplay.TargetPolygons[69].Vertices.Count);
        Assert.Equal(12, gameplay.TargetPolygons[97].Vertices.Count);
        Assert.Equal(23, gameplay.TargetPolygons[106].Vertices.Count);
        Assert.Equal(21, gameplay.TargetPolygons[113].Vertices.Count);

        AssertGroup(gameplay, 7, 405.696777f, 150.271667f, 77.617119f, 8.599070f, 8, null);
        AssertGroup(gameplay, 13, 396.032715f, 177.981873f, 86.440277f, 14.734653f, 18, null);
        AssertGroup(gameplay, 18, 396.076263f, 178.861725f, 85.206528f, 28.526443f, 17, null);
        AssertGroup(gameplay, 24, 346.612030f, 274.601532f, 76.946808f, 59.392933f, 69, null);
        AssertGroup(gameplay, 26, 344.132233f, 268.649445f, 79.191124f, 9.984863f, null, 9);
        AssertGroup(gameplay, 27, 385.582092f, 137.495697f, 84.435158f, 13.827257f, null, 117);
        AssertGroup(gameplay, 66, 393.224030f, 161.651550f, 75.476601f, 11.866180f, 97, null);
        AssertGroup(gameplay, 73, 363.138947f, 122.205894f, 77.990746f, 25.416681f, 106, null);
        AssertGroup(gameplay, 79, 354.838928f, 223.180603f, 79.563789f, 28.386227f, null, 81);
        AssertGroup(gameplay, 82, 400.901245f, 388.617493f, 79.109177f, 16.958359f, 113, null);

        var included = gameplay.MobyInstances[465].Position;
        Assert.True(gameplay.TryContainsTargetGroup(24, included.X, included.Y, included.Z, out bool group24Included));
        Assert.True(group24Included);

        var polygonRejected = gameplay.MobyInstances[445].Position;
        Assert.True(gameplay.TryContainsTargetGroup(24, polygonRejected.X, polygonRejected.Y, polygonRejected.Z, out bool group24Rejected));
        Assert.False(group24Rejected);

        var volumeCenter = gameplay.TargetGroups[26].Center;
        Assert.True(gameplay.TryContainsTargetGroup(26, volumeCenter.X, volumeCenter.Y, volumeCenter.Z, out bool group26Center));
        Assert.True(group26Center);

        var unsupportedCenter = gameplay.TargetGroups[52].Center;
        Assert.False(gameplay.TryContainsTargetGroup(52, unsupportedCenter.X, unsupportedCenter.Y, unsupportedCenter.Z, out bool unsupported));
        Assert.False(unsupported);

        Assert.Empty(gameplay.TargetGroups.SelectMany(group => group.UnknownList2));
        Assert.Empty(gameplay.TargetGroups.SelectMany(group => group.UnknownList4));
        Assert.Equal(
            [0],
            gameplay.TargetGroups[52].UnknownList3);
        Assert.Equal(
            1,
            gameplay.TargetGroups.Sum(group => group.UnknownList3.Count));
    }

    private static void AssertGroup(
        UyaGameplay.Gameplay gameplay,
        int index,
        float x,
        float y,
        float z,
        float radius,
        int? polygon,
        int? volume)
    {
        UyaGameplay.TargetGroup group = gameplay.TargetGroups[index];
        Assert.Equal(index, group.Index);
        Assert.Equal(x, group.Center.X, precision: 4);
        Assert.Equal(y, group.Center.Y, precision: 4);
        Assert.Equal(z, group.Center.Z, precision: 4);
        Assert.Equal(radius, group.Radius, precision: 4);
        Assert.Equal(
            polygon is int polygonIndex ? [polygonIndex] : [],
            group.PolygonRegionIndices);
        Assert.Equal(
            volume is int volumeIndex ? [volumeIndex] : [],
            group.OrientedVolumeIndices);
        Assert.Empty(group.UnknownList2);
        Assert.Empty(group.UnknownList3);
        Assert.Empty(group.UnknownList4);
    }
}
