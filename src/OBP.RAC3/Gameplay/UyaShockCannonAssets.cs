using OBP.PS2.Audio;
using OBP.PS2.Geometry;
using OBP.PS2.Presentation;
using OBP.RAC3.Level;
using OBP.Runtime;
using OBP.Runtime.Audio;
using OBP.Runtime.Presentation;

namespace OBP.RAC3.Gameplay;

/// <summary>
/// Source-owned global gadget presentation, distinct from any level-local
/// Moby texture/audio remap. No Ratchet pose or attachment fallback is implied.
/// </summary>
public static class UyaShockCannonAssets
{
    public const string ModelAssetKind = "rac3-global-gadget:1882";
    public sealed record Voice(double StartSeconds, bool Loop, RuntimeAudioClip Clip);
    public sealed record Sound(int LocalSoundIndex, UyaGlobalGadget.SoundDefinition Definition,
        IReadOnlyList<Voice> Voices)
    {
        public UyaShockCannonIdentity Weapon => UyaShockCannonProfile.Identity;
        /// <summary>Native linear/quadratic SoundDef law, before host gain normalization.</summary>
        public int VolumeAtDistance(float distance, bool quadratic = false)
        {
            if (!float.IsFinite(distance) || distance < 0) throw new ArgumentOutOfRangeException(nameof(distance));
            if (distance <= Definition.MinimumRange) return Definition.MaximumVolume;
            if (distance >= Definition.MaximumRange) return Definition.MinimumVolume;
            float numerator = RoundVolume((double)Definition.MaximumRange - distance);
            float denominator = RoundVolume((double)Definition.MaximumRange - Definition.MinimumRange);
            if (quadratic || (Definition.Flags & 1) != 0)
            {
                numerator = RoundVolume((double)numerator * numerator);
                denominator = RoundVolume((double)denominator * denominator);
            }
            numerator = RoundVolume((double)numerator * (Definition.MaximumVolume - Definition.MinimumVolume));
            // Native DIV rounds to nearest; CVT.W then truncates the result.
            return Definition.MinimumVolume + (int)(numerator / denominator);
        }

        /// <summary>
        /// OBP playback gain convention. Native SoundDef units are retained;
        /// this does not claim the original SPU/master mixer is reproduced.
        /// </summary>
        public double HostGain(float distance) => VolumeAtDistance(distance) / 2048d;

        private static float RoundVolume(double value)
        {
            float result = (float)value;
            return Math.Abs(result) > Math.Abs(value)
                ? result > 0 ? MathF.BitDecrement(result) : MathF.BitIncrement(result)
                : result;
        }
    }

    public static IReadOnlyList<RuntimeTexture> Textures(UyaGlobalGadget.Source source)
    {
        RequireCannon(source);
        return source.Textures.Select((texture, id) => new RuntimeTexture(ModelAssetKind, id,
            texture.Width, texture.Height, texture.Rgba)).ToArray();
    }

    /// <summary>
    /// Explicit source sequence/frame, not a fabricated idle selector. The
    /// global gadget vertices need their original inverse bind and animated joints.
    /// </summary>
    public static IReadOnlyList<RuntimeObjectMesh> Model(UyaGlobalGadget.Source source, int sequence, int frame)
    {
        RequireCannon(source);
        var clip = source.Sequences.SingleOrDefault(s => s.Index == sequence)
            ?? throw new ArgumentOutOfRangeException(nameof(sequence));
        if ((uint)frame >= clip.Frames.Count) throw new ArgumentOutOfRangeException(nameof(frame));
        double[] native = GcUyaGadgetPose.Pose(source.Mesh, source.Joints, source.InverseBindMatrices, clip.Frames[frame]);
        var positions = new double[native.Length];
        for (int i = 0; i < native.Length; i += 3)
        {
            positions[i] = native[i]; positions[i + 1] = native[i + 2]; positions[i + 2] = native[i + 1];
        }
        var surfaces = new Dictionary<(int, RuntimeMaterialPresentation?), List<int>>();
        for (int face = 0; face < source.Mesh.Indices.Length / 3; face++)
        {
            int material = source.Mesh.TriangleMaterialStateIndices[face];
            var presentation = material >= 0 ? Ps2MaterialPresentation.From(source.Mesh.Materials[material], classifyMobySurface: true) : null;
            var key = (source.Mesh.TriangleMaterialSlots[face], presentation);
            if (!surfaces.TryGetValue(key, out var indices)) surfaces.Add(key, indices = []);
            indices.AddRange(source.Mesh.Indices.AsSpan(face * 3, 3).ToArray());
        }
        return surfaces.Select(surface => new RuntimeObjectMesh(ModelAssetKind, surface.Key.Item1,
            positions, source.Mesh.Uvs, surface.Value.ToArray(), MaterialPresentation: surface.Key.Item2)).ToArray();
    }

    /// <summary>
    /// Decode one complete local program on demand, with caller-owned RNG seed.
    /// Each sustained tone has its own loop; the intro is never looped wholesale.
    /// Musical pitch/resampling and ADSR use the shared renderer's documented
    /// host approximation, not a claim of bit-exact SPU synthesis.
    /// </summary>
    public static Sound Audio(UyaGlobalGadget.Source source, int localSoundIndex, int seed)
    {
        RequireCannon(source);
        if ((uint)localSoundIndex >= source.SoundDefinitions.Count) throw new ArgumentOutOfRangeException(nameof(localSoundIndex));
        var definition = source.SoundDefinitions[localSoundIndex];
        if (definition.MinimumPitch != definition.MaximumPitch)
            throw new NotSupportedException("An unresolved SoundDef pitch RNG range needs explicit caller input.");
        var bank = source.SoundBank ?? throw new InvalidDataException("UYA Cannon sound bank is absent.");
        var voices = Ps2SoundRenderer989.TryRenderVoices(bank.Sounds[definition.SoundIndex], bank.DecodeTone, seed, definition.MinimumPitch)
            ?? throw new NotSupportedException("UYA Cannon sound program is outside the admitted interpreter.");
        return new(localSoundIndex, definition, voices.Select(voice => new Voice(
            (double)voice.StartFrame / Ps2SoundRenderer989.SampleRate, voice.Loop,
            new(Ps2SoundRenderer989.SampleRate, 1, voice.Audio.Samples, voice.Audio.LoopStartFrame, voice.Audio.LoopEndFrame))).ToArray());
    }

    private static void RequireCannon(UyaGlobalGadget.Source source)
    {
        if (source.Entry.NativeClassId != UyaShockCannonProfile.HeldMobyClass)
            throw new ArgumentException("Shock Cannon presentation requires the original class-1882 asset.", nameof(source));
    }
}
