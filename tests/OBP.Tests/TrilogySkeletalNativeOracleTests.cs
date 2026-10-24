using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using OBP.PS2.Animation;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed partial class TrilogySkeletalRetailTests
{
    [SkippableTheory]
    [InlineData("gc")]
    [InlineData("uya")]
    public async Task OriginalInstructionsAgreeWithEveryPackedFrameAndSampledHandMatrices(string game)
    {
        string? program = Environment.GetEnvironmentVariable("OBP_SKELETON_ORACLE");
        Skip.If(string.IsNullOrWhiteSpace(program), "Run tools/trilogy-skeleton-native-trace.py for original-instruction comparison.");
        using var data = new Sources();
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "OneBigPackage.sln")))
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Cannot locate the repository.");
        string directory = Path.Combine(root, "captures", "trilogy-skeleton-oracle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            data.WriteOracleInputs(directory, game);
            var (index, assets, table, _) = data.Raw(game);
            var frames = new List<(int Sequence, int Frame, Ps2SkeletalAnimation.Frame Value)>();
            int[] starts = Enumerable.Range(0, 256).Select(s => I32(index, table + s * 4)).Where(at => at > 0).Order().ToArray();
            for (int s = 0; s < 256; s++)
            {
                int at = I32(index, table + s * 4); if (at == 0) continue;
                int end = starts.FirstOrDefault(next => next > at, assets.Length);
                var sequence = Ps2SkeletalAnimation.ReadSequence(assets.AsSpan(at, end - at), 0, 111, s, true);
                for (int f = 0; f < sequence.Frames.Count; f++) frames.Add((s, f, sequence.Frames[f]));
            }
            byte[] native = await Execute(null, frames.Select(f => (f.Sequence, f.Frame)));
            Assert.Equal(frames.Count * 2048, native.Length);
            for (int i = 0; i < frames.Count; i++)
            {
                int at = i * 2048; var frame = frames[i].Value;
                Assert.Equal(frame.TransitionRate, BitConverter.ToSingle(native, at));
                Assert.Equal(frame.TimelineUnits, BinaryPrimitives.ReadInt16LittleEndian(native.AsSpan(at + 4)));
                for (int joint = 0; joint < 111; joint++)
                {
                    var q = frame.Rotations[joint]; int word = at + 16 + joint * 8;
                    Assert.Equal(q.X, Short(word)); Assert.Equal(q.Y, Short(word + 2)); Assert.Equal(q.Z, Short(word + 4));
                    Assert.InRange(Math.Abs(q.W - Short(word + 6)), 0, 1);
                }
                int cursor = at + 16 + 111 * 8;
                foreach (var channel in frame.Scales.Concat(frame.Translations))
                {
                    Assert.Equal(channel.X, Short(cursor)); Assert.Equal(channel.Y, Short(cursor + 2)); Assert.Equal(channel.Z, Short(cursor + 4));
                    Assert.Equal(channel.JointTag, BinaryPrimitives.ReadUInt16LittleEndian(native.AsSpan(cursor + 6))); cursor += 8;
                }
            }
            var source = game == "gc" ? data.Gc : data.Uya;
            _output.WriteLine($"{game}: {frames.Count} frames; XYZ and auxiliary vectors exact; W differs by at most one fixed-point unit.");
            foreach (float weight in new[] { 0f, .25f, .5f, 1f })
            {
                bool carry = weight > 0;
                var cases = source.Body.Values.Where(c => !carry || c.SequenceId < 23)
                    .SelectMany(c => new[] { 0, c.Frames.Count / 2, c.Frames.Count - 1 }.Distinct().Select(f => (Sequence: c.SequenceId, Frame: f)))
                    .Where(c => !carry || source.Arms.Take(source.ArmCount).All(a => a.TryGetValue(c.Sequence, out var clip) && c.Frame < clip.Frames.Count)).ToArray();
                byte[] matrices = await Execute(carry ? "carry" : "pose", cases, weight);
                Assert.Equal(cases.Length * 31 * 64, matrices.Length);
                var sampler = new PlayerSkeletalSampler(source.Skeleton);
                double maxLinear = 0, maxOrigin = 0; string worst = "";
                for (int i = 0; i < cases.Length; i++)
                {
                    var (s, f) = cases[i]; var sample = new PlayerSkeletalSample(f, f, 0);
                    sampler.Reset(source.Body[s], sample);
                    if (carry) for (int side = 0; side < source.ArmCount; side++) sampler.Apply(source.Arms[side][s], sample, weight);
                    sampler.Skin();
                    for (int semantic = 0; semantic < 31; semantic++)
                    {
                        var m = sampler.JointMatrices[source.Skeleton.Attachments[semantic].Joint];
                        float[] values = [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33];
                        int[] words = [0, 1, 2, 4, 5, 6, 8, 9, 10]; int at = (i * 31 + semantic) * 64;
                        for (int k = 0; k < values.Length; k++) maxLinear = Math.Max(maxLinear, Math.Abs(values[k] - BitConverter.ToSingle(matrices, at + words[k] * 4)));
                        var origin = new Vector3(BitConverter.ToSingle(matrices, at + 48), BitConverter.ToSingle(matrices, at + 52), BitConverter.ToSingle(matrices, at + 56));
                        double error = Vector3.Distance(m.Translation, origin);
                        if (error > maxOrigin) { maxOrigin = error; worst = $"sequence{s} frame{f} semantic{semantic}"; }
                    }
                }
                Assert.True(maxLinear <= .0003 && maxOrigin <= .0002,
                    $"{game}/{(carry ? "carry" : "pose")}: {cases.Length} poses, maximum basis error {maxLinear:G9}, maximum origin error {maxOrigin:G9}, {worst}.");
                _output.WriteLine($"{game}: carry weight{weight}, {cases.Length} poses, max basis error{maxLinear:G9}, max origin error{maxOrigin:G9}.");
            }
            short Short(int at) => BinaryPrimitives.ReadInt16LittleEndian(native.AsSpan(at));
        }
        finally
        {
            // The exact directory was created inside ignored captures above.
            Directory.Delete(directory, recursive: true);
        }

        async Task<byte[]> Execute(string? mode, IEnumerable<(int Sequence, int Frame)> cases, float weight = 1)
        {
            var start = new ProcessStartInfo(program!) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(directory); start.ArgumentList.Add(game);
            if (mode is not null) { start.ArgumentList.Add(mode); start.ArgumentList.Add(weight.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            using var process = Process.Start(start)!; using var output = new MemoryStream();
            var reading = process.StandardOutput.BaseStream.CopyToAsync(output); var errors = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(string.Join('\n', cases.Select(c => $"{c.Sequence} {c.Frame}")) + "\n");
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); throw; }
            await reading;
            Assert.True(process.ExitCode == 0, await errors);
            return output.ToArray();
        }
    }
}
