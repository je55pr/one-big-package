using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using OBP.Core;
using OBP.IO;
using OBP.PS2.Presentation;
using OBP.PS2.Textures;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>The two retail slot-3 mobys, decoded independently of the destination.</summary>
public sealed record Rac1ClankPackAssets(
    IReadOnlyDictionary<int, Rac1ClankPackAssets.Model> Models,
    IReadOnlyList<RuntimeTexture> Textures,
    IReadOnlyDictionary<int, IReadOnlyList<Rac1ClankPackAssets.Pair>> AnimationPairs,
    IReadOnlyList<int> DefaultBlends,
    Rac1EffectAssets Effects, IReadOnlyList<Vector3> JetVertices, IReadOnlyList<Vector2> JetUvs,
    IReadOnlyList<Vector3> JetQuads, IReadOnlyList<Rac1ClankPackAssets.BlinkLayer> BlinkLayers, IReadOnlyList<float> BlinkWeights)
{
    public const string SocketKey = "rac1:clank-back";
    public const int SemanticJoint = 5;
    public const int RatchetJoint = 5;
    public const int ClankClass = 601;
    public sealed record Pair(int RatchetSequence, int PackSequence, int ClankSequence);
    public sealed record BlinkLayer(int Joint, PlayerJointChannels Target);
    public sealed record Model(int NativeClass, PlayerSkeletalAsset Skeletal,
        IReadOnlyList<RuntimeObjectMesh> Surfaces, IReadOnlyDictionary<int, Rac1MobyAnimation.Sequence> Sequences,
        string SourceSha256);

    public static Rac1ClankPackAssets Load(string path)
    {
        using var disc = new FileRandomAccessReader(path);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(level => level.LevelId == 0);
        var core = Rac1LevelCore.Open(disc, level);
        var classes = Rac1StaticClasses.Read(core);
        var models = new Dictionary<int, Model>();
        foreach (var (id, expected) in new (int, string)[]
        {
            (601, "92077a25250584e210d3a477a80d1ca1958fa495d93d22dda930f1e3b9d421fa"),
            (607, "2c46e695f91166fbc1455db8bb2969a738ca3f0b9e46da0a1f618f0879d0c3da"),
            (608, "f73c7eba18df8ad28c18c5735f5604a532e1eab85306b9b545bbfd83c1091e01"),
            (609, "05fef95614ee0e1656b84dd6136f84483d405e170e283c9543873fa51c5d1679"),
        })
        {
            if (!classes.Mobies.TryGetValue(id, out var cls))
                throw new InvalidDataException($"R&C1 Clank class {id} is absent.");
            string hash = Convert.ToHexString(SHA256.HashData(core.Assets.AsSpan(cls.AssetOffset, cls.AssetLength))).ToLowerInvariant();
            if (hash != expected) throw new InvalidDataException($"R&C1 Clank class {id} differs from its retail authority.");
            models.Add(id, Decode(cls, core.Assets.AsSpan(cls.AssetOffset, cls.AssetLength), hash));
        }
        var ratchet = classes.Mobies[0];
        if (Rac1MobyAttachment.ReadJoint(core.Assets.AsSpan(ratchet.AssetOffset, ratchet.AssetLength), SemanticJoint) != RatchetJoint)
            throw new InvalidDataException("R&C1 Clank attachment differs from retail.");
        var ids = models.Values.SelectMany(model => model.Surfaces).Select(surface => surface.TextureId).ToHashSet();
        var textures = RcLevelTextureTable.Read(core.Index, core.Assets, core.GsRam, core.Header.TexturesBaseOffset,
                new(core.Header.MobyTextures.Count, core.Header.MobyTextures.Offset))
            .Where(texture => ids.Contains(texture.Index))
            .Select(texture => new RuntimeTexture("moby", texture.Index, texture.Width, texture.Height, texture.Rgba)).ToArray();
        if (ids.Except(textures.Select(texture => texture.TextureId)).Any())
            throw new InvalidDataException("R&C1 Clank texture is absent.");
        var overlay = Rac1ExecutableOverlay.Read(disc, level);
        if (overlay.Sha256 != "eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804")
            throw new InvalidDataException("R&C1 Clank controller tables differ from the retail authority.");
        var pairs = new Dictionary<int, IReadOnlyList<Pair>>();
        foreach (var (pack, address) in new[] { (2, 0x17bbf0), (3, 0x17bbd0), (4, 0x15f6b8) })
        {
            var entries = new List<Pair>();
            for (int i = 0; ; i++)
            {
                if (i > 20) throw new InvalidDataException("R&C1 Clank animation table is unterminated.");
                var row = overlay.Read(address + i * 4, 4);
                int sequence = BinaryPrimitives.ReadInt16LittleEndian(row);
                if (sequence == -1) break;
                if (!models[605 + pack].Sequences.ContainsKey(row[2]) || !models[ClankClass].Sequences.ContainsKey(row[3]))
                    throw new InvalidDataException("R&C1 Clank animation pair is absent.");
                entries.Add(new(sequence, row[2], row[3]));
            }
            pairs.Add(pack, entries);
        }
        var choices = new[] { 10, 11, 12, 21, 34 }.ToDictionary(type => type,
            type => Rac1ParticleAssets.TextureChoices(core, type, type == 12 ? 8 : 1));
        var effects = new Rac1EffectAssets(choices, Rac1ParticleAssets.ReadTextures(core,
            choices.Values.SelectMany(ids => ids).ToHashSet()).ToDictionary(texture => texture.TextureId),
            new Dictionary<int, RuntimeTexture>());
        var vertices = Enumerable.Range(0, 196).Select(i =>
        {
            var v = overlay.Read(0x1d9470 + i * 16, 16);
            return new Vector3(BinaryPrimitives.ReadSingleLittleEndian(v), BinaryPrimitives.ReadSingleLittleEndian(v[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(v[8..]));
        }).ToArray();
        var uvs = Enumerable.Range(0, 196).Select(i =>
        {
            var v = overlay.Read(0x1da0b0 + i * 8, 8);
            return new Vector2(BinaryPrimitives.ReadSingleLittleEndian(v), BinaryPrimitives.ReadSingleLittleEndian(v[4..]));
        }).ToArray();
        var quads = Enumerable.Range(0, 8).Select(i =>
        {
            var v = overlay.Read(0x1da9e0 + i * 16, 16);
            return new Vector3(BinaryPrimitives.ReadSingleLittleEndian(v), BinaryPrimitives.ReadSingleLittleEndian(v[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(v[8..]));
        }).ToArray();
        var blink = new List<BlinkLayer>();
        float clankScale = classes.Mobies[ClankClass].Mesh.Scale;
        for (int i = 0; i < 4; i++)
        {
            int socket = BinaryPrimitives.ReadInt32LittleEndian(overlay.Read(0x15f6f8 + i * 4, 4));
            var q = overlay.Read(0x17c1e0 + i * 16, 16);
            var s = overlay.Read(0x17c220 + i * 16, 16);
            var t = overlay.Read(0x17c260 + i * 16, 16);
            blink.Add(new(models[ClankClass].Skeletal.Skeleton.Attachments[socket].Joint, new(
                new(BinaryPrimitives.ReadSingleLittleEndian(q), BinaryPrimitives.ReadSingleLittleEndian(q[4..]),
                    BinaryPrimitives.ReadSingleLittleEndian(q[8..]), BinaryPrimitives.ReadSingleLittleEndian(q[12..])),
                new Vector3(BinaryPrimitives.ReadSingleLittleEndian(s), BinaryPrimitives.ReadSingleLittleEndian(s[4..]), BinaryPrimitives.ReadSingleLittleEndian(s[8..])),
                new Vector3(BinaryPrimitives.ReadSingleLittleEndian(t), BinaryPrimitives.ReadSingleLittleEndian(t[4..]), BinaryPrimitives.ReadSingleLittleEndian(t[8..])) * (clankScale * clankScale / 1024))));
        }
        var weights = Enumerable.Range(0, 22).Select(i => BinaryPrimitives.ReadSingleLittleEndian(overlay.Read(0x17c2a0 + i * 4, 4))).ToArray();
        return new(models, textures, pairs, Enumerable.Range(0, 2)
            .Select(i => BinaryPrimitives.ReadInt32LittleEndian(overlay.Read(0x15f6e0 + i * 4, 4))).ToArray(), effects, vertices, uvs, quads, blink, weights);
    }

    private static Model Decode(Rac1StaticClasses.MobyClass cls, ReadOnlySpan<byte> payload, string hash)
    {
        // Ordinary Moby skins retain their stored inverse-bind translation.
        // Ratchet's separate common-pivot reconstruction does not apply here.
        float scale = cls.Mesh.Scale / 1024;
        var joints = new PlayerSkeletonJoint[cls.Joints.Count];
        for (int j = 0; j < joints.Length; j++)
        {
            var joint = cls.Joints[j]; var a = joint.NativeAffine;
            var inverse = new Matrix4x4((float)a[0], (float)a[1], (float)a[2], 0,
                (float)a[4], (float)a[5], (float)a[6], 0, (float)a[8], (float)a[9], (float)a[10], 0, 0, 0, 0, 1);
            int parent = j == 0 ? -1 : joint.ParentRecordIndex;
            var local = new Vector3(joint.CommonX, joint.CommonY, joint.CommonZ) * scale;
            inverse.Translation = new Vector3((float)a[12], (float)a[13], (float)a[14]) * scale;
            joints[j] = new(parent, local, inverse);
        }
        var sequences = cls.Sequences.Where(slot => slot.Value is not null).ToDictionary(slot => slot.Index, slot => slot.Value!);
        var clips = sequences.Values.Select(sequence => new PlayerSkeletalClip(ObpSourceGame.Rac1,
            Rac1Authority.Primary.BuildId, cls.OClass, sequence.Index, Enumerable.Range(0, joints.Length).ToArray(),
            sequence.Frames.Select(frame =>
            {
                var channels = joints.Select((joint, j) =>
                {
                    var q = frame.JointRotations[j];
                    return new PlayerJointChannels(new(q.Xf, q.Yf, q.Zf, q.Wf), Vector3.One, joint.Translation);
                }).ToArray();
                foreach (ulong word in frame.Thing1)
                {
                    int j = (int)(word >> 48) & 0x7fff;
                    channels[j] = channels[j] with { Scale = new Vector3((short)word, (short)(word >> 16), (short)(word >> 32)) / 4096 };
                }
                foreach (ulong word in frame.Thing2)
                    channels[(int)(word >> 48)] = channels[(int)(word >> 48)] with
                    { Translation = new Vector3((short)word, (short)(word >> 16), (short)(word >> 32)) * scale };
                float rate = sequence.ConstantTransitionRate > 0 ? sequence.ConstantTransitionRate : frame.TransitionRate;
                if (rate <= 0 && sequence.Frames.Count != 1) throw new InvalidDataException("Invalid Clank timing.");
                return new PlayerSkeletalFrame(channels, rate > 0 ? 1d / (60d * rate) : 1d / 60, frame.TimestampUnits / 16d);
            }).ToArray()));
        var chains = new Dictionary<int, PlayerSkeletonAttachment>();
        int sockets = cls.OClass switch { ClankClass => 7, 608 => 4, 609 => 2, _ => 0 };
        for (int socket = 0; socket < sockets; socket++)
            chains.Add(socket, new(Rac1MobyAttachment.ReadJoint(payload, socket), []));
        var asset = new PlayerSkeletalAsset(new(ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId, "clank-pack", $"rac1:moby:{cls.OClass}"),
            new(joints, chains), new(cls.Mesh.Positions, cls.Mesh.VertexJoints, cls.Mesh.VertexWeights), clips.ToDictionary(clip => clip.SequenceId));
        var surfaces = Enumerable.Range(0, cls.TriangleTextureIds.Length).GroupBy(face =>
        {
            int state = cls.Mesh.TriangleMaterialStateIndices[face];
            var material = (uint)state < cls.Mesh.Materials.Count ? Ps2MaterialPresentation.From(cls.Mesh.Materials[state], null, classifyMobySurface: true) : null;
            return (Texture: cls.TriangleTextureIds[face], Material: material);
        }).Select(group => new RuntimeObjectMesh("moby", group.Key.Texture, cls.Mesh.Positions, cls.Mesh.Uvs,
            group.SelectMany(face => cls.Mesh.Indices.AsSpan(face * 3, 3).ToArray()).ToArray(), MaterialPresentation: group.Key.Material)).ToArray();
        return new(cls.OClass, asset, surfaces, sequences, hash);
    }
}
