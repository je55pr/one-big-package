using OBP.Core;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.RAC2;

public static partial class Rac2WorldImport
{
    /// <summary>
    /// Recreate one source Maktar dispenser's native regular-key sequence as
    /// a diagnostic independent animated mesh. This does not affect ordinary
    /// arena import, script native gameplay, collision or spawn state.
    /// </summary>
    public static RuntimeWorld WithMaktarDispenserPreview(
        IRandomAccessReader source, RuntimeWorld world, int instanceIndex, bool animate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(world);
        if (world.Game != ObpSourceGame.Rac2 || world.LevelId != Rac2MaktarArenaPresentation.NativeLevel ||
            world.BuildId != Rac2Authority.Primary.BuildId)
            throw new InvalidDataException("Dispenser preview requires the R&C2 Maktar retail world.");
        if (instanceIndex is < 394 or > 399)
            throw new ArgumentOutOfRangeException(nameof(instanceIndex), "Maktar native dispenser indices are 394..399.");

        var iso = Iso9660Filesystem.Open(source);
        var wad = iso.OpenFile("/G/LEVEL2.WAD") ?? throw new InvalidDataException("Maktar retail level is missing.");
        var header = Rac2LevelWad.ReadHeader(wad);
        var instances = Rac2Instances.Read(Rac2LevelWad.RequireLump(wad, header, 2));
        var inst = instances.MobyInstances.Single(m => m.Index == instanceIndex);
        if (inst.OClass != Rac2MaktarArenaPresentation.DispenserClass)
            throw new InvalidDataException("The requested native Maktar dispenser has changed class.");

        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var cls = Rac2MobyClasses.Read(core, 2)[inst.OClass];
        var model = cls.Mesh;
        var sequence = cls.Sequences.Single(s => s.Index == 0);
        if (cls.Joints.Count != 5 || model.Indices.Length / 3 != 1188 ||
            sequence.Frames.Count != 10 || sequence.Frames.Any(f => !f.ExtraChannelsResolved) ||
            !Ps2MobyNativePose.HasRigidJointBinding(model, cls.Joints.Count))
            throw new InvalidDataException("Maktar dispenser lacks a validated retail rigid-joint sequence.");

        var bytes = Rac2MobyClasses.ClassBytes(core, cls.OClass);
        var native = Rac2Instances.MatrixFromPosRotScale(
            (inst.Position.X, inst.Position.Y, inst.Position.Z),
            (inst.Rotation.X, inst.Rotation.Y, inst.Rotation.Z), inst.Scale == 0 ? 1 : inst.Scale);
        var frames = new List<double[]>();
        foreach (var key in animate ? sequence.Frames : sequence.Frames.Take(1))
        {
            var positions = Ps2MobyNativePose.Pose(bytes, model, key);
            var transformed = new double[positions.Length];
            for (int i = 0; i < positions.Length; i += 3)
            {
                var (x, y, z) = Rac2Instances.TransformPoint(native,
                    positions[i], positions[i + 1], positions[i + 2]);
                transformed[i] = R2(x);
                transformed[i + 1] = R2(z);
                transformed[i + 2] = R2(y);
            }
            frames.Add(transformed);
        }

        // The authored instance's ambient alone is very dim (~.1). Its
        // assigned retail directional light (index 6) illuminates the native
        // housing: use exactly the same static per-vertex lighting convention
        // as PlaceMobyInstances. The preview colours are frozen at the first
        // key, not claimed to be live PS2 dynamic lighting.
        var firstKey = sequence.Frames[0];
        var firstNative = Ps2MobyNativePose.Pose(bytes, model, firstKey);
        var normals = Ps2MobyNativePose.PoseNormals(bytes, model, firstKey, firstNative);
        var light = (uint)inst.LightIndex < (uint)instances.DirLights.Count
            ? instances.DirLights[inst.LightIndex]
            : null;
        var da = light is null ? (X: 0d, Y: 0d, Z: 0d) : YUp(light.DirectionA);
        var db = light is null ? (X: 0d, Y: 0d, Z: 0d) : YUp(light.DirectionB);
        var colours = new float[model.Positions.Length / 3 * 4];
        for (int vertex = 0; vertex < model.Positions.Length / 3; vertex++)
        {
            int v = vertex * 3, i = vertex * 4;
            var (nx, ny, nz) = WorldNormal(native, normals[v], normals[v + 1], normals[v + 2]);
            double ka = System.Math.Max(0, -(nx * da.X + ny * da.Y + nz * da.Z));
            double kb = System.Math.Max(0, -(nx * db.X + ny * db.Y + nz * db.Z));
            colours[i] = (float)System.Math.Clamp(inst.LightColour.R +
                (light is null ? 0 : light.ColourA.X * ka + light.ColourB.X * kb), .03, 1);
            colours[i + 1] = (float)System.Math.Clamp(inst.LightColour.G +
                (light is null ? 0 : light.ColourA.Y * ka + light.ColourB.Y * kb), .03, 1);
            colours[i + 2] = (float)System.Math.Clamp(inst.LightColour.B +
                (light is null ? 0 : light.ColourA.Z * ka + light.ColourB.Z * kb), .03, 1);
            colours[i + 3] = 1;
        }

        var groups = new Dictionary<(int Texture, RuntimeMaterialPresentation? Presentation), List<int>>();
        for (int face = 0; face < cls.TriangleTextureIds.Length; face++)
        {
            var surface = (cls.TriangleTextureIds[face],
                PresentationFor(model.Materials, model.TriangleMaterialStateIndices, null, face, classifyMobySurface: true));
            if (!groups.TryGetValue(surface, out var indices))
                groups[surface] = indices = [];
            indices.Add(model.Indices[face * 3]);
            indices.Add(model.Indices[face * 3 + 1]);
            indices.Add(model.Indices[face * 3 + 2]);
        }

        var animated = new List<RuntimeAnimatedMesh>(world.AnimatedMeshes ?? []);
        foreach (var (surface, indices) in groups)
            animated.Add(new RuntimeAnimatedMesh(
                $"maktar-dispenser-{instanceIndex}-t{surface.Texture}", "moby", surface.Texture,
                model.Uvs, indices.ToArray(), colours, frames,
                animate ? (float)System.Math.Clamp(sequence.Frames[0].Speed * 60, 6, 60) : 1,
                MaterialPresentation: surface.Presentation, VertexColoursAreAuthored: false));
        return world with { AnimatedMeshes = animated };
    }
}
