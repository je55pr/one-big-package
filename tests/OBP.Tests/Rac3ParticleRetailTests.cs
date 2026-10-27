using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using OBP.IO;
using OBP.PS2.Compression;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;

namespace OBP.Tests;

public sealed class Rac3ParticleRetailTests
{
    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    [SkippableFact]
    public void ActualPartBankAndTwoCallersHaveQualifiedResourcesAndIndependentVisibleResults()
    {
        using var fixture = new Fixture();
        Assert.Equal(65, fixture.Assets.Textures.Count); Assert.Equal(129, fixture.Assets.Definitions.DefinitionCount);
        Assert.Equal(12, fixture.Assets.Definitions.Texture(97, 0));
        Assert.Equal(new[] { 53, 54, 55, 56 }, Enumerable.Range(0, 4).Select(i => fixture.Assets.Definitions.Texture(104, i)));
        // This narrow sprite leaf is byte-identical to the separately witnessed GC leaf; the whole renderers differ.
        Assert.Equal("400bce1e6a3040e64674c888a902a0c1c682de798285afb218b4f158aef92526", Hash(fixture.Read(0x45ee98, 0x118)));
        Assert.Equal("7b501f4a20f4760aa38169d4ca794e4db7f36a6379a5ece8b506e9b0c3ad79d7", Hash(fixture.Read(0x45e810, 0xc28)));
        Assert.Equal(0x46800020u, BinaryPrimitives.ReadUInt32LittleEndian(fixture.Read(0x46dd7c, 4))); // CVT.S.W life.
        Assert.Equal(0x4614a881u, BinaryPrimitives.ReadUInt32LittleEndian(fixture.Read(0x46dd80, 4))); // SUB.S sizes.
        Assert.Equal(0x46001083u, BinaryPrimitives.ReadUInt32LittleEndian(fixture.Read(0x46dda4, 4))); // DIV.S size step.
        Assert.Equal(0x0c11b72eu, BinaryPrimitives.ReadUInt32LittleEndian(fixture.Read(0x385f18, 4)));
        Assert.Equal(0x0c11b72eu, BinaryPrimitives.ReadUInt32LittleEndian(fixture.Read(0x38667c, 4)));
        Assert.Equal(0x0c11b72eu, BinaryPrimitives.ReadUInt32LittleEndian(fixture.Read(0x386724, 4)));
        Assert.Equal(0x2404006fu, BinaryPrimitives.ReadUInt32LittleEndian(fixture.Read(0x46fe9c, 4))); // Separate ready-charge family 111.
        var charge = new Rac3ParticleSession(fixture.Assets, Rac3ParticleRandom.FromSeed(1234));
        var beam = new Rac3ParticleSession(fixture.Assets, Rac3ParticleRandom.FromSeed(1234));
        charge.EmitShockCannonCharging(Rac3ParticleTests.Owner, 30, Rac3ParticleTests.Muzzle);
        beam.EmitShockCannonBeamGlow(Rac3ParticleTests.Owner, Rac3ParticleTests.Muzzle, Rac3ParticleTests.End);
        Assert.Equal(2, charge.Count); Assert.Equal(6, beam.Count);
        Assert.NotEqual(charge.Live.First().Color, beam.Live.First().Color);
        Assert.NotEqual(charge.Live.First().NativeSize, beam.Live.First().NativeSize);
        Assert.NotEqual(charge.Live.First().Position, beam.Live.First().Position);
        foreach (int id in new[] { 12, 53, 54, 55, 56 })
        {
            var image = fixture.Assets.Textures[id]; Assert.True(image.Width > 0); Assert.Equal(image.Width * image.Height * 4, image.Rgba.Length);
            Assert.Contains(image.Rgba.Where((_, i) => i % 4 == 3), alpha => alpha != 0);
        }
        Assert.Equal(new[]
        {
            "f413498e09f2f99c044a01686bbfda929b6006186403552d8aeb778bd3ea0575",
            "1881c0a654a0f3ab9701771c1782ebaf8e229a738cc23d0e6325ac5e75ea3bf3",
            "65a8d4054241cacb0d5fb455bcd2e4c29051c7ad72c6735547b8604a548d1060",
            "6b5d0e42a8c401305b4860204652660bdd51edf64c559fb613b358d0cadd24eb"
        }, Enumerable.Range(53, 4).Select(id => Hash(fixture.Assets.Textures[id].Rgba)));
        for (int i = 0; i < 20; i++) { charge.Step(); beam.Step(); }
        Assert.Empty(charge.Live); Assert.Empty(beam.Live);
    }

    [SkippableFact]
    public void BothNativeRecipesMatchEveryLiveRecordAndSpriteByteForThreeSeedsThroughExpiry()
    {
        string program = Oracle(); using var fixture = new Fixture();
        foreach (bool beam in new[] { false, true })
            foreach (uint seed in new uint[] { 0, 1234, 32768 })
                Compare(program, fixture, beam, seed, 30, 20, false, Rac3ParticleTests.Muzzle, Rac3ParticleTests.End, 100);
        Compare(program, fixture, false, 1234, 78, 20, false, Rac3ParticleTests.Muzzle, Rac3ParticleTests.End, 100);
    }

