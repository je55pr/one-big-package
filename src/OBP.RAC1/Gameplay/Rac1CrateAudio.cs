using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Audio;
using OBP.RAC1.Level;
using OBP.Runtime.Audio;

namespace OBP.RAC1.Gameplay;

/// <summary>Retail event associations and bank voices, with explicit host playback-rate approximation.</summary>
public static class Rac1CrateAudio
{
    public sealed record Sound(int BankSoundId, RuntimeAudioClip Clip, double Gain,
        int CenterNote, int CenterFine, int PitchMinimum, int PitchMaximum, bool Loop = false,
        IReadOnlyList<RuntimeAudioClip>? Alternatives = null)
    {
        public RuntimeAudioClip SelectPitch(Rac1PickupRandom random)
        {
            var clip = Alternatives is { Count: > 0 } choices ? choices[random.Below(choices.Count)] : Clip;
            int modulation = PitchMaximum > PitchMinimum
                ? PitchMinimum + random.Below(PitchMaximum - PitchMinimum) : PitchMinimum;
            return new(ApproximateToneRate(CenterNote, CenterFine, modulation), 1, clip.InterleavedPcm16,
                clip.LoopStartFrame, clip.LoopEndFrame);
        }
    }

    /// <summary>
    /// 989snd note60, 128 fine units/semitone and signed centre-note rate basis.
    /// Musical ratio approximation; native lookup-table/SPU quantization and ADSR
    /// are intentionally not claimed bit-exact (see crate/pickup evidence).
    /// </summary>
    public static int ApproximateToneRate(int centerNote, int centerFine, int pitchModulation) =>
        Math.Clamp((int)Math.Round((centerNote >= 0 ? 44_100d : 48_000d) *
            Math.Pow(2, (60 - Math.Abs(centerNote) + (centerFine + pitchModulation) / 128d) / 12)), 100, 192_000);

