using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Audio;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using OBP.Runtime.Audio;

namespace OBP.RAC2.Audio;

/// <summary>Source-local class SoundDef 0 dispatch for crate break and Bolt arrival. Pitch, gain and program selection come from the active level.</summary>
public static class Rac2CrateAudio
{
    public sealed record Cue(int NativeClass, int BankSound, double Gain, IReadOnlyList<RuntimeAudioClip> Variants);
    public sealed record Catalogue(IReadOnlyDictionary<int, Cue> Cues);
    public static Catalogue Load(string path, int level)
    {
        using var reader = new FileRandomAccessReader(path);
        var wad = Iso9660Filesystem.Open(reader).OpenFile($"/G/LEVEL{level}.WAD") ?? throw new InvalidDataException("Crate audio level is unavailable.");
        var header = Rac2LevelWad.ReadHeader(wad);
        return Read(Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0)), Rac2SoundBank.Open(Rac2LevelWad.RequireLump(wad, header, 1)));
    }
    public static Catalogue Read(Rac2LevelCore.Core core, Rac2SoundBank bank)
    {
        var result = new Dictionary<int, Cue>();
        int directory = core.Header.SoundRemapOffset;
        foreach (int cls in new[] { 500, 13, 14, 15, 16, 17, 18, 19 })
        {
            var bytes = Rac2MobyClasses.ClassBytes(core, cls);
            if (bytes.Length < 0x48) throw new InvalidDataException("Truncated crate sound class header.");
            if (bytes[0xd] == 0) continue; // Native class-sound dispatch returns -1 for this class.
            int definition = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x28));
            if (definition < 0x48 || definition > bytes.Length - 32) throw new InvalidDataException("Crate SoundDef is out of bounds.");
            int row = Enumerable.Range(0, core.Header.MobyClasses.Count).Single(index =>
                BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(core.Header.MobyClasses.Offset + index * 32 + 4)) == cls);
            int entry = checked(directory + 8 + row * 4);
            if (directory < 0 || entry > core.Index.Length - 4) throw new InvalidDataException("Crate sound remap directory is out of bounds.");
            int offset = BinaryPrimitives.ReadUInt16LittleEndian(core.Index.AsSpan(entry));
            int count = BinaryPrimitives.ReadUInt16LittleEndian(core.Index.AsSpan(entry + 2));
            int first = checked(directory + offset);
            // R&C2 preserves the absent direct slot before its sole local SoundDef.
            if (count != bytes[0xd] + 1 || first < entry + 4 || first > core.Index.Length - count * 4L || BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(first)) != -1)
                throw new InvalidDataException("Crate SoundDef remap does not match its recovered native layout.");
            int id = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(first + 4));
            if ((uint)id >= bank.Sounds.Count) throw new InvalidDataException("Crate sound is outside its original SBlk.");
            int pitchMin = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(definition + 16));
            int pitchMax = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(definition + 20));
            if (pitchMin != pitchMax || pitchMin < short.MinValue || pitchMax > short.MaxValue)
                throw new InvalidDataException("Crate SoundDef pitch needs an unrecovered selector.");
            double gain = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(definition + 12)) / 2048d, 0, 1);
            var variants = new List<RuntimeAudioClip>();
            int variantCount = bank.Sounds[id].Grains.Any(grain => grain.Type is 25 or 27 or 31) ? 12 : 1;
            for (int seed = 0; seed < variantCount; seed++)
            {
                var voices = Ps2SoundRenderer989.TryRenderVoices(bank.Sounds[id], bank.DecodeTone, seed, pitchMin)
                    ?? throw new NotSupportedException($"Original crate sound {id} has an unsupported grain program.");
                if (voices.Count != 1 || voices[0].Loop || voices[0].StartFrame != 0)
                    throw new NotSupportedException("Crate audio needs an unsupported multi-voice or delayed host.");
                var audio = voices[0].Audio;
                variants.Add(new(Ps2SoundRenderer989.SampleRate, 1, audio.Samples));
            }
            result.Add(cls, new(cls, id, gain, variants));
        }
        return new(result);
    }
}
