using OBP.IO;
using OBP.PS2.Audio;

namespace OBP.RAC2.Audio;

/// <summary>Going Commando level-WAD sound-bank lump 1 over the shared 989snd codec.</summary>
public sealed class Rac2SoundBank : Ps2SoundBank989
{
    private Rac2SoundBank(Ps2SoundBank989 source) : base(source) { }

    public new static Rac2SoundBank Open(IRandomAccessReader reader) => new(Ps2SoundBank989.Open(reader));
}
