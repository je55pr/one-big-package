using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1NovalisAudioTests
{
    [SkippableFact]
    public void NovalisCranksDoorsMushroomsRocksAndEnemiesHaveDecodedRetailOneShots()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 1);
        var core = Rac1LevelCore.Open(reader, level);
        var sounds = Rac1CrateAudio.Decode(reader, level, core,
            new HashSet<int> { 280, 459, 577, 665, 704, 754 });

        Assert.Equal(156, sounds[(280, 0)].BankSoundId);
        Assert.Equal(161, sounds[(459, 0)].BankSoundId);
        Assert.Equal(189, sounds[(577, 0)].BankSoundId);
        Assert.Equal(215, sounds[(665, 0)].BankSoundId);
        Assert.Equal(216, sounds[(665, 1)].BankSoundId);
        Assert.Equal(226, sounds[(704, 0)].BankSoundId);
        Assert.Equal(239, sounds[(754, 0)].BankSoundId);
        Assert.All(sounds.Values, sound => Assert.NotEmpty(sound.Clip.InterleavedPcm16));
    }
}
