using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2.Audio;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;

namespace OBP.Tests;

public sealed class Rac2CrateEffectsRetailTests
{
    [SkippableFact]
    public void OozlaUsesOriginalDetachablePartsAndClassSoundPrograms()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_GC_ISO not set");
        using var reader = new FileRandomAccessReader(path!);
        var wad = Iso9660Filesystem.Open(reader).OpenFile("/G/LEVEL1.WAD")!;
        var header = Rac2LevelWad.ReadHeader(wad);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var assets = Rac2CrateAssets.Read(core);
        Assert.Equal(new[] { 0, 2, 5 }, assets.Parts.Keys.Order());
        Assert.Equal(new[] { 32, 20, 20 }, assets.Parts.Values.Select(part => part.Surfaces.Sum(surface => surface.TriangleCount)));
        Assert.All(assets.Parts.Values, part => Assert.All(part.Surfaces, surface => Assert.Equal(40, surface.TextureId)));
        var sounds = Rac2CrateAudio.Read(core, Rac2SoundBank.Open(Rac2LevelWad.RequireLump(wad, header, 1)));
        Assert.Equal(69, sounds.Cues[500].BankSound);
        Assert.Equal(1, sounds.Cues[500].Gain);
        Assert.Equal(53, sounds.Cues[13].BankSound);
        Assert.Equal(53, sounds.Cues[14].BankSound);
        Assert.Equal(12, sounds.Cues[13].Variants.Count);
        Assert.All(sounds.Cues.Values.SelectMany(cue => cue.Variants), clip => Assert.True(clip.FrameCount > 0));
        var overlay = Rac2LevelOverlay.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        uint Word(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(overlay.ReadVirtual(address, 4));
        // Break: class-local sound 0, not the separate representative upgrade VAG.
        Assert.Equal(0x0000282Du, Word(0x39235C));
        Assert.Equal(0x0C0D55E0u, Word(0x392360));
        // Per-tick gravity, velocity clamp and exact one-unit stack spacing.
        Assert.Equal(0x3C013B32u, Word(0x3929B8));
        Assert.Equal(0x34216750u, Word(0x3929BC));
        Assert.Equal(0x3C01BE99u, Word(0x3929C4));
        Assert.Equal(0x3C013F80u, Word(0x392C08));
        Assert.Equal(0x24100025u, Word(0x3926A4));
        Assert.Equal(0x0C0C6FC4u, Word(0x39271C));
    }
}
