using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using OBP.IO;
using OBP.PS2.Elf;
using OBP.PS2.Iso;
using OBP.RAC2;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;

namespace OBP.Tests;

public sealed class Rac2ParticleRetailTests
{
    private static readonly string[] PvarHashes =
    [
        "b022ff55f600457d27f63332559947b238e28a81316ffd81929b14855baccdfd",
        "eff43e11a85a38c306bed48464c824de0361c469305bcb970eda6c5a136e43dc",
        "05f2b6c00018295fc37d8929f11fc6ed738cc7d5133db3863ee82b2f09968833"
    ];
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [SkippableFact]
    public void MuseumHasThreeDistinctAuthoredEmittersAndAnIndependent109TexturePartBank()
    {
        using var fixture = new Fixture();
        Assert.Equal(new[] { 262, 263, 264 }, fixture.Instances.Select(m => m.Index));
        Assert.Equal(PvarHashes, fixture.Instances.Select(m => Rac2MuseumParticleEmitter.FromAuthored(m).AuthoredParametersSha256));
        Assert.Equal(new[] { 118, 119, 120 }, fixture.Instances.Select(m => m.PVarIndex));
        Assert.Equal(109, fixture.Assets.Textures.Count); Assert.Equal(111, fixture.Assets.Definitions.DefinitionCount);
        Assert.Equal(0, fixture.Assets.Definitions.Texture(104, 0));
        var expectedTextures = new[] { 44, 23, 47 };
        var imageHashes = new[]
        {
            "cc203fc377b393190c469c93029848fd48f5e65e716780e39717439f0ee3718d",
            "a48580072d7cd6cf42a52d04cdfa4a9917d4ce5054cb0b7f275e6ccb601f93d7",
            "e07cd794e7345df48ac01b19731597a5f69bd271fa952757a81bc9c903504ce6"
        };
        var colors = new[] { 0x05801212u, 0x141c2a2fu, 0x0f0d0c0cu };
        for (int i = 0; i < 3; i++)
        {
            var owner = Rac2MuseumParticleEmitter.FromAuthored(fixture.Instances[i]);
            var rng = Rac2ParticleRandom.FromSeed(1234); var session = new Rac2ParticleSession(fixture.Assets, rng);
            var p = session.Emit(owner).Particle!;
            Assert.Equal(expectedTextures[i], p.Texture); Assert.Equal(colors[i], p.Color); Assert.Equal(3, rng.SamplesConsumed);
            var texture = fixture.Assets.Textures[p.Texture];
            Assert.Equal(32, texture.Width); Assert.Equal(32, texture.Height); Assert.Equal(imageHashes[i], Hash(texture.Rgba));
            Assert.Contains(texture.Rgba.Where((_, at) => at % 4 == 3), alpha => alpha > 0);
            owner.StopEmission(); int lifetime = owner.Parameters.Lifespan;
            for (int tick = 1; tick < lifetime; tick++) { session.Step(); Assert.True(session.IsLive(p)); }
            session.Step(); Assert.Empty(session.Live);
        }
    }

    [SkippableFact]
    public void AllAuthoredRecordsAndSpriteDescriptorsMatchOriginalEeVuForThreeSeedsUntilExpiry()
    {
        string program = Oracle(); using var fixture = new Fixture();
        foreach (var instance in fixture.Instances)
            foreach (uint seed in new uint[] { 0, 1234, 32768 })
                Compare(program, fixture, instance, instance.PVarData!, seed, Rac2MuseumParticleEmitter.FromAuthored(instance).Parameters.Lifespan, false, 100);
    }

