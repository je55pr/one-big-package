using OBP.PS2.Geometry;
using OBP.RAC3.Level;

namespace OBP.RAC3;

/// <summary>
/// The Phoenix trophy-room figurines use the upright orientation from their
/// source reveal clip, but retain the authored resting translation. Their
/// reveal clip's translation lifts them off the display during the animation.
/// This is a bounded presentation fallback, not a synthetic gameplay pose.
/// </summary>
internal static class Rac3PhoenixTrophyPresentation
{
    internal const int TableIndex = 3;
    private const int FirstFigurineClass = 7501;
    private const int LastFigurineClass = 7509;

    internal static IReadOnlyDictionary<int, Rac3Assets.MobyVisualClass> Apply(
        IReadOnlyDictionary<int, Rac3Assets.MobyVisualClass> classes)
    {
        var presented = new Dictionary<int, Rac3Assets.MobyVisualClass>(classes);
        for (int id = FirstFigurineClass; id <= LastFigurineClass; id++)
        {
            if (!presented.TryGetValue(id, out var cls) ||
                cls.NativePoseSource is not { } source ||
                cls.Joints.Count != 1 ||
                !Ps2MobyNativePose.HasRigidJointBinding(cls.Mesh, cls.Joints.Count))
                continue;

            var rest = cls.Sequences.FirstOrDefault(sequence => sequence.Index == 0);
            var reveal = cls.Sequences.FirstOrDefault(sequence => sequence.Index == 1);
            if (rest is null || reveal is null || rest.Frames.Count < 1 || reveal.Frames.Count < 2 ||
                !rest.Frames[0].ExtraChannelsResolved || !reveal.Frames[1].ExtraChannelsResolved)
                continue;

            // Retail's reveal starts by pitching the miniature upright. Its
            // animated translation is not the shelf placement, so preserve the
            // resting key's translation instead of levitating the figurine.
            var upright = reveal.Frames[1] with
            {
                TranslationOverrides = rest.Frames[0].TranslationOverrides,
            };
            try
            {
                var positions = Ps2MobyNativePose.Pose(source, cls.Mesh, upright);
                var normals = Ps2MobyNativePose.PoseNormals(source, cls.Mesh, upright, positions);
                presented[id] = cls with
                {
                    Mesh = cls.Mesh with
                    {
                        Positions = positions,
                        Normals = normals,
                        NativeInitialPoseApplied = true,
                    },
                };
            }
            catch (InvalidDataException)
            {
                // Keep the validated source rest key if a class cannot supply
                // the complete upright frame. Never invent an orientation.
            }
        }
        return presented;
    }
}
