using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using OBP.IO;
using OBP.PS2.Elf;
using OBP.PS2.Iso;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1ParticleEmitterRetailTests
{
    private const string PvarHash = "8d5d507bb3ef654ef549279a97fc2c94d08cb6a87e3c6db13ff2076f74f82c71";

    [SkippableFact]
    public void AllNineteenLevelsContainOneAuthoredPresetAcrossTenNovalisOwners()
    {
        using var disc = Open();
        var found = new List<(int Level, int Instance, int Pvars)>();
        foreach (var level in Rac1DiscIndex.Read(disc).Levels)
            foreach (var instance in Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).MobyInstances.Where(m => m.OClass == 27))
            {
                var owner = Rac1ParticleEmitter.FromAuthored(level.LevelId, instance);
                Assert.Equal(PvarHash, owner.AuthoredParametersSha256);
                Assert.Equal(0x170eu, owner.Parameters.Flags); Assert.Equal(150, owner.Parameters.Lifetime);
                Assert.Equal(4, owner.Parameters.TextureDefinition); Assert.Equal(1, owner.Parameters.BurstCount);
                Assert.Equal(0, owner.Parameters.BurstInterval);
                found.Add((level.LevelId, instance.Index, instance.PVarIndex));
            }
        Assert.Equal(Enumerable.Range(285, 10).Select(i => (1, i, i + 54)), found);
    }

    [SkippableFact]
    public void ActualNovalisPresetUsesTheExistingPartBankCapsItsSpeedAndDrainsAfter150Ticks()
    {
        using var disc = Open(); var level = Rac1DiscIndex.Read(disc).Levels.Single(l => l.LevelId == 1);
        var instance = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).MobyInstances.Single(m => m.Index == 285);
        var owner = Rac1ParticleEmitter.FromAuthored(1, instance);
        var assets = Rac1EffectAssets.DecodeEmitters(Rac1LevelCore.Open(disc, level), [owner.Parameters]);
        Assert.Equal(new[] { 0 }, assets.ParticleChoices[4]); Assert.Single(assets.PartTextures); Assert.Empty(assets.FxTextures);
        var session = new Rac1ParticleSession(assets, Rac1PickupRandom.FromSeed(1234));
        session.Emit(owner, Rac1ParticleEmitterTests.Seeing(owner.Position)); var p = Assert.Single(session.Live);
        Assert.Equal(0xff262626u, p.Color); Assert.Equal(0, p.Texture);
        Assert.InRange(p.Size * Rac1ParticleSession.NativeSizeUnits, 200000, 310000);
        Assert.InRange(p.Velocity.Length(), .99999f, 1.00001f);
        var before = p.Position; session.Step(Vector3.Zero);
        Assert.InRange(Vector3.Distance(before, p.Position), .9999f, 1.0001f);
        Assert.InRange(p.Velocity.Length(), .01499999f, .01500001f);
        owner.StopEmission();
        for (int tick = 1; tick < 149; tick++) { session.Step(Vector3.Zero); Assert.True(session.IsLive(p)); }
        session.Step(Vector3.Zero); Assert.Empty(session.CaptureSprites());
    }

    [SkippableFact]
    public void AuthoredAndExplicitSyntheticBranchesMatchOriginalEeVuInstructionsForThreeSeeds()
    {
        string? program = Environment.GetEnvironmentVariable("OBP_RAC1_TYPE6_ORACLE");
        Skip.If(string.IsNullOrWhiteSpace(program), "Build tools/build-rac1-type6-oracle.ps1 and set OBP_RAC1_TYPE6_ORACLE.");
        using var disc = Open(); var level = Rac1DiscIndex.Read(disc).Levels.Single(l => l.LevelId == 1);
        var overlay = Rac1ExecutableOverlay.Read(disc, level);
        Assert.Equal("81673b8dcf5930c7313a7c83d0f76e5e1948691dc09af63dd8c8f6a04f9a72ae", overlay.Sha256);
        var instance = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).MobyInstances.Single(m => m.Index == 285);
        var core = Rac1LevelCore.Open(disc, level);
        byte[] ram = new byte[32 * 1024 * 1024];
        var executable = Iso9660Filesystem.Open(disc).OpenFile("/SCUS_971.99")!;
        Assert.Equal("e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9",
            Convert.ToHexString(SHA256.HashData(executable.Read(0, checked((int)executable.Length)))).ToLowerInvariant());
        foreach (var segment in Elf32Reader.ReadProgramHeaders(executable).Where(p => p.Type == Elf32Reader.ProgramTypeLoad))
            executable.Read(segment.Offset, checked((int)segment.FileSize)).CopyTo(ram, checked((int)segment.VirtualAddress));
        overlay.CopyLoadedSections(ram);
        void Put(int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(ram.AsSpan(at), value);
        Put(0x1000010, instance.Position.X); Put(0x1000014, instance.Position.Y); Put(0x1000018, instance.Position.Z);
        ram[0x1003000] = checked((byte)Rac1ParticleAssets.InitialTexture(core, 4));
        // Original bytes are temporary only and always deleted; output contains derived numbers.
        string privateRam = Path.Combine(Path.GetTempPath(), $"obp-type6-{Guid.NewGuid():N}.bin");
        try
        {
            foreach (var trial in Trials(instance))
                foreach (uint seed in new uint[] { 0, 1234, 32768 })
                {
                    trial.Parameters.CopyTo(ram, 0x1001000);
                    Put(0x1000040, trial.Rotation.X); Put(0x1000044, trial.Rotation.Y); Put(0x1000048, trial.Rotation.Z);
                    File.WriteAllBytes(privateRam, ram);
                    string[] rows = Execute(program!, privateRam, seed);
                    var owner = Rac1ParticleEmitter.FromAuthored(1, instance);
                    Assert.Equal(PvarHash, owner.AuthoredParametersSha256);
                    owner.ApplyNativeFrame(Rac1ParticleEmitterParameters.Parse(trial.Parameters), owner.Position, trial.Rotation);
                    var session = new Rac1ParticleSession(Rac1EffectAssets.DecodeEmitters(core, [owner.Parameters]), Rac1PickupRandom.FromSeed(seed));
                    session.Emit(owner, Rac1ParticleEmitterTests.Seeing(owner.Position)); var p = Assert.Single(session.Live);
                    Assert.Equal(151, rows.Length);
                    for (int tick = 0; tick <= 150; tick++)
                    {
                        string[] values = rows[tick].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        double Number(int i) => double.Parse(values[i], CultureInfo.InvariantCulture);
                        Assert.Equal(tick, Number(0)); Assert.Equal(session.IsLive(p) ? 1 : 0, Number(1)); Assert.Equal(8, Number(2));
                        if (session.IsLive(p))
                        {
                            double[] managed = [p.Position.X, p.Position.Y, p.Position.Z, p.Velocity.X, p.Velocity.Y, p.Velocity.Z, p.Size * Rac1ParticleSession.NativeSizeUnits];
                            for (int k = 0; k < managed.Length; k++)
                                Assert.True(Math.Abs(managed[k] - Number(3 + k)) <= (k == 6 ? .03125 : .0001),
                                    $"{trial.Name} seed {seed} tick {tick} field {k}: managed {managed[k]:R}, native {Number(3 + k):R}");
                            Assert.Equal((uint)Number(10), p.Color); Assert.Equal((byte)Number(11), p.Angle);
                            Assert.Equal((int)Number(12), p.Remaining); Assert.Equal((int)Number(13), p.Texture);
                            Assert.Equal((byte)Number(14), (byte)p.Blending); Assert.Equal((byte)Number(15), p.DepthRange);
                        }
                        if (tick < 150) session.Step(Vector3.Zero);
                    }
                }
        }
        finally { File.Delete(privateRam); }
    }

    private static IEnumerable<(string Name, byte[] Parameters, Vector3 Rotation)> Trials(Rac1Instances.MobyInstance instance)
    {
        yield return ("authored", instance.PVar!, new(instance.Rotation.X, instance.Rotation.Y, instance.Rotation.Z));
        // Diagnostic branch probes, deliberately NOT described as source-authored effects.
        foreach (bool euler in new[] { false, true })
        {
            byte[] p = Rac1ParticleEmitterTests.SyntheticParameters();
            void F(int at, float value) => Rac1ParticleEmitterTests.Put(p, at, value);
            Rac1ParticleEmitterTests.Put(p, 0xc0, 12); Rac1ParticleEmitterTests.Put(p, 0x70, euler ? 0xfffe : 0xeffe);
            F(0, .001f); F(4, -.002f); F(8, -.003f); F(0x40, .05f);
            F(0x30, 400); F(0x34, -.007f); F(0x38, .5f); F(0x44, .25f);
            F(0x4c, .3f); F(0x50, .5f); F(0x54, .7f);
            F(0x80, .1f); F(0x84, .2f); F(0x88, .05f);
            F(0x90, euler ? 30 : .4f); F(0x94, euler ? 20 : .5f); F(0x98, euler ? 10 : .6f);
            F(0xcc, 1); F(0xd0, 2); F(0xd4, 4);
            yield return (euler ? "synthetic-euler-owner" : "synthetic-cartesian-owner", p, new(2.9f, .4f, -2.8f));
        }
    }

    private static string[] Execute(string program, string privateRam, uint seed)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(privateRam);
        using var process = Process.Start(start)!;
        process.StandardInput.WriteLine(FormattableString.Invariant($"{seed} 150")); process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Type-6 native oracle timed out."); }
        Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static FileRandomAccessReader Open()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        var disc = new FileRandomAccessReader(path!); Assert.Equal(Rac1Authority.PrimaryIsoSizeBytes, disc.Length); return disc;
    }
}