    public static IReadOnlyDictionary<(int Class, int LocalSound), Sound> Decode(
        IRandomAccessReader disc, Rac1DiscIndex.NativeLevel level, Rac1LevelCore.Core core,
        IReadOnlySet<int>? admittedClasses = null)
    {
        var data = Rac1DiscIndex.OpenRange(disc, level, 0);
        var bank = Ps2SoundBank989.Open(Rac1LevelData.OpenRange(data, Rac1LevelData.ReadDirectory(data), 1));
        var result = new Dictionary<(int, int), Sound>();
        var programs = new Dictionary<int, Ps2SoundRenderer989.Rendered?>();
        var classOffsets = Rac1StaticClasses.ReadMobyTable(core).ToDictionary(row => row.OClass, row => row.AssetOffset);
        foreach (var entry in Rac1ClassSoundRemap.Read(core).Where(entry =>
                     admittedClasses?.Contains(entry.Key) ?? (entry.Key is 0 or 71 or >= 13 and <= 16 or 74 or 121 or 153 or 172 or 186 or 190 or 192 or 203 or 229 or 230 or 270 or 280 or 457 or 459 or 479 or 577 or 665 or 704 or 749 or 754 or 1440 ||
                         Rac1WeaponProfiles.Additional.Any(profile => profile.ModelClass == entry.Key) || Rac1BoltCrate.IsCrateClass(entry.Key))))
        {
            int cls = classOffsets[entry.Key];
            ReadOnlySpan<byte> payload = cls > 0 ? core.Assets.AsSpan(cls) : Rac1GlobalMoby.Read(core, entry.Key).Payload;
            for (int local = 0; local < entry.Value.Count; local++)
            {
                int id = entry.Value[local];
                if (id == -1) continue;
                if ((uint)id >= bank.Sounds.Count) throw new InvalidDataException("R&C1 remapped sound is outside level bank.");
                if (payload.Length < 0x48 || local >= payload[0xd])
                    throw new InvalidDataException("R&C1 class SoundDef/remap count differs.");
                long at = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x28, 4)) + local * 0x20L;
                if (at < 0x48 || at > payload.Length - 0x20)
                    throw new InvalidDataException("R&C1 class SoundDef is outside assets.");
                if (DecodeSound(bank, id, payload.Slice((int)at, 32), programs) is { } decoded)
                    result.Add((entry.Key, local), decoded);
            }
        }
        return result;
    }

    public static IReadOnlyDictionary<int, Sound> DecodeLevelSounds(
        IRandomAccessReader disc, Rac1DiscIndex.NativeLevel level, Rac1LevelCore.Core core,
        IReadOnlySet<int>? admitted = null)
    {
        var data = Rac1DiscIndex.OpenRange(disc, level, 0);
        var bank = Ps2SoundBank989.Open(Rac1LevelData.OpenRange(data, Rac1LevelData.ReadDirectory(data), 1));
        var programs = new Dictionary<int, Ps2SoundRenderer989.Rendered?>();
        var result = new Dictionary<int, Sound>();
        foreach (var entry in Rac1LevelSoundRemap.Read(core.Index))
        {
            if (entry.BankSoundId == -1 || admitted?.Contains(entry.Index) == false) continue;
            if ((uint)entry.BankSoundId >= bank.Sounds.Count)
                throw new InvalidDataException("R&C1 level remapped sound is outside level bank.");
            if (DecodeSound(bank, entry.BankSoundId, core.Index.AsSpan(entry.DefinitionOffset, 32), programs) is { } sound)
                result.Add(entry.Index, sound);
        }
        return result;
    }

    private static Sound? DecodeSound(Ps2SoundBank989 bank, int id, ReadOnlySpan<byte> definition,
        Dictionary<int, Ps2SoundRenderer989.Rendered?> programs)
    {
        var sound = bank.Sounds[id];
        int pitchMin = BinaryPrimitives.ReadInt32LittleEndian(definition.Slice(0x10, 4));
        int pitchMax = BinaryPrimitives.ReadInt32LittleEndian(definition.Slice(0x14, 4));
        if (pitchMin < short.MinValue || pitchMax > short.MaxValue || pitchMin > pitchMax)
            throw new InvalidDataException("R&C1 SoundDef pitch range is invalid.");
        double gain = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(definition.Slice(0xc, 4)) / 2048d, 0, 1);
        if (sound.Grains is [{ Type: 1, Delay: 0, Tone: { } tone }])
        {
            var pcm = bank.DecodeTone(tone);
            var clip = new RuntimeAudioClip(ApproximateToneRate(tone.CenterNote, tone.CenterFine, 0), 1, pcm.Samples,
                sound.Loop ? pcm.Info.LoopStartSample ?? 0 : 0);
            return new(id, clip, Math.Clamp(gain * sound.Volume / 127d * tone.Volume / 127d, 0, 1),
                tone.CenterNote, tone.CenterFine, pitchMin, pitchMax, sound.Loop);
        }
        if (!programs.TryGetValue(id, out var rendered))
            programs[id] = rendered = Ps2SoundRenderer989.TryRender(sound, bank.DecodeTone);
        if (rendered is null) return null;
        RuntimeAudioClip[]? alternatives = null;
        if (sound.Grains.Any(grain => grain.Type == 31))
        {
            var variants = Enumerable.Range(0, 8).Select(seed => Ps2SoundRenderer989.TryRender(sound, bank.DecodeTone, id * 17 + seed)).ToArray();
            if (variants.Any(variant => variant is null)) return null;
            alternatives = variants.Select(variant => new RuntimeAudioClip(Ps2SoundRenderer989.SampleRate, 1,
                variant!.Samples, variant.LoopStartFrame, variant.LoopEndFrame)).ToArray();
            rendered = variants.FirstOrDefault(variant => variant!.Samples.Length > 0) ?? rendered;
        }
        return new(id, new RuntimeAudioClip(Ps2SoundRenderer989.SampleRate, 1, rendered.Samples,
            rendered.LoopStartFrame, rendered.LoopEndFrame), gain, -60, 0, pitchMin, pitchMax, sound.Loop, alternatives);
    }

    // Metal break explicitly requests class500 sound0 in 0x2d0a54.
    public static int BreakSoundClass(int crateClass) => crateClass == 502 ? 500 : crateClass;
    public const int BreakLocalSound = 0;
    public const int NanotechArrivalLocalSound = 1;
}
