using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1NovalisMusicTests
{
    [SkippableFact]
    public void RetailLevelMusicTableIdentifiesOriginalCaveAndWaterworksIntrosLoops()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var index = Rac1DiscIndex.Read(disc);
        var novalis = index.Levels.Single(l => l.LevelId == 1);
        var music = Rac1NovalisMusic.Open(disc, novalis);
        Assert.Equal(9, music.Tracks.Count);
        Assert.Equal((0, 1), music.Pair(Rac1NovalisMusic.Area.Enemy));
        Assert.Equal((2, 3), music.Pair(Rac1NovalisMusic.Area.Cave));
        Assert.Equal((4, 5), music.Pair(Rac1NovalisMusic.Area.Waterworks));
        string[] expected =
        [
            "L01_Enemy_Start", "L01_Enemy_Loop",
            "L01_Cave_Start", "L01_Cave_Loop",
            "L01_Waterworks_S", "L01_Waterworks_L",
            "L01_Transition1", "L01_Transition2", "L01_Transition3",
        ];
        for (int i = 0; i < expected.Length; i++)
        {
            var track = music.Tracks[i];
            Assert.Equal(expected[i], track.Header.Name);
            Assert.Equal(44100, track.Header.SampleRate);
            Assert.InRange(track.DurationSeconds, 3, 160);
            if (i != 0) Assert.True(track.Sector > music.Tracks[i - 1].Sector);
        }
        Assert.InRange(music.Tracks[2].DurationSeconds, 7.6, 7.8);
        Assert.InRange(music.Tracks[3].DurationSeconds, 107, 108);
        Assert.InRange(music.Tracks[5].DurationSeconds, 149, 151);

        var intro = music.Decode(2);
        Assert.Equal(1, intro.ChannelCount);
        Assert.Equal(44100, intro.SampleRateHz);
        Assert.InRange(intro.DurationSeconds, 7.6, 7.8);
        Assert.Contains(intro.InterleavedPcm16, s => s != 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => music.Decode(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => music.Decode(9));
        Assert.Throws<ArgumentOutOfRangeException>(() => music.Pair((Rac1NovalisMusic.Area)42));
    }

}
