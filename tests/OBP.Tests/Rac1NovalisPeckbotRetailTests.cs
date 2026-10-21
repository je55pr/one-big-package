using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1NovalisPeckbotRetailTests
{
    [SkippableFact]
    public void OriginalNovalisHas68NativePeckbotsAndOriginalDeathFragments()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, 1);
        var mobys = world.DynamicObjects ??
            throw new InvalidDataException("Novalis Moby records not imported.");
        var birds = mobys.Where(moby => moby.NativeClassId == Rac1NovalisPeckbotSession.ClassId).ToArray();
        Assert.Equal(68, birds.Length);
        int initiallyStanding = 0, initiallyLoitering = 0;
        foreach (var bird in birds)
        {
            byte[] pvar = Assert.Single(bird.NativePayloads!, payload =>
                payload.Format == Rac1BoltCrate.PVarPayloadFormat).Data;
            Assert.Equal(0x270, pvar.Length);
            int mode = BinaryPrimitives.ReadInt16LittleEndian(pvar.AsSpan(0x20a));
            if (mode == 0) initiallyLoitering++;
            else if (mode == 1) initiallyStanding++;
            else throw new InvalidDataException($"Unsupported Peckbot placement selector {mode}");
            int centre = Rac1EnemyMotion.RewardCentre(bird);
            Assert.Contains(centre, new[] { 0, 16 });
        }
        Assert.True(initiallyStanding > 0);
        Assert.True(initiallyLoitering > 0);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == 1);
        var core = Rac1LevelCore.Open(reader, level);
        var classes = Rac1StaticClasses.Read(core).Mobies;
        foreach (int id in new[] { 1747, 1748, 1749 })
        {
            Assert.True(classes.TryGetValue(id, out var fragment));
            Assert.Single(fragment!.Sequences, s => s.Value is not null);
            Assert.Single(fragment.Sequences[0].Value!.Frames);
        }
    }

    [Fact]
    public void PeckAtAnimationWindowAppliesOneNanotechWithNormalInvulnerability()
    {
        var player = new OBP.RAC1.Player.Rac1RatchetNanotechSession();
        var damage = new Rac1GameplayDamageEvent(
            Rac1GameplayEntityRef.Moby(new(577, 586)), Rac1GameplayEntityRef.Player, 1);
        Assert.Equal(3, player.ApplyDamage(damage).Nanotech);
        Assert.Equal(3, player.ApplyDamage(damage).Nanotech);
        Assert.Throws<NotSupportedException>(() =>
            player.ApplyDamage(new Rac1GameplayDamageEvent(
                Rac1GameplayEntityRef.Moby(new(577, 586)),
                Rac1GameplayEntityRef.Player, 2)));
    }
}