    [SkippableFact]
    public void OriginalConstructorKeeps100TickLifeForTheRampThatExtendsGcType85To127()
    {
        string program = Oracle(); using var fixture = new Fixture();
        Compare(program, fixture, false, 1234, 30, 100, false, Rac3ParticleTests.Muzzle, Rac3ParticleTests.End, 100, constructor: true);
    }

    [SkippableFact]
    public void RepeatedRecipesHoleReuseTransformedPoseAndDepthFadeMatchOriginalInstructions()
    {
        string program = Oracle(); using var fixture = new Fixture();
        foreach (bool beam in new[] { false, true })
        {
            Compare(program, fixture, beam, 1234, 30, 25, true, Rac3ParticleTests.Muzzle, Rac3ParticleTests.End, 100);
            Compare(program, fixture, beam, 0, 60, 20, false, new(102, -43, 4, 1), new(86, -35, 8, 1), 100);
        }
        foreach (float distance in new[] { 1.25f, 1.5f, 127.5f })
            Compare(program, fixture, false, 1234, 60, 1, false, Rac3ParticleTests.Muzzle, Rac3ParticleTests.End, distance);
    }

    [SkippableFact]
    public void RealSaturatedAllocatorSkipsAngleAndPostSelectionButConsumesRecipeRng()
    {
        string program = Oracle(); using var fixture = new Fixture();
        foreach (bool beam in new[] { false, true })
        {
            string[] rows = fixture.Execute(program, beam, 0, 0, 30, false, true, Rac3ParticleTests.Muzzle, Rac3ParticleTests.End, 0);
            Assert.Equal($"F 0 2048 {(beam ? 17 : 4)}", Assert.Single(rows, row => row.StartsWith("F ", StringComparison.Ordinal)));
        }
    }

