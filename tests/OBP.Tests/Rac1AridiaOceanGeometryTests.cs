using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1AridiaOceanGeometryTests
{
    [SkippableFact]
    public void NativeGeometryUsesSourceStripsAndDoesNotInventAlphaOrCollision()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(l => l.LevelId == 2);
        var overlay = Rac1LevelOverlay.Open(disc, level);
        var records = Rac1AridiaOceanSource.Decode(overlay);
        var meshes = Rac1AridiaOceanGeometry.Read(overlay);

        Assert.Equal(7, meshes.Count);
        Assert.Equal(136 * 124, meshes.Sum(m => m.VertexCount));
        Assert.Equal(136 * 98, meshes.Sum(m => m.TriangleCount));
        Assert.Equal([57, 8, 31, 20, 17, 2, 1],
            meshes.Select(m => m.VertexCount / 124));

        for (int i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            Assert.Equal(records[i].SourceAddress, mesh.SourceAddress);
            Assert.Equal(mesh.VertexCount * 2, mesh.TextureST.Length);
            Assert.Equal(mesh.VertexCount * 3, mesh.PackedRgb.Length);
            Assert.Equal(0, mesh.TriangleIndices.Length % 3);
            Assert.All(mesh.TriangleIndices, index => Assert.InRange(index, 0, mesh.VertexCount - 1));
            Assert.All(Enumerable.Range(0, mesh.VertexCount), index =>
                Assert.Equal(records[i].Height, mesh.PositionsYUp[index * 3 + 1]));
        }

        var firstRecord = records[0];
        int firstCell = Array.IndexOf(firstRecord.CellMask, (byte)1);
        int col = firstCell % firstRecord.CellColumns;
        int row = firstCell / firstRecord.CellColumns;
        var first = meshes[0];
        Assert.Equal(firstRecord.OriginX + col * 7 * firstRecord.StepX, first.PositionsYUp[0], 5);
        Assert.Equal(firstRecord.OriginY + row * 7 * firstRecord.StepY, first.PositionsYUp[2], 5);
        Assert.Equal(-0.5f, first.TextureST[0]);
        Assert.Equal(-0.5f, first.TextureST[1]);

        var sourceColour = Rac1AridiaOceanSource.ReadCellPackets(overlay, firstRecord)[firstCell].PackedValues;
        Assert.Equal(sourceColour[0], first.PackedRgb[0]);
        Assert.Equal(sourceColour[1], first.PackedRgb[1]);
        Assert.Equal(sourceColour[2], first.PackedRgb[2]);
    }
}