using OBP.Core.Math;
using OBP.Fusion;
using OBP.Runtime;

namespace OneBigPackage;

/// <summary>
/// Original OBP greybox, rendered and collided through the same RuntimeWorld
/// presentation and WorldHost pipeline as original planets. No native parser.
/// </summary>
internal static class FusionLabWorldFactory
{
    private const double WallThickness = 0.36;
    private const double WallHeight = 5.2;
    private const double DoorHeight = 3.7;

    public static RuntimeWorld Build(FusionLabDefinition definition)
    {
        definition.Validate();
        var meshes = new List<RuntimeMesh>();
        var collision = new List<RuntimeCollisionBlob>();
        int boxIndex = 0;

        void Box(double x, double y, double z, double width, double height, double depth,
            double[] tint, string part)
        {
            var positions = new List<double>(72);
            var indices = new List<int>(36);

            void Face((double X, double Y, double Z) a, (double X, double Y, double Z) b,
                      (double X, double Y, double Z) c, (double X, double Y, double Z) d)
            {
                int first = positions.Count / 3;
                foreach (var point in new[] { a, b, c, d })
                {
                    positions.Add(point.X);
                    positions.Add(point.Y);
                    positions.Add(point.Z);
                }
                indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }

            double x0 = x - width / 2, x1 = x + width / 2;
            double y0 = y - height / 2, y1 = y + height / 2;
            double z0 = z - depth / 2, z1 = z + depth / 2;
            Face((x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0)); // top
            Face((x1, y0, z0), (x1, y0, z1), (x0, y0, z1), (x0, y0, z0)); // bottom
            Face((x1, y1, z0), (x1, y1, z1), (x1, y0, z1), (x1, y0, z0)); // right
            Face((x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)); // left
            Face((x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)); // north
            Face((x0, y1, z0), (x1, y1, z0), (x1, y0, z0), (x0, y0, z0)); // south

            int vertexCount = positions.Count / 3;
            var colors = new float[vertexCount * 4];
            for (int i = 0; i < vertexCount; i++)
            {
                colors[4 * i] = (float)tint[0];
                colors[4 * i + 1] = (float)tint[1];
                colors[4 * i + 2] = (float)tint[2];
                colors[4 * i + 3] = 1f;
            }

            int id = boxIndex++;
            meshes.Add(new RuntimeMesh("fusion-" + part, id, positions.ToArray(),
                new float[vertexCount * 2], indices.ToArray(), colors,
                RenderWithoutTexture: true));
            collision.Add(new RuntimeCollisionBlob(0, positions.ToArray(), indices.ToArray(),
                new int[indices.Count / 3]));
        }

        foreach (var room in definition.Rooms)
        {
            var tint = room.Tint;
            var floorTint = tint.Select(c => Math.Clamp(c * 0.60, 0, 1)).ToArray();
            var trimTint = tint.Select(c => Math.Clamp(c * 1.22, 0, 1)).ToArray();
            Box(room.CenterX, -0.45, room.CenterZ, room.Width, 0.9, room.Depth, floorTint, "floor");

            // Side walls; no invisible doors or teleports. All passages share a
            // continuous physical floor and a collision-sized open doorway.
            Box(room.CenterX - room.Width / 2 + WallThickness / 2,
                WallHeight / 2, room.CenterZ, WallThickness, WallHeight,
                room.Depth, tint, "wall");
            Box(room.CenterX + room.Width / 2 - WallThickness / 2,
                WallHeight / 2, room.CenterZ, WallThickness, WallHeight,
                room.Depth, tint, "wall");

            for (int end = -1; end <= 1; end += 2)
            {
                string? neighbour = end < 0
                    ? definition.Connections.FirstOrDefault(c => c.To == room.Id)?.From
                    : definition.Connections.FirstOrDefault(c => c.From == room.Id)?.To;
                var passage = neighbour is null ? null
                    : definition.Connections.First(c =>
                        end < 0 ? c.To == room.Id : c.From == room.Id);
                double edgeZ = room.CenterZ + end * room.Depth / 2;
                if (passage is null)
                {
                    Box(room.CenterX, WallHeight / 2, edgeZ, room.Width, WallHeight,
                        WallThickness, tint, "endwall");
                    continue;
                }
                double opening = passage.OpeningWidth;
                double remaining = (room.Width - opening) / 2;
                Box(room.CenterX - (opening + remaining) / 2, WallHeight / 2,
                    edgeZ, remaining, WallHeight, WallThickness, tint, "portal-wall");
                Box(room.CenterX + (opening + remaining) / 2, WallHeight / 2,
                    edgeZ, remaining, WallHeight, WallThickness, tint, "portal-wall");
                Box(room.CenterX, (DoorHeight + WallHeight) / 2, edgeZ,
                    opening, WallHeight - DoorHeight, WallThickness, trimTint, "lintel");
            }

            // Original, readable visual accent above each entrance.
            Box(room.CenterX + room.Width / 2 - WallThickness / 2,
                WallHeight - 0.13, room.CenterZ, WallThickness, 0.24,
                Math.Min(4, room.Depth - 1), trimTint, "guide");
        }

        double minX = definition.Rooms.Min(r => r.CenterX - r.Width / 2) - 3;
        double maxX = definition.Rooms.Max(r => r.CenterX + r.Width / 2) + 3;
        double minZ = definition.Rooms.Min(r => r.CenterZ - r.Depth / 2) - 3;
        double maxZ = definition.Rooms.Max(r => r.CenterZ + r.Depth / 2) + 3;

        return new RuntimeWorld(
            Game: "fusion",
            BuildId: "obp-custom-fusion-lab-v1",
            LevelId: 0,
            PlanetName: "Fusion Lab",
            LocationName: "Test Facility",
            Meshes: meshes,
            Textures: Array.Empty<RuntimeTexture>(),
            MaterialCount: boxIndex,
            CollisionMeshes: collision,
            Bounds: new ObpBounds(new Vec3(minX, -5, minZ), new Vec3(maxX, WallHeight + 5, maxZ)),
            Environment: new RuntimeEnvironment(
                DeathHeight: -25f, IsSphericalWorld: false,
                BackgroundColour: (0.05, 0.08, 0.15), FogColour: (0.09, 0.12, 0.2),
                FogNearDistance: 55f, FogFarDistance: 140f,
                AmbientColour: (0.55, 0.60, 0.73),
                BackgroundSource: RuntimeAtmosphereSource.PresentationFallback,
                FogSource: RuntimeAtmosphereSource.PresentationFallback),
            Ship: null,
            PlayerStart: new RuntimeSpawn(
                definition.Spawn.X, definition.Spawn.Y, definition.Spawn.Z, Math.PI));
    }
}
