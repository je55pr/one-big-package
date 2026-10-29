using System.Numerics;
using System.Security.Cryptography;
using OBP.IO;
using OBP.PS2.Presentation;
using OBP.PS2.Textures;
using OBP.RAC1.Animation;
using OBP.RAC1.Geometry;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.RAC1.Gameplay;

/// <summary>Canonical LEVEL0 global class192, with its own level-local texture map and skeleton.</summary>
public static class Rac1BombGloveAssets
{
    public const string CompressedSha256 = "cddebd40b4c00a06edbe306c671f2d2f10e474e0e1b1455f325afa2a57a02d4f";
    public sealed record Clip(int Sequence, double[][] Frames, double[] Durations, Vector3[] BombOrigins);
    public sealed record Asset(IReadOnlyList<RuntimeObjectMesh> Surfaces, IReadOnlyList<RuntimeTexture> Textures,
        IReadOnlyDictionary<int, Clip> Clips, int BombOriginJoint, int VertexCount, int TriangleCount,
        IReadOnlyDictionary<int, RuntimeTexture> ParticleTexturesByPoolType);

    public static Asset Load(string path)
    {
        using var reader = new FileRandomAccessReader(path);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == 0);
        return Decode(Rac1LevelCore.Open(reader, level));
    }

    internal static Asset Decode(Rac1LevelCore.Core core)
    {
        var source = Rac1GlobalMoby.Read(core, Rac1BombGlove.NativeWeaponClassId);
        if (source.Compressed.Length != 19180 ||
            !SHA256.HashData(source.Compressed).AsSpan().SequenceEqual(Convert.FromHexString(CompressedSha256)))
            throw new InvalidDataException("Bomb Glove global class differs from the pinned retail authority.");
        var mesh = Rac1Moby.ReadClass(source.Payload);
        var joints = Rac1Moby.ReadSkeleton(source.Payload);
        var sequences = Rac1MobyAnimation.ReadSequences(source.Payload);
        if (mesh.Positions.Length != 1140 * 3 || mesh.Indices.Length != 1200 * 3 || joints.Count != 17 || sequences.Count(slot => slot.Value is not null) != 3)
            throw new InvalidDataException("Bomb Glove geometry/animation contract changed.");
        int originJoint = Rac1MobyAttachment.ReadJoint(source.Payload, 0);
        var clips = new Dictionary<int, Clip>();
        foreach (var slot in sequences.Where(slot => slot.Value is not null))
        {
            var sequence = slot.Value ?? throw new InvalidDataException("Bomb Glove sequence is absent.");
            double[][] frames = sequence.Frames.Select(frame => Rac1MobyPose.PoseLocalHierarchy(mesh, joints, frame)).ToArray();
            var origins = sequence.Frames.Select(frame =>
            {
                var joint = Rac1MobyPose.PoseLocalJoints(mesh, joints, frame)[originJoint];
                // 0x2c2710..2c27c8: semantic0 origin + its matrix times (0,-.09,-.02).
                return new Vector3((float)(joint.Position[0] - .09f * joint.NativeLinear[1, 0] - .02f * joint.NativeLinear[2, 0]),
                    (float)(joint.Position[1] - .09f * joint.NativeLinear[1, 1] - .02f * joint.NativeLinear[2, 1]),
                    (float)(joint.Position[2] - .09f * joint.NativeLinear[1, 2] - .02f * joint.NativeLinear[2, 2]));
            }).ToArray();
            var durations = sequence.Frames.Select(frame => frames.Length == 1 ? 1d / 60 :
                1d / (60 * (sequence.ConstantTransitionRate > 0 ? sequence.ConstantTransitionRate : frame.TransitionRate))).ToArray();
            if (durations.Any(duration => !double.IsFinite(duration) || duration <= 0))
                throw new InvalidDataException("Invalid Bomb Glove animation rate.");
            clips.Add(slot.Index, new(slot.Index, frames, durations, origins));
        }
        var groups = Enumerable.Range(0, mesh.Indices.Length / 3).GroupBy(face =>
        {
            int slot = mesh.TriangleMaterialSlots[face];
            if ((uint)slot >= source.TextureMap.Length || source.TextureMap[slot] == 255)
                throw new InvalidDataException("Bomb Glove surface has an unmapped texture.");
            int state = mesh.TriangleMaterialStateIndices[face];
            RuntimeMaterialPresentation? presentation = (uint)state < mesh.Materials.Count
                ? Ps2MaterialPresentation.From(mesh.Materials[state], null, classifyMobySurface: true) : null;
            return (Texture: (int)source.TextureMap[slot], Presentation: presentation);
        });
        // Equipment vertices stay native Z-up; the player-view adapter owns its local-axis conversion.
        var surfaces = groups.Select(group => new RuntimeObjectMesh("moby", group.Key.Texture, mesh.Positions, mesh.Uvs,
            group.SelectMany(face => mesh.Indices.AsSpan(face * 3, 3).ToArray()).ToArray(),
            MaterialPresentation: group.Key.Presentation)).ToArray();
        var ids = surfaces.Select(surface => surface.TextureId).ToHashSet();
        var textures = RcLevelTextureTable.Read(core.Index, core.Assets, core.GsRam, core.Header.TexturesBaseOffset,
                new(core.Header.MobyTextures.Count, core.Header.MobyTextures.Offset))
            .Where(texture => ids.Contains(texture.Index))
            .Select(texture => new RuntimeTexture("moby", texture.Index, texture.Width, texture.Height, texture.Rgba)).ToArray();
        if (textures.Length != ids.Count) throw new InvalidDataException("Bomb Glove texture is absent.");
        int[] types = [4, 34]; // Class122 trail and class79's 150 type34 sparks.
        var textureByType = types.ToDictionary(type => type, type => Rac1ParticleAssets.InitialTexture(core, type));
        var particleTextures = Rac1ParticleAssets.ReadTextures(core, textureByType.Values.ToHashSet()).ToDictionary(texture => texture.TextureId);
        return new(surfaces, textures, clips, originJoint, 1140, 1200,
            textureByType.ToDictionary(pair => pair.Key, pair => particleTextures[pair.Value]));
    }
}
