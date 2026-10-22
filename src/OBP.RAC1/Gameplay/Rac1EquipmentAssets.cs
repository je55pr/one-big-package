using System.Numerics;
using System.Security.Cryptography;
using OBP.IO;
using OBP.PS2.Presentation;
using OBP.PS2.Textures;
using OBP.RAC1.Animation;
using OBP.RAC1.Geometry;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>Retail equipment/child skins, kept in native Z-up local coordinates.</summary>
public static class Rac1EquipmentAssets
{
    public sealed record Clip(int Sequence, double[][] Frames, double[] Durations, Vector3[] Origins,
        float[] Positions, int? DirectSound, IReadOnlyList<Rac1MobyAnimation.TimedSoundCue> Sounds,
        IReadOnlyDictionary<int, Vector3[]>? SocketOrigins = null);
    public sealed record Model(int NativeClass, IReadOnlyList<RuntimeObjectMesh> Surfaces,
        IReadOnlyDictionary<int, Clip> Clips, int VertexCount, int TriangleCount, string SourceSha256);
    public sealed record Catalogue(IReadOnlyDictionary<int, Model> Models, IReadOnlyList<RuntimeTexture> Textures,
        IReadOnlyDictionary<int, RuntimeTexture> ParticleTextures)
    {
        public Rac1EffectAssets? Effects { get; init; }
    }

    public static Catalogue Load(string path)
    {
        using var reader = new FileRandomAccessReader(path);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == 0);
        return Decode(Rac1LevelCore.Open(reader, level));
    }

    internal static Catalogue Decode(Rac1LevelCore.Core core)
    {
        var local = Rac1StaticClasses.Read(core);
        var models = new Dictionary<int, Model>();
        foreach (var profile in Rac1WeaponProfiles.Additional)
        {
            var source = Rac1GlobalMoby.Read(core, profile.ModelClass);
            string hash = Convert.ToHexString(SHA256.HashData(source.Compressed)).ToLowerInvariant();
            if (hash != profile.CompressedSha256) throw new InvalidDataException($"Retail {profile.Name} asset differs from its authority.");
            var mesh = Rac1Moby.ReadClass(source.Payload);
            models.Add(profile.ModelClass, Bake(profile.ModelClass, mesh, Rac1Moby.ReadSkeleton(source.Payload),
                Rac1MobyAnimation.ReadSequences(source.Payload), face =>
                {
                    int slot = mesh.TriangleMaterialSlots[face];
                    if (slot == -1) return -1;
                    if ((uint)slot >= source.TextureMap.Length || source.TextureMap[slot] == 255)
                        throw new InvalidDataException("Equipment references an unmapped texture slot.");
                    return source.TextureMap[slot];
                },
                Rac1MobyAttachment.ReadJoint(source.Payload, 0), hash,
                profile.Item == Rac1WeaponId.Ryno ? Enumerable.Range(0, Rac1ArsenalRules.RynoBarrels)
                    .Select(socket => Rac1MobyAttachment.ReadJoint(source.Payload, socket)).ToArray() : null));
        }
        foreach (int id in new[] { 74, 186, 230, 153, 172, 203, 270, 479, 457 })
        {
            var cls = local.Mobies[id];
            string hash = Convert.ToHexString(SHA256.HashData(core.Assets.AsSpan(cls.AssetOffset, cls.AssetLength))).ToLowerInvariant();
            models.Add(id, Bake(id, cls.Mesh, cls.Joints, cls.Sequences, face => cls.TriangleTextureIds[face], null, hash));
        }
        var ids = models.Values.SelectMany(model => model.Surfaces).Select(surface => surface.TextureId).Where(id => id >= 0).ToHashSet();
        var textures = RcLevelTextureTable.Read(core.Index, core.Assets, core.GsRam, core.Header.TexturesBaseOffset,
                new(core.Header.MobyTextures.Count, core.Header.MobyTextures.Offset))
            .Where(texture => ids.Contains(texture.Index))
            .Select(texture => new RuntimeTexture("moby", texture.Index, texture.Width, texture.Height, texture.Rgba)).ToArray();
        if (ids.Except(textures.Select(texture => texture.TextureId)).Any())
            throw new InvalidDataException("An equipment material references a missing texture.");
        var effects = Rac1EffectAssets.Decode(core);
        return new(models, textures, effects.ParticleChoices.ToDictionary(pair => pair.Key, pair => effects.PartTextures[pair.Value[0]]))
        { Effects = effects };
    }

    private static Model Bake(int id, Rac1Moby.Mesh mesh, IReadOnlyList<Rac1Moby.SkeletonJoint> joints,
        IReadOnlyList<Rac1MobyAnimation.SequenceSlot> sequences, Func<int, int> texture, int? originJoint, string hash,
        int[]? socketJoints = null)
    {
        var surfaces = Enumerable.Range(0, mesh.Indices.Length / 3).GroupBy(face =>
        {
            int state = mesh.TriangleMaterialStateIndices[face];
            var material = (uint)state < mesh.Materials.Count
                ? Ps2MaterialPresentation.From(mesh.Materials[state], null, classifyMobySurface: true) : null;
            return (Texture: texture(face), Material: material);
        }).Select(group => new RuntimeObjectMesh("moby", group.Key.Texture, mesh.Positions, mesh.Uvs,
            group.SelectMany(face => mesh.Indices.AsSpan(face * 3, 3).ToArray()).ToArray(),
            MaterialPresentation: group.Key.Material)).ToArray();
        var clips = new Dictionary<int, Clip>();
        foreach (var slot in sequences.Where(slot => slot.Value is not null))
        {
            var sequence = slot.Value!;
            var frames = sequence.Frames.Select(frame => joints.Count == 0 ? mesh.Positions :
                Rac1MobyPose.PoseLocalHierarchy(mesh, joints, frame)).ToArray();
            var durations = sequence.Frames.Select(frame => sequence.Frames.Count == 1 ? 1d / 60 :
                1d / (60 * (sequence.ConstantTransitionRate > 0 ? sequence.ConstantTransitionRate : frame.TransitionRate))).ToArray();
            if (durations.Any(duration => !double.IsFinite(duration) || duration <= 0))
                throw new InvalidDataException("Equipment sequence has an invalid transition rate.");
            var origins = sequence.Frames.Select(frame =>
            {
                if (originJoint is not { } joint) return Vector3.Zero;
                var pose = Rac1MobyPose.PoseLocalJoints(mesh, joints, frame)[joint];
                return new Vector3((float)pose.Position[0], (float)pose.Position[1], (float)pose.Position[2]);
            }).ToArray();
            var socketOrigins = socketJoints?.Select((joint, socket) => (joint, socket)).ToDictionary(pair => pair.socket,
                pair => sequence.Frames.Select(frame =>
                {
                    var pose = Rac1MobyPose.PoseLocalJoints(mesh, joints, frame)[pair.joint];
                    return new Vector3((float)pose.Position[0], (float)pose.Position[1], (float)pose.Position[2]);
                }).ToArray());
            clips.Add(slot.Index, new(slot.Index, frames, durations, origins,
                sequence.Frames.Select(frame => frame.TimestampUnits / 16f).ToArray(),
                sequence.HasDirectSound ? sequence.SoundId : null, sequence.TimedSoundCues, socketOrigins));
        }
        return new(id, surfaces, clips, mesh.Positions.Length / 3, mesh.Indices.Length / 3, hash);
    }
}
