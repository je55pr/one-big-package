using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Audio;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using OBP.Runtime.Audio;

namespace OBP.RAC2.Audio;

/// <summary>Source-local SoundDef dispatch for crate breaks and supply/Bolt collection. Pitch bend, gain and program selection come from the active level.</summary>
public static class Rac2CrateAudio
{
    public sealed record Cue(int NativeClass, int BankSound, double Gain, IReadOnlyList<RuntimeAudioClip> Variants);
    public sealed record Catalogue(IReadOnlyDictionary<(int NativeClass, int LocalSound), Cue> Cues);
    public static Catalogue Load(string path, int level) => Load(path, level, null);

    /// <summary>Decode only the requested family's sounds; unrelated unsupported classes cannot reject that family.</summary>
    public static Catalogue Load(string path, int level, IReadOnlySet<int>? nativeClasses)
    {
        using var reader = new FileRandomAccessReader(path);
        var wad = Iso9660Filesystem.Open(reader).OpenFile($"/G/LEVEL{level}.WAD") ?? throw new InvalidDataException("Crate audio level is unavailable.");
        var header = Rac2LevelWad.ReadHeader(wad);
        return Read(Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0)), Rac2SoundBank.Open(Rac2LevelWad.RequireLump(wad, header, 1)), nativeClasses);
    }
    public static Catalogue Read(Rac2LevelCore.Core core, Rac2SoundBank bank) => Read(core, bank, null);

    public static Catalogue Read(Rac2LevelCore.Core core, Rac2SoundBank bank, IReadOnlySet<int>? nativeClasses)
    {
        var result = new Dictionary<(int, int), Cue>();
        int directory = core.Header.SoundRemapOffset;
        var rows = Rac2MobyClasses.ClassRows(core);
        var present = rows.Select(row => row.NativeClass).ToHashSet();
        foreach (int cls in new[] { 500, 501, 511, 3291, 13, 14, 15, 16, 17, 18, 19 }
            .Where(cls => present.Contains(cls) && (nativeClasses is null || nativeClasses.Contains(cls))))
        {
            var bytes = Rac2MobyClasses.ClassBytes(core, cls);
            if (bytes.Length < 0x48) throw new InvalidDataException("Truncated crate sound class header.");
            if (bytes[0xd] == 0) continue; // Native class-sound dispatch returns -1 for this class.
            int definitions = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x28));
            if (definitions < 0x48 || definitions > bytes.Length - bytes[0xd] * 32) throw new InvalidDataException("Crate SoundDef is out of bounds.");
            int row = rows.Single(row => row.NativeClass == cls).Index;
            long entryOffset = (long)directory + 8 + row * 4L;
            if (directory < 0 || entryOffset > core.Index.Length - 4L) throw new InvalidDataException("Crate sound remap directory is out of bounds.");
            int entry = (int)entryOffset;
            int offset = BinaryPrimitives.ReadUInt16LittleEndian(core.Index.AsSpan(entry));
            int count = BinaryPrimitives.ReadUInt16LittleEndian(core.Index.AsSpan(entry + 2));
            long firstOffset = (long)directory + offset;
            // R&C2 preserves the absent direct slot before the local SoundDefs.
            if (count != bytes[0xd] + 1 || firstOffset < entryOffset + 4 || firstOffset > core.Index.Length - count * 4L)
                throw new InvalidDataException("Crate SoundDef remap does not match its recovered native layout.");
            int first = (int)firstOffset;
            if (BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(first)) != -1)
                throw new InvalidDataException("Crate SoundDef remap has no absent direct slot.");
            for (int localSound = 0; localSound < bytes[0xd]; localSound++)
            {
                int definition = definitions + localSound * 32;
                int id = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(first + (localSound + 1) * 4));
                if ((uint)id >= bank.Sounds.Count) throw new InvalidDataException("Crate sound is outside its original SBlk.");
                int pitchMin = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(definition + 16));
                int pitchMax = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(definition + 20));
                if (pitchMin > pitchMax || pitchMin < short.MinValue || pitchMax > short.MaxValue)
                    throw new InvalidDataException("Crate SoundDef pitch bend range is invalid.");
                double gain = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(definition + 12)) / 2048d, 0, 1);
                var variants = new List<RuntimeAudioClip>();
                int variantCount = pitchMin != pitchMax || bank.Sounds[id].Grains.Any(grain => grain.Type is 25 or 27 or 31) ? 12 : 1;
                for (int seed = 0; seed < variantCount; seed++)
                {
                    // Oozla 0x35574C stores SoundDef +0x10/+0x14 at slot +0x84;
                    // 0x355378 passes it as t1 (PB), distinct from t0 (PM).
                    int bend = pitchMin == pitchMax ? pitchMin : SamplePitch(seed, pitchMin, pitchMax);
                    var voices = Ps2SoundRenderer989.TryRenderVoices(bank.Sounds[id], bank.DecodeTone, seed, pitchBend: bend)
                        ?? throw new NotSupportedException($"Original crate sound {id} has an unsupported grain program.");
                    if (voices.Count != 1 || voices[0].Loop || voices[0].StartFrame != 0)
                        throw new NotSupportedException("Crate audio needs an unsupported multi-voice or delayed host.");
                    var audio = voices[0].Audio;
                    variants.Add(new(Ps2SoundRenderer989.SampleRate, 1, audio.Samples));
                }
                result.Add((cls, localSound), new(cls, id, gain, variants));
            }
        }
        return new(result);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S2245", Justification = "Seeded cosmetic audio pitch variation has no security role; deterministic variants are intentional.")]
    // Cosmetic audio only; retain deterministic variants, never security randomness.
    private static int SamplePitch(int seed, int minimum, int maximum) => new Random(seed).Next(minimum, maximum + 1); // nosemgrep: csharp.crypto.rule-WeakRNG
}
