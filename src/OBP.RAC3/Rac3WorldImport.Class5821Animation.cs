using OBP.IO;
using OBP.PS2.Geometry;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;
using OBP.Runtime;

namespace OBP.RAC3;

/// <summary>
/// Retail geometry and per-surface frame mapping for the one-eyed Tyhrranoid's
/// proven TABLE1 state-10 action 12. Prepares a clip only; no invented native
/// state clock, idle/approach behavior, transition, damage or looping.
/// </summary>
public static partial class Rac3WorldImport
{
    public static RuntimeObjectAnimationClip LoadClass5821State10Clip(
        string sourcePath,
        IReadOnlyList<RuntimeObjectMesh> importedSurfaces)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(importedSurfaces);
        using var disc = new FileRandomAccessReader(sourcePath);
        var level = Rac3LevelCore.Open(disc, 1);
        int textures = Rac3Assets.ReadTextures(level.Core, Rac3Assets.TextureTable.Moby).Count;
        var classes = Rac3Assets.ReadMobyClasses(level.Core, textures, nativePoseOverride: true);
        if (!classes.Decoded.TryGetValue(Rac3Class5821Actor.NativeClassId, out var cls) ||
            cls.NativePoseSource is null || !cls.Mesh.SkinStateFullyResolved)
            throw new NotSupportedException("UYA TABLE1 one-eyed actor has no fully recovered native skeleton.");

        var sequence = cls.Sequences.Single(
            s => s.Index == Rac3Class5821Actor.NativeState10SelectedAction);
        if (sequence.Frames.Count != 20 || sequence.BoundingSphere is null ||
            sequence.Frames.Any(frame => !frame.ExtraChannelsResolved ||
                !float.IsFinite(frame.Speed) || frame.Speed is <= 0 or > 1))
            throw new NotSupportedException("UYA class-5821 state-10 action 12 has unsupported native frames.");

        var vertexMaps = new Dictionary<int, int[][]>();
        var expected = BuildMobyModels(
            new Dictionary<int, Rac3Assets.MobyVisualClass> { [cls.OClass] = cls },
            new HashSet<int> { cls.OClass }, vertexMaps)[cls.OClass];
        if (expected.Count == 0 || expected.Count != importedSurfaces.Count)
            throw new InvalidDataException("UYA class-5821 animation surfaces differ from original imported actor.");

        // Do not infer a vertex's joint from its XYZ coordinates: two different
        // native vertices can have identical positions but different skin weights.
        // The exact source-to-surface vertex remaps come from BuildMobyModels.
        var remaps = vertexMaps[cls.OClass];
        for (int index = 0; index < expected.Count; index++)
        {
            var original = expected[index];
            var actual = importedSurfaces[index];
            if (original.TextureId != actual.TextureId ||
                original.MaterialPresentation != actual.MaterialPresentation ||
                !original.Positions.SequenceEqual(actual.Positions) ||
                !original.Indices.SequenceEqual(actual.Indices) ||
                !original.Uvs.SequenceEqual(actual.Uvs))
                throw new InvalidDataException(
                    "UYA class-5821 native animation does not match the actor's original surface topology.");
        }

        var sphere = sequence.BoundingSphere.Value;
        double k = cls.Mesh.Scale / 1024d;
        double cx = sphere.X * k, cy = sphere.Y * k, cz = sphere.Z * k;
        double limit = sphere.Radius * k * 1.10d;
        if (!double.IsFinite(limit) || limit <= 0d)
            throw new InvalidDataException("UYA class-5821 action has invalid source bounds.");

        var nativeFrames = new List<double[]>(sequence.Frames.Count);
        foreach (var frame in sequence.Frames)
        {
            var native = Ps2MobyNativePose.Pose(cls.NativePoseSource, cls.Mesh, frame);
            for (int offset = 0; offset < native.Length; offset += 3)
            {
                double dx = native[offset] - cx;
                double dy = native[offset + 1] - cy;
                double dz = native[offset + 2] - cz;
                if (Math.Sqrt(dx * dx + dy * dy + dz * dz) > limit)
                    throw new InvalidDataException("UYA class-5821 action escaped the validated native pose bounds.");
            }
            nativeFrames.Add(native);
        }

        var surfaces = new List<RuntimeObjectAnimationSurface>(remaps.Length);
        for (int surface = 0; surface < remaps.Length; surface++)
        {
            int[] vertices = remaps[surface];
            var localFrames = nativeFrames.Select(native =>
            {
                var local = new double[vertices.Length * 3];
                for (int index = 0; index < vertices.Length; index++)
                {
                    int from = vertices[index] * 3, to = index * 3;
                    local[to] = native[from];
                    local[to + 1] = native[from + 2]; // PS2 Z-up -> OBP Y-up
                    local[to + 2] = native[from + 1];
                }
                return local;
            }).ToArray();
            surfaces.Add(new(surface, localFrames));
        }

        return new("uya-class5821-state10-action12", RuntimeObjectAnimationRole.Reaction,
            surfaces, sequence.Frames.Select(frame => 1d / (60d * frame.Speed)).ToArray());
    }

    /// <summary>
    /// Explicit OBP-only visual audition for #297's eight-actor diagnostic.
    /// Repeats the genuine native frames on a 60Hz diagnostic clock with a
    /// one-second held final pose. This is NOT recovered UYA state progression.
    /// </summary>
    public static RuntimeObjectAnimationSample SampleClass5821Diagnostic(
        RuntimeObjectAnimationClip clip, long elapsedTicks)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.Id != "uya-class5821-state10-action12" || clip.FrameCount != 20)
            throw new ArgumentException("Only the verified UYA state-10 action-12 clip is auditable.", nameof(clip));
        if (elapsedTicks < 0) throw new ArgumentOutOfRangeException(nameof(elapsedTicks));
        // Quantize only the OBP diagnostic repeat boundary, not the retail
        // clip's frame durations. Floating sums can otherwise keep the final
        // frame one extra tick at an exact cycle boundary.
        long cycleTicks = checked((long)Math.Round(clip.DurationSeconds * 60d,
            MidpointRounding.AwayFromZero)) + 60;
        double seconds = (elapsedTicks % cycleTicks) / 60d;
        int frame = clip.FrameIndexAt(seconds);
        double start = 0;
        for (int index = 0; index < frame; index++) start += clip.FrameDurationsSeconds[index];
        float fraction = (float)Math.Clamp((seconds - start) / clip.FrameDurationsSeconds[frame], 0d, 1d);
        return new(clip.Id, frame, Math.Min(frame + 1, clip.FrameCount - 1), fraction);
    }
}
