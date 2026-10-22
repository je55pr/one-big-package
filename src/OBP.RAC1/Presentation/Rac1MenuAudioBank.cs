using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Audio;
using OBP.Runtime.Audio;
using OBP.RAC1.Gameplay;

namespace OBP.RAC1.Presentation;

/// <summary>
/// Two short original R&C1 effects from the startup/global 989snd bank.
/// Source voices 0/1 are confirmed retail audio, but their exact native menu
/// event association has not yet been proven from the panel's SoundDef table.
/// </summary>
public static class Rac1MenuAudioBank
{
    public static IReadOnlyDictionary<int, RuntimeAudioClip> Read(IRandomAccessReader disc, ReadOnlySpan<byte> index)
    {
        int lba = BinaryPrimitives.ReadInt32LittleEndian(index.Slice(0x14E0, 4));
        int sectors = BinaryPrimitives.ReadInt32LittleEndian(index.Slice(0x14E4, 4));
        long length = (long)sectors * 2048;
        long offset = (long)lba * 2048;
        if (lba < 1 || length is <= 24 or > 4_194_304 || offset > disc.Length || length > disc.Length - offset)
            throw new InvalidDataException("R&C1 startup sound bank outside source bounds.");
        var bank = Ps2SoundBank989.Open(new SubRangeReader(disc, offset, length, "rac1-shared-startup-audio"));
        if (bank.Sounds.Count < 2) throw new InvalidDataException("R&C1 startup sound slots missing.");
        var cues = new Dictionary<int, RuntimeAudioClip>();
        for (int slot = 0; slot < 2; slot++)
        {
            var source = bank.Sounds[slot];
            if (source.Grains is not [{ Type: 1, Delay: 0, Tone: { } tone }])
                throw new InvalidDataException("R&C1 startup menu-audio candidate format differs.");
            var sample = bank.DecodeTone(tone);
            cues.Add(slot, new RuntimeAudioClip(
                Rac1CrateAudio.ApproximateToneRate(tone.CenterNote, tone.CenterFine, 0), 1, sample.Samples));
        }
        return cues;
    }
}