    private static void Compare(string program, Fixture fixture, bool beam, uint seed, int age, int frames, bool repeat, Vector4 muzzle, Vector4 end, float distance, bool constructor = false)
    {
        var rows = fixture.Execute(program, beam, seed, frames, age, repeat, false, muzzle, end, distance, constructor).Select(r => r.Split(' ')).ToArray();
        foreach (var row in rows.Where(r => r[0] == "D")) Assert.Equal(fixture.Assets.Definitions.Texture(int.Parse(row[1]), 0), int.Parse(row[2]));
        foreach (var row in rows.Where(r => r[0] == "T"))
        {
            var binding = fixture.Assets.NativeLayouts[int.Parse(row[1])].Bind(0x1000);
            Assert.Equal(binding.Palette, uint.Parse(row[2])); Assert.Equal(binding.Pixels, uint.Parse(row[3]));
        }
        var rng = Rac3ParticleRandom.FromSeed(seed); var session = new Rac3ParticleSession(fixture.Assets, rng);
        void Emit()
        {
            if (constructor) session.Spawn97(Rac3ParticleTests.Owner, new(muzzle, Vector3.Zero, 100, 0x123456, 0, 0, 127, 21000, 115500, 0));
            else if (beam) session.EmitShockCannonBeamGlow(Rac3ParticleTests.Owner, muzzle, end);
            else session.EmitShockCannonCharging(Rac3ParticleTests.Owner, age, muzzle);
        }
        Emit(); var draw = Rac3ParticleDraw.ReadVeldin(fixture.Level);
        var view = new Rac3ParticleDraw.View(new(muzzle.X, muzzle.Y, muzzle.Z - distance), Vector3.UnitZ, 1f / 420000, int.MaxValue);
        for (int tick = 0; tick <= frames; tick++)
        {
            string at = tick.ToString(CultureInfo.InvariantCulture); var frame = Assert.Single(rows, r => r[0] == "F" && r[1] == at);
            Assert.Equal(session.Count, int.Parse(frame[2])); Assert.Equal(rng.SamplesConsumed, long.Parse(frame[3]));
            var native = rows.Where(r => r[0] == "R" && r[1] == at).ToArray(); var managed = session.Live.ToArray();
            Assert.Equal(managed.Length, native.Length);
            for (int i = 0; i < managed.Length; i++)
            {
                Assert.Equal(managed[i].Slot, int.Parse(native[i][2]));
                byte[] expected = Words(native[i].Skip(3)), actual = managed[i].CopyNativeRecord();
                Assert.True(expected.SequenceEqual(actual), $"{(beam ? "beam" : "charge")} seed {seed} tick {tick} slot {i}: record byte {Difference(expected, actual):x}; native={Convert.ToHexString(expected)}, managed={Convert.ToHexString(actual)}");
            }
            var sprites = rows.Where(r => r[0] == "S" && r[1] == at).ToArray(); Assert.Equal(Math.Min(1, managed.Length), sprites.Length);
            if (sprites.Length != 0)
            {
                var p = managed[0]; var sprite = draw.Build(p, view, fixture.Assets.NativeLayouts[p.Texture], 0x100000);
                Assert.True(sprite.HasValue);
                byte[] expected = Words(sprites[0].Skip(3)), actual = sprite.Value.CopyNativeDescriptor();
                Assert.True(expected.SequenceEqual(actual), $"{(beam ? "beam" : "charge")} seed {seed} tick {tick}: sprite byte {Difference(expected, actual):x}; native={Convert.ToHexString(expected)}, managed={Convert.ToHexString(actual)}");
            }
            if (tick < frames) { session.Step(); if (repeat) Emit(); }
        }
    }
    private static int Difference(byte[] a, byte[] b) => Enumerable.Range(0, a.Length).FirstOrDefault(i => a[i] != b[i], -1);
    private static byte[] Words(IEnumerable<string> words)
    {
        uint[] ns = words.Select(s => uint.Parse(s, CultureInfo.InvariantCulture)).ToArray(); byte[] result = new byte[ns.Length * 4];
        for (int i = 0; i < ns.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(i * 4), ns[i]); return result;
    }
    private static string Oracle()
    {
        string? program = Environment.GetEnvironmentVariable("OBP_RAC3_PARTICLE_ORACLE");
        Skip.If(string.IsNullOrWhiteSpace(program), "Build tools/build-rac3-particles-oracle.ps1 and set OBP_RAC3_PARTICLE_ORACLE."); return program!;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly FileRandomAccessReader _disc;
        private readonly byte[] _ram = new byte[32 * 1024 * 1024];
        public Rac3LevelCore.OpenedLevel Level { get; }
        public Rac3ParticleAssets Assets { get; }
        public byte[] Read(int address, int size) => _ram.AsSpan(address, size).ToArray();
        public Fixture()
        {
            string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO"); Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_UYA_ISO not set");
            _disc = new(iso!); Assert.Equal(Rac3Authority.PrimaryIsoSizeBytes, _disc.Length);
            byte[] packed = _disc.Read(1463L * 2048, 771008);
            Assert.Equal("200068cb4186715521cdc1c20bc6c5a88054d0da54aa336ffdb2022633351c7d", Hash(packed));
            byte[] boot = WadLz.Decompress(packed.AsSpan(0x7080).ToArray()).Data;
            Assert.Equal("39ddc0784e58f7f94fd4c17341060a7d02a4afcd480bdf693c5ae5173c7d21f9", Hash(boot)); Load(boot);
            Level = Rac3LevelCore.Open(_disc, 1);
            byte[] overlay = Level.PrimaryReader.Read(Level.Core.Data.Overlay.Offset, Level.Core.Data.Overlay.Size);
            Assert.Equal("093fb8641cb2bf48073793f6cfac726cb7321654d285b848658f3e957df1dcec", Hash(overlay)); Load(overlay);
            Assert.Equal(0x4a600190u, BinaryPrimitives.ReadUInt32LittleEndian(_ram.AsSpan(0x10f1ac)));
            Level.Core.Index.CopyTo(_ram, 0x1000000); Assets = Rac3ParticleAssets.Read(Level.Core, Rac3ParticleTests.Source);
        }
        private void Load(byte[] image)
        {
            for (int at = 0; at + 16 <= image.Length;)
            {
                uint destination = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at));
                int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at + 4)));
                uint kind = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at + 8));
                Assert.InRange(size, 0, image.Length - at - 16);
                if (kind is 1 or 8) image.AsSpan(at + 16, size).CopyTo(_ram.AsSpan((int)destination));
                at += 16 + size;
            }
        }
        public string[] Execute(string program, bool beam, uint seed, int frames, int age, bool repeat, bool full, Vector4 muzzle, Vector4 end, float distance, bool constructor = false)
        {
            void V(int at, Vector4 v)
            {
                BinaryPrimitives.WriteSingleLittleEndian(_ram.AsSpan(at), v.X); BinaryPrimitives.WriteSingleLittleEndian(_ram.AsSpan(at + 4), v.Y);
                BinaryPrimitives.WriteSingleLittleEndian(_ram.AsSpan(at + 8), v.Z); BinaryPrimitives.WriteSingleLittleEndian(_ram.AsSpan(at + 12), v.W);
            }
            V(0x1700000, muzzle); V(0x1700010, end);
            string path = Path.Combine(Path.GetTempPath(), $"obp-uya-particles-{Guid.NewGuid():N}.bin");
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var stream = new FileStream(path, options)) stream.Write(_ram);
                var start = new ProcessStartInfo(program) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                start.ArgumentList.Add(path); start.ArgumentList.Add(constructor ? "constructor" : beam ? "beam" : "charge"); using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
                process.StandardInput.WriteLine(FormattableString.Invariant($"{seed} {frames} {age} {(repeat ? 1 : 0)} {(full ? 1 : 0)} {distance}")); process.StandardInput.Close();
                if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("UYA particle oracle timed out."); }
                Assert.True(process.ExitCode == 0, errors.GetAwaiter().GetResult());
                return output.GetAwaiter().GetResult().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            finally { File.Delete(path); }
        }
        public void Dispose() => _disc.Dispose();
    }
}
