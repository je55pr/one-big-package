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
        int CenterNote, int CenterFine, int PitchMinimum, int PitchMaximum)
    {
        public RuntimeAudioClip SelectPitch(Rac1PickupRandom random)
        {
            int modulation = PitchMaximum > PitchMinimum
                ? PitchMinimum + random.Below(PitchMaximum - PitchMinimum) : PitchMinimum;
            return new(ApproximateToneRate(CenterNote, CenterFine, modulation), 1, Clip.InterleavedPcm16);
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
        var classOffsets = Rac1StaticClasses.ReadMobyTable(core).ToDictionary(row => row.OClass, row => row.AssetOffset);
        foreach (var entry in Rac1ClassSoundRemap.Read(core).Where(entry =>
                     admittedClasses?.Contains(entry.Key) ?? (entry.Key is >= 13 and <= 16 or 121 or 192 or 749 or 1440 || Rac1BoltCrate.IsCrateClass(entry.Key))))
        {
            int cls = classOffsets[entry.Key];
            ReadOnlySpan<byte> payload = cls > 0 ? core.Assets.AsSpan(cls) : Rac1GlobalMoby.Read(core, entry.Key).Payload;
            for (int local = 0; local < entry.Value.Count; local++)
            {
                int id = entry.Value[local];
                if (id == -1) continue;
                if ((uint)id >= bank.Sounds.Count) throw new InvalidDataException("R&C1 remapped sound is outside level bank.");
                var sound = bank.Sounds[id];
                // This slice admits only a single immediate tone. Do not flatten
                // scripted grain programs into an incorrect representative sound.
                if (sound.Grains is not [{ Type: 1, Delay: 0, Tone: { } tone }]) continue;
                var pcm = bank.DecodeTone(tone);
                if (payload.Length < 0x48 || local >= payload[0xd])
                    throw new InvalidDataException("R&C1 class SoundDef/remap count differs.");
                long at = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x28, 4)) + local * 0x20L;
                if (at < 0x48 || at > payload.Length - 0x20)
                    throw new InvalidDataException("R&C1 class SoundDef is outside assets.");
                int pitchMin = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice((int)at + 0x10, 4));
                int pitchMax = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice((int)at + 0x14, 4));
                if (pitchMin < short.MinValue || pitchMax > short.MaxValue || pitchMin > pitchMax)
                    throw new InvalidDataException("R&C1 class SoundDef pitch range is invalid.");
                var clip = new RuntimeAudioClip(ApproximateToneRate(tone.CenterNote, tone.CenterFine, 0), 1, pcm.Samples);
                result.Add((entry.Key, local), new(id, clip,
                    Math.Clamp(sound.Volume / 127d * tone.Volume / 127d, 0, 1), tone.CenterNote, tone.CenterFine, pitchMin, pitchMax));
            }
        }
        return result;
    }

    // Metal break explicitly requests class500 sound0 in 0x2d0a54.
    public static int BreakSoundClass(int crateClass) => crateClass == 502 ? 500 : crateClass;
    public const int BreakLocalSound = 0;
    public const int NanotechArrivalLocalSound = 1;
}
