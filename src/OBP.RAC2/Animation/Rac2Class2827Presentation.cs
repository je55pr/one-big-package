using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;
using OBP.Runtime;

namespace OBP.RAC2.Animation;

/// <summary>Original MSR I clips selected by the witnessed opening chase/attack/recovery slice.</summary>
public static class Rac2Class2827Presentation
{
    public sealed record Asset(IReadOnlyDictionary<int, RuntimeObjectAnimationClip> Clips)
    {
        public RuntimeObjectAnimationSet Presentation => new(Clips.Values.ToArray());
    }

    public static Asset Load(string path, IReadOnlyList<RuntimeObjectMesh> surfaces) =>
        Load(path, surfaces, false);

    public static Asset Load(string path, IReadOnlyList<RuntimeObjectMesh> surfaces,
        bool nativePosePreview)
    {
        using var reader = new FileRandomAccessReader(path);
        var wad = Iso9660Filesystem.Open(reader).OpenFile("/G/LEVEL0.WAD") ?? throw new InvalidDataException("Aranos actor bank is missing.");
        var header = Rac2LevelWad.ReadHeader(wad);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        // The actor's model comes from the same LEVEL0 importer. When the
        // generic GC native-pose preview is enabled, its recovered VU1 cache
        // adds vertices. Animation keys must use that identical source stream,
        // otherwise the actor factory rejects the mismatched model entirely.
        var cls = Rac2MobyClasses.Read(core, level: 0,
            nativePoseOverride: nativePosePreview)[Rac2Class2827HostileSession.NativeClassId];
        byte[]? nativeClassBytes = nativePosePreview
            ? Rac2MobyClasses.ClassBytes(core, Rac2Class2827HostileSession.NativeClassId)
            : null;
        var result = new Dictionary<int, RuntimeObjectAnimationClip>();
        foreach (int id in new[] { Rac2Class2827HostileSession.ChaseSequence, Rac2Class2827HostileSession.AttackSequence16,
            Rac2Class2827HostileSession.AttackSequence27, Rac2Class2827HostileSession.RecoverySequence })
        {
            var clip = cls.Sequences.Single(sequence => sequence.Index == id);
            if (clip.Frames.Count == 0 || clip.Frames.Any(frame => !float.IsFinite(frame.Speed) || frame.Speed <= 0 || frame.Speed > 1))
                throw new NotSupportedException("MSR I attack clip has unsupported native transition timing.");
            var poses = clip.Frames.Select(frame =>
            {
                var posed = nativeClassBytes is null
                    ? Rac2MobyAnimation.Pose(cls.Mesh, cls.Joints, frame)
                    : Ps2MobyNativePose.Pose(nativeClassBytes, cls.Mesh, frame);
                var positions = new double[posed.Length];
                for (int index = 0; index < posed.Length; index += 3)
                { positions[index] = posed[index]; positions[index + 1] = posed[index + 2]; positions[index + 2] = posed[index + 1]; }
                return positions;
            }).ToArray();
            if (surfaces.Any(surface => surface.Positions.Length != poses[0].Length))
                throw new InvalidDataException("MSR I animation does not match its original mesh vertex stream.");
            result.Add(id, new("gc-msr1-attack-" + id, RuntimeObjectAnimationRole.Reaction,
                Enumerable.Range(0, surfaces.Count).Select(index => new RuntimeObjectAnimationSurface(index, poses)).ToArray(),
                clip.Frames.Select(frame => 1d / (60 * frame.Speed)).ToArray()));
        }
        return new(result);
    }

    public static RuntimeObjectAnimationSample Sample(Asset asset, int sequence, int nativeTicks)
    {
        var clip = asset.Clips[sequence];
        double elapsed = Math.Max(0, nativeTicks) / 60d;
        bool loop = sequence == Rac2Class2827HostileSession.ChaseSequence;
        if (loop) elapsed %= clip.DurationSeconds;
        int frame = clip.FrameIndexAt(elapsed);
        double start = clip.FrameDurationsSeconds.Take(frame).Sum();
        float fraction = (float)Math.Clamp((elapsed - start) / clip.FrameDurationsSeconds[frame], 0, 1);
        return new(clip.Id, frame, loop ? (frame + 1) % clip.FrameCount : Math.Min(frame + 1, clip.FrameCount - 1), fraction);
    }
}
