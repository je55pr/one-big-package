using OBP.Core;
using System.Buffers.Binary;
using OBP.Core.Math;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

/// <summary>
/// Synthetic Kerwan taxi world shared by the Metropolis taxi tests. Synthetic data only; no retail content.
/// </summary>
internal static class Rac1MetropolisTaxiFixtures
{
    internal static readonly (int Index, int Class, double X, double Y, double Z, int OutboundPoints)[] Placements =
    [
        (919, 816, 179.77748, 50, 244.59235, 150),
        (920, 816, 264.25, 34, 93.75, 150),
        (921, 816, 264, 56, 85, 0),
        (961, 1012, 280.74213, 48.165154, 149.90324, 75),
        (962, 1012, 207.35722, 61.993126, 133.20839, 150),
    ];

    // Fixture includes the source-game payload contract without an ISO.
    internal static byte[] TinyNativePath(double x, double y, double z, bool returning = false)
    {
        var result = new byte[4 + 3 * 12];
        BinaryPrimitives.WriteInt32LittleEndian(result, 3);
        for (int i = 0; i < 3; i++)
        {
            int at = 4 + 12 * i;
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(at, 4), (float)(x + (returning ? 2 - i : i) * 5));
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(at + 4, 4), (float)z);
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(at + 8, 4), (float)y);
        }
        return result;
    }

    internal static RuntimeWorld Kerwan(bool wrongClass = false, bool missingPath = false) => new(
        Game: ObpSourceGame.Rac1,
        BuildId: "rac1-ntscu-original",
        LevelId: 3,
        PlanetName: "Kerwan",
        LocationName: null,
        Meshes: [],
        Textures: [],
        MaterialCount: 0,
        CollisionMeshes: [],
        Bounds: new ObpBounds(new Vec3(0, 0, 0), new Vec3(500, 500, 500)),
        Environment: null,
        Ship: new RuntimeSpawn(248.46194, 55.5, 98.921234, 0),
        DynamicObjects: Placements.Select(p => new RuntimeDynamicObject(
            ObpSourceGame.Rac1, wrongClass && p.Index == 961 ? 999 : p.Class, p.Index, null,
            $"moby_{p.Class}", $"moby_i{p.Index}",
            new RuntimeObjectTransform(
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                p.X, p.Y, p.Z, 1,
            ]),
            [],
            NativePayloads: p.OutboundPoints == 0 || (missingPath && p.Index == 919)
                ? []
                : p.Class == 1012
                    ? [
                        new RuntimeOpaquePayload(Rac1MetropolisTaxiSession.OutboundPathFormat,
                            TinyNativePath(p.X, p.Y, p.Z)),
                        new RuntimeOpaquePayload(Rac1MetropolisTaxiSession.ReturnPathFormat,
                            TinyNativePath(p.X, p.Y, p.Z, returning: true)),
                    ]
                    : [new RuntimeOpaquePayload(
                        Rac1MetropolisTaxiSession.OutboundPathFormat,
                        TinyNativePath(p.X, p.Y, p.Z))])).ToArray());

    /// <summary>
    /// Kerwan with the class-816 taxi at instance 919 given a native approach path, departure path and
    /// trigger cuboid, so it enters the traffic cycle. <paramref name="leadingPayloads"/> (for example a
    /// PVar) are attached ahead of the three path/trigger payloads.
    /// </summary>
    internal static RuntimeWorld KerwanWithApproachTraffic919(params RuntimeOpaquePayload[] leadingPayloads)
    {
        var world = Kerwan();
        var objects = world.DynamicObjects!.ToArray();
        int slot = Array.FindIndex(objects, o => o.InstanceIndex == 919);
        var stop = Placements.Single(p => p.Index == 919);
        var volume = new byte[24];
        float[] fields = [(float)stop.X - 12, (float)stop.Z, (float)stop.Y, 4f, 4f, 4f];
        for (int i = 0; i < fields.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(volume.AsSpan(i * 4, 4), fields[i]);
        objects[slot] = objects[slot] with
        {
            NativePayloads = objects[slot].NativePayloads!.Concat(leadingPayloads).Concat([
                new RuntimeOpaquePayload(Rac1MetropolisTaxiSession.ApproachPathFormat,
                    TinyNativePath(stop.X - 10, stop.Y, stop.Z)),
                new RuntimeOpaquePayload(Rac1MetropolisTaxiSession.DeparturePathFormat,
                    TinyNativePath(stop.X + 10, stop.Y, stop.Z)),
                new RuntimeOpaquePayload(Rac1MetropolisTaxiSession.TriggerVolumeFormat, volume),
            ]).ToArray(),
        };
        return world with { DynamicObjects = objects };
    }
}
