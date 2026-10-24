using OBP.PS2.Audio;
using OBP.IO;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;

namespace OBP.Tests;

public sealed partial class UyaShockCannonRetailTests
{
    [SkippableFact]
    public void GlobalGadgetExtractsOriginalCannonMeshTextureClipsAndSeparateCompactBank()
    {
        using var disc = OpenAuthority();
        byte[] header = disc.Read(1001L * 0x800 + UyaGlobalGadget.TocHeaderOffset, UyaGlobalGadget.HeaderSize);
        Hash(header, "eab8557f0a171e9efbc98fa23b8319f9027cc1356ba019845d6dfae451afeb87");
        var source = UyaGlobalGadget.Read(disc, UyaShockCannonProfile.HeldMobyClass);
        Assert.Equal(14, source.Entry.DirectoryIndex);
        Assert.Equal(new UyaGlobalGadget.Range(811, 22), source.Entry.Model);
        Assert.Equal(new UyaGlobalGadget.Range(833, 24), source.Entry.SoundBank);
        Hash(source.ClassBytes, "6d2ce1bac9f0fc6483f5fcc67adf5d417425a3b5ea5c740d48d5b0e9a0fe864d");
        Hash(source.TextureBytes, "93c1942160c41c34bc240ae5c4692e97fad2a2d84d20980c2e339a1e69329f22");
        Assert.Equal(2205, source.Mesh.Positions.Length / 3);
        Assert.Equal(1866, source.Mesh.Indices.Length / 3);
        Assert.Equal(10, source.Joints.Count);
        Assert.Equal(new[] { 11, 2, 11 }, source.Sequences.Select(s => s.Frames.Count));
        var texture = Assert.Single(UyaShockCannonAssets.Textures(source));
        Assert.Equal((128, 128), (texture.Width, texture.Height));
        Hash(texture.Rgba, "44d4e150346834617b3c73cf22ece9645fb247d16e32fabbe1ee0dd10dfc0112");
        var model = UyaShockCannonAssets.Model(source, 0, 0);
        Assert.Equal(1866, model.Sum(m => m.TriangleCount));
        Assert.All(model, surface =>
        {
            Assert.Equal(UyaShockCannonAssets.ModelAssetKind, surface.AssetKind);
            Assert.All(surface.Positions, p => Assert.True(double.IsFinite(p)));
            Assert.All(surface.Indices, i => Assert.InRange(i, 0, surface.Positions.Length / 3 - 1));
            // The class inverse bind cancels the authored resting joint pose.
            // Applying animated inverse joints alone crushes this -Y barrel.
            Assert.InRange(surface.Positions.Where((_, i) => i % 3 == 2).Min(), -1.119, -1.117);
            for (int v = 0; v < surface.Positions.Length; v += 3)
            {
                Assert.InRange(Math.Abs(surface.Positions[v] - source.Mesh.SkinLocalPositions[v]), 0, .00002);
                Assert.InRange(Math.Abs(surface.Positions[v + 1] - source.Mesh.SkinLocalPositions[v + 2]), 0, .00002);
                Assert.InRange(Math.Abs(surface.Positions[v + 2] - source.Mesh.SkinLocalPositions[v + 1]), 0, .00002);
            }
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => UyaShockCannonAssets.Model(source, 3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => UyaShockCannonAssets.Model(source, 0, 11));
        Assert.Equal(3, source.SoundBank!.Version);
        Assert.Equal(6, source.SoundBank.Sounds.Count);
        Assert.Equal(20, source.SoundBank.Sounds.Sum(s => s.Grains.Count));
        Assert.Equal(8, source.SoundBank.NumVags);
    }

    [SkippableFact]
    public void AllCannonProgramsDecodeIncludingRandomizedOrdinaryShotAndTwoSustainedChargeVoices()
    {
        using var disc = OpenAuthority();
        var source = UyaGlobalGadget.Read(disc, UyaShockCannonProfile.HeldMobyClass);
        Assert.Equal(new[] { 1843, 1536, 2048, 1945, 1126, 1024 }, source.SoundDefinitions.Select(s => s.MaximumVolume));
        foreach (int cue in Enumerable.Range(0, 6))
        {
            var sound = UyaShockCannonAssets.Audio(source, cue, 7);
            Assert.NotEmpty(sound.Voices);
            if (cue != 2) Assert.All(sound.Voices, v => Assert.Contains(v.Clip.InterleavedPcm16, sample => sample != 0));
        }
        // Original recovery cue2 is a silent 11-frame ADPCM sample, not a
        // missing decoder result. Do not invent a reload sound to fill it.
        var recovery = UyaShockCannonAssets.Audio(source, 2, 0);
        Assert.All(Assert.Single(recovery.Voices).Clip.InterleavedPcm16, sample => Assert.Equal(0, sample));
        Assert.All(source.SoundBank!.DecodeTone(Assert.Single(source.SoundBank.Sounds[2].Tones)).Samples, sample => Assert.Equal(0, sample));
        var charge = UyaShockCannonAssets.Audio(source, 3, 0);
        Assert.Equal(new[] { false, true, true }, charge.Voices.Select(v => v.Loop));
        Assert.Equal(new[] { 0d, 225d / 240, 275d / 240 }, charge.Voices.Select(v => v.StartSeconds));
        Assert.NotEqual(charge.Voices[1].Clip.LoopEndFrame - charge.Voices[1].Clip.LoopStartFrame,
            charge.Voices[2].Clip.LoopEndFrame - charge.Voices[2].Clip.LoopStartFrame);
        Assert.Equal(1945, charge.VolumeAtDistance(0));
        Assert.Equal(972, charge.VolumeAtDistance(17.5f));
        Assert.Equal(486, charge.VolumeAtDistance(17.5f, quadratic: true));
        Assert.Equal(0, charge.VolumeAtDistance(35));
        var shot = UyaShockCannonAssets.Audio(source, 4, 123);
        var repeated = UyaShockCannonAssets.Audio(source, 4, 123);
        Assert.Equal(5, shot.Voices.Count);
        Assert.Equal(new[] { 0d, 0, 0, 8d / 240, 18d / 240 }, shot.Voices.Select(v => v.StartSeconds));
        for (int i = 0; i < shot.Voices.Count; i++)
            Assert.Equal(shot.Voices[i].Clip.InterleavedPcm16, repeated.Voices[i].Clip.InterleavedPcm16);
        Assert.All(shot.Voices, v => Assert.False(v.Loop));
        var empty = UyaShockCannonAssets.Audio(source, 1, 0);
        Assert.Equal(381, empty.Definition.MinimumPitch);
        Assert.Equal(48000, Assert.Single(empty.Voices).Clip.SampleRateHz);
    }

    [SkippableFact]
    public void BeamTextureLookupLoaderGeometryAndSoundAttenuationPinOriginalInstructions()
    {
        using var disc = OpenAuthority();
        byte[] overlay = ReadTable1Overlay(disc);
        Hash(Virtual(overlay, 0x003873f8, 0x3e0), "c9d1957cd3c055d8dfe65884ee86cc69cac2fed03088d87a3751c8ded2663b61");
        Hash(Virtual(overlay, 0x00409a50, 0x4a0), "084dc15700741a2f1a8d47df6877eaaea58e40771fcfa0279e53dc14a799e8fd");
        Hash(Virtual(overlay, 0x00428fe8, 0x2c), "efc1e4643283e8823631b780184987b6502fe543bf67046a8900d8e8cee1fdd7");
        Hash(Virtual(overlay, 0x004225e8, 0xb0), "60322f7624789da8fceabf36d61648caa1f376e851c3b71642b06af6b619ddec");
        Hash(Virtual(overlay, 0x004a53d0, 0xd8), "e364e539463930faf4e76b05ed776fef7524d653b69762f24685b62c72a8463c");
        Hash(Virtual(overlay, 0x001d6470, 0x5c), "d107c0401c126e91247f14b739228efe70f40f3d23e7632caa05bba9eddfce10");
        Assert.Equal(15, F32(Virtual(overlay, 0x001dde40, 4), 0));
        Assert.Equal(23, I32(Virtual(overlay, 0x001dde34, 4), 0));
        var textures = UyaFxTextures.Read(UyaLevelCore.Open(disc, 1).Core, new HashSet<int> { 10, 23 });
        Assert.Equal((64, 64), (textures[10].Width, textures[10].Height));
        Assert.Equal((32, 32), (textures[23].Width, textures[23].Height));
        Hash(textures[10].Rgba, "4a533ab174a088868b13240ca0dbcd9b04965cd66ae9c90c682d95852d991a46");
        Hash(textures[23].Rgba, "866e4ed766ddb501d2d22757bc94f7a900d8ed28d0ada04ee366e35ef047b7f7");
    }
}
