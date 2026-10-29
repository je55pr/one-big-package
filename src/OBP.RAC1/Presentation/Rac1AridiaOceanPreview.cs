using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.RAC1.Presentation;

/// <summary>
/// A bounded source-grid preview of Aridia's class-854 sand sea. Record placement,
/// height, step, resolution and texture frame are retail-authored; constant height
/// and one repeating texture per 7x7 cell are temporary presentation choices
/// until VU0 wave displacement, VU1 STQ and native GS blending are recovered.
/// Unlike the old missing surface, this geometry is NOT inferred from collision.
/// </summary>
public static class Rac1AridiaOceanPreview
{
    public const string AssetKind = "rac1-ocean-preview";
    public const int FirstTextureId = Rac1AridiaOceanSource.AnimatedFxStart;

    public static (IReadOnlyList<RuntimeMesh> Meshes, RuntimeTexture Texture) Build(
        Rac1LevelOverlay overlay, Rac1LevelCore.Core core)
    {
        var records = Rac1AridiaOceanSource.Decode(overlay);
        var tex = Rac1FxTextures.Read(core, new HashSet<int> { FirstTextureId })[FirstTextureId];
        var texture = tex with { AssetKind = AssetKind };
        var meshes = new List<RuntimeMesh>(records.Count);
        foreach (var record in records)
        {
            // A 7-by-7 grid is the source's basic draw cell. Keep individual
            // vertices per cell rather than pretending they share UVs or colour.
            var positions = new List<double>();
            var uvs = new List<float>();
            var indices = new List<int>();
            for (int row = 0; row < record.CellRows; row++)
                for (int col = 0; col < record.CellColumns; col++)
                {
                    // The overlay stores a 0/1 flag for each native cell. Blank
                    // cells are genuinely absent, not transparent polygons.
                    if (record.CellMask[row * record.CellColumns + col] == 0)
                        continue;
                    int baseVertex = positions.Count / 3;
                    for (int v = 0; v <= 7; v++)
                        for (int u = 0; u <= 7; u++)
                        {
                            double x = record.OriginX + (col * 7 + u) * record.StepX;
                            double y = record.OriginY + (row * 7 + v) * record.StepY;
                            positions.Add(x);
                            positions.Add(record.Height); // Native Z-up -> OBP Y-up.
                            positions.Add(y);
                            uvs.Add(u / 7f);
                            uvs.Add(v / 7f);
                        }

                    for (int v = 0; v < 7; v++)
                        for (int u = 0; u < 7; u++)
                        {
                            int a = baseVertex + v * 8 + u;
                            int b = a + 1;
                            int c = a + 8;
                            int e = c + 1;
                            // Upward in OBP Y-up before the Godot X-reflection.
                            indices.Add(a); indices.Add(c); indices.Add(b);
                            indices.Add(b); indices.Add(c); indices.Add(e);
                        }
                }

            meshes.Add(new RuntimeMesh(AssetKind, FirstTextureId,
                positions.ToArray(), uvs.ToArray(), indices.ToArray(),
                MaterialPresentation: new RuntimeMaterialPresentation(
                    WrapS: RuntimeTextureWrap.Repeat,
                    WrapT: RuntimeTextureWrap.Repeat,
                    AlphaBlendEnabled: true),
                PresentationGroup: $"aridia-ocean-{record.SourceAddress:x8}"));
        }

        return (meshes, texture);
    }
}