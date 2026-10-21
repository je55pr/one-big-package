using OBP.PS2.Audio;

namespace OBP.Tests;

public sealed class Ps2SoundRenderer989Tests
{
    private static Ps2SoundBank989.Tone Tone(int offset = 0) => new(0, 127, -60, 0, 0, 0, 0, 2, 2, 0x00ff, 0, 0, offset);
    private static Ps2SpuAdpcm.DecodedSample Sample(Ps2SoundBank989.Tone tone, bool repeats = false) =>
        new(Enumerable.Repeat((short)(tone.SampleOffset == 0 ? 10000 : 20000), 960).ToArray(),
            new(16, 1, 960, repeats ? 100 : null, 960, repeats));
    private static Ps2SoundBank989.Sound Sound(params Ps2SoundBank989.Grain[] grains) => new(1, 127, 0, 0, 0, 0, grains);

    [Fact]
    public void DelayedGrainsUse240HzAndAreMixedAtTheirOwnStartTimes()
    {
        var sound = Sound(new(1, 0, Tone()), new(1, 48, Tone(1)));
        var rendered = Assert.IsType<Ps2SoundRenderer989.Rendered>(Ps2SoundRenderer989.TryRender(sound, tone => Sample(tone)));
        Assert.Contains(rendered.Samples.Take(960), sample => sample > 0);
        Assert.All(rendered.Samples.Skip(2000).Take(7000), sample => Assert.Equal(0, sample));
        Assert.Contains(rendered.Samples.Skip(9600).Take(960), sample => sample > 15000);
    }

    [Fact]
    public void KeyOffEndsARepeatingVoiceInsteadOfPlayingItsEntireStoredLoopAgain()
    {
        var sound = Sound(new(1, 0, Tone()), new(41, 60, null), new(24, 0, null));
        var rendered = Assert.IsType<Ps2SoundRenderer989.Rendered>(Ps2SoundRenderer989.TryRender(sound, tone => Sample(tone, true)));
        Assert.Contains(rendered.Samples.Skip(9000).Take(1000), sample => sample > 0);
        Assert.All(rendered.Samples.Skip(12010), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void RandomPlaySelectsOneAlternativeInsteadOfFlatteningAllTones()
    {
        byte[] parameters = new byte[32]; parameters[0] = 2; parameters[2] = 1;
        var sound = Sound(new(25, 0, null, parameters), new(1, 0, Tone()), new(1, 0, Tone(1)));
        var rendered = Assert.IsType<Ps2SoundRenderer989.Rendered>(Ps2SoundRenderer989.TryRender(sound, tone => Sample(tone)));
        Assert.InRange(rendered.Samples.Max(), 10000, 20000);
    }

    [Fact]
    public void UnsupportedProgramsStayExplicitRatherThanReturningAnArbitraryTone()
    {
        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(new(99, 0, null), new(1, 0, Tone())), tone => Sample(tone)));
    }
}
