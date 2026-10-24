using System.Numerics;
using OBP.RAC1.Animation;
using OBP.RAC1.Level;

namespace OBP.RAC1.Presentation;

/// <summary>
/// Source-owned model/animation geometry for the 14 native class-0x472 menu
/// panel Mobys. The original class is level-local (Veldin core), not a global
/// Moby. These are real mesh vertices and sequence frames, never shipped art.
/// </summary>
public sealed class Rac1MenuPanelModel
{
    public const int NativeClassId = 0x472;
    public sealed record Clip(int NativeSequence, IReadOnlyList<double[]> Frames, float Rate,
        IReadOnlyList<Vector3[]> AttachmentFrameCorners);
    public IReadOnlyList<Vector3> RestPositions { get; }
    public IReadOnlyList<int> TriangleIndices { get; }
    public IReadOnlyList<Vector2> Uvs { get; }
    public IReadOnlyList<int> TriangleTextureIds { get; }
    public IReadOnlyDictionary<int, Clip> Sequences { get; }
    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public int JointCount { get; }
    public IReadOnlyList<int> AttachmentFrameJoints { get; }

    private Rac1MenuPanelModel(Rac1StaticClasses.MobyClass native, ReadOnlySpan<byte> payload)
    {
        JointCount = native.JointCount;
        // FUN_0023a318 transforms the executable's D_00160ED0 table
        // {0,1,2,3} through the class attachment chains, then
        // FUN_002196b8 projects resulting frame points 0 and 3.
        var attachmentJoints = new int[4];
        for (int semantic = 0; semantic < attachmentJoints.Length; semantic++)
            attachmentJoints[semantic] = Rac1MobyAttachment.ReadJoint(payload, semantic);
        AttachmentFrameJoints = attachmentJoints;
        if (AttachmentFrameJoints.Distinct().Count() != 4)
            throw new InvalidDataException("R&C1 panel frame attachment joints are not distinct.");
        var mesh = native.Mesh;
        RestPositions = Enumerable.Range(0, mesh.Positions.Length / 3)
            .Select(i => new Vector3((float)mesh.Positions[3 * i],
                (float)mesh.Positions[3 * i + 1], (float)mesh.Positions[3 * i + 2]))
            .ToArray();
        TriangleIndices = mesh.Indices.ToArray();
        Uvs = Enumerable.Range(0, mesh.Uvs.Length / 2)
            .Select(i => new Vector2(mesh.Uvs[i * 2], mesh.Uvs[i * 2 + 1]))
            .ToArray();
        TriangleTextureIds = native.TriangleTextureIds.ToArray();
        Min = new Vector3(RestPositions.Min(p => p.X), RestPositions.Min(p => p.Y), RestPositions.Min(p => p.Z));
        Max = new Vector3(RestPositions.Max(p => p.X), RestPositions.Max(p => p.Y), RestPositions.Max(p => p.Z));
        Sequences = native.Sequences.Where(s => s.Value is not null).ToDictionary(
            s => s.Index, s =>
            {
                var sequence = s.Value!;
                // Keep native frame boundaries and IDs intact. A view adapter
                // may sample them once we recover each MenuPage.moby_anims slot.
                double[][] poses = sequence.Frames
                    .Select(f => Rac1MobyPose.PoseLocalHierarchy(mesh, native.Joints, f))
                    .ToArray();
                Vector3[][] corners = sequence.Frames.Select(frame =>
                {
                    var joints = Rac1MobyPose.PoseLocalJoints(mesh, native.Joints, frame);
                    return AttachmentFrameJoints.Select(joint =>
                    {
                        var pos = joints[joint].Position;
                        return new Vector3((float)pos[0], (float)pos[1], (float)pos[2]);
                    }).ToArray();
                }).ToArray();
                return new Clip(s.Index, poses, sequence.ConstantTransitionRate, corners);
            });
    }

    public static Rac1MenuPanelModel Read(Rac1LevelCore.Core level0)
    {
        var native = Rac1StaticClasses.Read(level0).Mobies.GetValueOrDefault(NativeClassId) ??
            throw new InvalidDataException("R&C1 Veldin core lacks native menu-panel class 0x472.");
        if (native.JointCount != 5 || native.Mesh.Positions.Length != 48 * 3 ||
            native.Mesh.Indices.Length != 32 * 3 || native.Sequences.Count != 216)
            throw new InvalidDataException("R&C1 0x472 model differs from the supported retail baseline.");
        return new Rac1MenuPanelModel(native,
            level0.Assets.AsSpan(native.AssetOffset, native.AssetLength));
    }
}