    [SkippableFact]
    public void DiagnosticIntervalsGravityOpacityLifetimeAdjustmentAndDepthFadeMatchRetailInstructions()
    {
        string program = Oracle(); using var fixture = new Fixture(); var instance = fixture.Instances[1];
        foreach (uint seed in new uint[] { 0, 1234, 32768 })
        {
            // Modified branch probes, explicitly separate from the authored-effect acceptance fixtures.
            byte[] p = (byte[])instance.PVarData!.Clone();
            Rac2ParticleTests.Put(p, 0, -.03125f); Rac2ParticleTests.Put(p, 4, .0625f); Rac2ParticleTests.Put(p, 8, -.125f);
            Rac2ParticleTests.Put(p, 0x34, -.03125f); Rac2ParticleTests.Put(p, 0x10, 9); Rac2ParticleTests.Put(p, 0x20, -1000);
            Rac2ParticleTests.Put(p, 0x24, 127); Rac2ParticleTests.Put(p, 0x28, 0);
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(0x4c), 1); BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(0x4e), 3);
            Compare(program, fixture, instance, p, seed, 24, true, 100);
            p = (byte[])instance.PVarData.Clone();
            Rac2ParticleTests.Put(p, 0x10, 100); Rac2ParticleTests.Put(p, 0x24, 0); Rac2ParticleTests.Put(p, 0x28, 127);
            Compare(program, fixture, instance, p, seed, 127, false, 100);
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(0x4c), 9); BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(0x4e), 3);
            Compare(program, fixture, instance, p, seed, 24, true, 100);
        }
        foreach (float distance in new[] { 4f, 4.5f, 479.5f })
            Compare(program, fixture, instance, instance.PVarData!, 1234, 1, false, distance);
    }

    [SkippableFact]
    public void EveryRegisteredMenuControlWritesTheSameNativePvarBytes()
    {
        string program = Oracle(); using var fixture = new Fixture();
        var controls = Rac2MuseumEffectControls.ReadParticleEditor(fixture.Overlay);
        var input = new StringBuilder("17\n");
        foreach (var c in controls) input.AppendLine(FormattableString.Invariant($"{c.PVarOffset} {(int)c.Kind} {c.Minimum} {c.Maximum}"));
        var edits = (from index in Enumerable.Range(0, controls.Count) from step in new[] { 5, -1, -5, 1 } select (index, step)).ToArray();
        foreach (var (index, step) in edits) input.AppendLine(FormattableString.Invariant($"{index} {step}"));
        byte[] pvars = fixture.Instances[0].PVarData!;
        string[] rows = fixture.Execute(program, fixture.Instances[0], pvars, "edit", input.ToString());
        Assert.Equal(edits.Length, rows.Length);
        var managed = Rac2MuseumParticleParameters.Parse(pvars);
        for (int i = 0; i < edits.Length; i++)
        {
            managed = managed.Edit(controls[edits[i].index], edits[i].step);
            Assert.Equal(managed.CopyNativeParameters(), Words(rows[i].Split(' ').Skip(3)));
        }
    }

    [SkippableFact]
    public void SaturatedNativePoolPreservesIntervalDrawsAndSkipsConstructorAngle()
    {
        string program = Oracle(); using var fixture = new Fixture(); var instance = fixture.Instances[0];
        var rows = fixture.Execute(program, instance, instance.PVarData!, "full", "1234 0 0 0");
        Assert.Equal("F 0 2048 2 0", Assert.Single(rows, row => row.StartsWith("F ", StringComparison.Ordinal)));
        var rng = Rac2ParticleRandom.FromSeed(1234); var session = new Rac2ParticleSession(fixture.Assets, rng);
        for (int i = 0; i < Rac2ParticleSession.NativeCapacity; i++)
            Assert.NotNull(session.Spawn85(Rac2ParticleTests.Source, new(Vector3.Zero, Vector3.Zero, 40, 0, 0, 0, 0, 0, 0, 0)));
        long before = rng.SamplesConsumed;
        Assert.Equal(Rac2ParticleSession.EmissionStatus.PoolFull, session.Emit(Rac2MuseumParticleEmitter.FromAuthored(instance)).Status);
        Assert.Equal(2, rng.SamplesConsumed - before); Assert.Equal(2048, session.Count);
    }

    private static void Compare(string program, Fixture fixture, Rac2Instances.MobyInstance instance, byte[] pvars, uint seed, int frames, bool repeat, float distance)
    {
        string input = FormattableString.Invariant($"{seed} {frames} {(repeat ? 1 : 0)} {distance}");
        var rows = fixture.Execute(program, instance, pvars, "emit", input).Select(row => row.Split(' ')).ToArray();
        foreach (var row in rows.Where(r => r[0] == "D")) Assert.Equal(fixture.Assets.Definitions.Texture(int.Parse(row[1]), 0), int.Parse(row[2]));
        foreach (var row in rows.Where(r => r[0] == "T"))
        {
            var bound = fixture.Assets.NativeLayouts[int.Parse(row[1])].Bind(0x1000);
            Assert.Equal(bound.Palette, uint.Parse(row[2])); Assert.Equal(bound.Pixels, uint.Parse(row[3]));
        }
        var owner = Rac2MuseumParticleEmitter.FromAuthored(instance);
        owner.ApplyNativeFrame(Rac2MuseumParticleParameters.Parse(pvars), owner.Position);
        var rng = Rac2ParticleRandom.FromSeed(seed); var session = new Rac2ParticleSession(fixture.Assets, rng);
        var draw = Rac2ParticleDraw.ReadMuseum(fixture.Overlay);
        var view = new Rac2ParticleDraw.View(owner.Position - new Vector3(0, 0, distance), Vector3.UnitZ, 1f / 420000, int.MaxValue);
        session.Emit(owner);
        for (int tick = 0; tick <= frames; tick++)
        {
            string at = tick.ToString(CultureInfo.InvariantCulture);
            var frame = Assert.Single(rows, r => r[0] == "F" && r[1] == at);
            Assert.Equal(session.Count, int.Parse(frame[2])); Assert.Equal(rng.SamplesConsumed, long.Parse(frame[3]));
            Assert.Equal(owner.Countdown, float.Parse(frame[4], CultureInfo.InvariantCulture));
            Assert.Equal(owner.Parameters.LowSpawnTimer, int.Parse(frame[5])); Assert.Equal(owner.Parameters.HighSpawnTimer, int.Parse(frame[6]));
            var native = rows.Where(r => r[0] == "R" && r[1] == at).ToArray(); var managed = session.Live.ToArray();
            Assert.Equal(managed.Length, native.Length);
            for (int i = 0; i < managed.Length; i++)
            {
                Assert.Equal(managed[i].Slot, int.Parse(native[i][2]));
                byte[] expected = Words(native[i].Skip(3)); byte[] actual = managed[i].CopyNativeRecord();
                Assert.True(expected.SequenceEqual(actual), $"instance {instance.Index}, seed {seed}, tick {tick}, slot {managed[i].Slot}: record mismatch at byte {FirstDifference(expected, actual):x}");
            }
            var sprites = rows.Where(r => r[0] == "S" && r[1] == at).ToArray(); Assert.Equal(Math.Min(1, managed.Length), sprites.Length);
            if (sprites.Length != 0)
            {
                var p = managed[0]; var sprite = draw.Build(p, view, fixture.Assets.NativeLayouts[p.Texture], 0x100000);
                Assert.True(sprite.HasValue, $"native sprite was admitted at instance {instance.Index} tick {tick}");
                byte[] expected = Words(sprites[0].Skip(3)), actual = sprite.Value.CopyNativeDescriptor();
                Assert.True(expected.SequenceEqual(actual), $"instance {instance.Index}, seed {seed}, tick {tick}: sprite mismatch at byte {FirstDifference(expected, actual):x}; native={Convert.ToHexString(expected)}, managed={Convert.ToHexString(actual)}");
            }
            if (tick < frames) { session.Step(); if (repeat) session.Emit(owner); }
        }
    }
    private static int FirstDifference(byte[] a, byte[] b) => Enumerable.Range(0, a.Length).FirstOrDefault(i => a[i] != b[i], -1);
    private static byte[] Words(IEnumerable<string> words)
    {
        uint[] numbers = words.Select(n => uint.Parse(n, CultureInfo.InvariantCulture)).ToArray(); byte[] result = new byte[numbers.Length * 4];
        for (int i = 0; i < numbers.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(i * 4), numbers[i]); return result;
    }
    private static string Oracle()
    {
        string? program = Environment.GetEnvironmentVariable("OBP_RAC2_MUSEUM_ORACLE");
        Skip.If(string.IsNullOrWhiteSpace(program), "Build tools/build-rac2-museum-oracle.ps1 and set OBP_RAC2_MUSEUM_ORACLE."); return program!;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly FileRandomAccessReader _disc;
        public Rac2LevelOverlay.Image Overlay { get; }
        public Rac2ParticleAssets Assets { get; }
        public Rac2Instances.MobyInstance[] Instances { get; }
        private readonly byte[] _ram = new byte[32 * 1024 * 1024];
        public Fixture()
        {
            string? path = Environment.GetEnvironmentVariable("OBP_GC_ISO"); Skip.If(string.IsNullOrWhiteSpace(path), "OBP_GC_ISO not set");
            _disc = new(path!); Assert.Equal(Rac2Authority.PrimaryIsoSizeBytes, _disc.Length);
            var fs = Iso9660Filesystem.Open(_disc); var wad = fs.OpenFile("/G/LEVEL21.WAD")!;
            var header = Rac2LevelWad.ReadHeader(wad); Assert.Equal(30, header.LevelId);
            var data = Rac2LevelWad.RequireLump(wad, header, 0); Overlay = Rac2LevelOverlay.Open(data);
            Assert.Equal("b46e44c26195efd160b1bc62910cf22e1b1fe3eb46944f5926a9f2b7879e45b6", Hash(Overlay.Raw));
            Instances = Rac2Instances.Read(Rac2LevelWad.RequireLump(wad, header, 2)).MobyInstances.Where(m => m.OClass == 4887).ToArray();
            var core = Rac2LevelCore.Open(data); Assets = Rac2ParticleAssets.Read(core, Rac2ParticleTests.Source);
            var executable = fs.OpenFile("/SCUS_972.68")!;
            Assert.Equal("36d5814d8d95328d5839612ccdf7a2e7ecac0b6f868d3ad4bb2e98411f734b4a", Hash(executable.Read(0, checked((int)executable.Length))));
            foreach (var s in Elf32Reader.ReadProgramHeaders(executable).Where(p => p.Type == Elf32Reader.ProgramTypeLoad))
                executable.Read(s.Offset, checked((int)s.FileSize)).CopyTo(_ram, checked((int)s.VirtualAddress));
            foreach (var s in Overlay.Sections) Overlay.Raw.AsSpan(s.DataOffset, s.CopySize).CopyTo(_ram.AsSpan((int)s.DestinationAddress));
            core.Index.CopyTo(_ram, 0x1000000);
        }
        public string[] Execute(string program, Rac2Instances.MobyInstance owner, byte[] pvars, string mode, string input)
        {
            pvars.CopyTo(_ram, 0x1701000);
            void F(int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(_ram.AsSpan(at), value);
            F(0x1700010, owner.Position.X); F(0x1700014, owner.Position.Y); F(0x1700018, owner.Position.Z);
            string privateRam = Path.Combine(Path.GetTempPath(), $"obp-gc-museum-{Guid.NewGuid():N}.bin");
            try
            {
                File.WriteAllBytes(privateRam, _ram);
                var start = new ProcessStartInfo(program) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                start.ArgumentList.Add(privateRam); start.ArgumentList.Add(mode);
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                process.StandardInput.WriteLine(input); process.StandardInput.Close();
                if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("GC Museum oracle timed out."); }
                Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
                return output.GetAwaiter().GetResult().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            finally { File.Delete(privateRam); }
        }
        public void Dispose() => _disc.Dispose();
    }
}
