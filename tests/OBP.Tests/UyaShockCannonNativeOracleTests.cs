using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;

namespace OBP.Tests;

public sealed class UyaShockCannonNativeOracleTests
{
    [SkippableFact]
    public void SoundDistanceGainMatchesOriginalQuantizationAndBothFalloffBranches()
    {
        foreach (bool quadratic in new[] { false, true })
            foreach (float distance in new[] { 0, 5, 6.5f, 17.5f, 23.33333f, 24.99f, 25, 50 })
            {
                var sound = new UyaShockCannonAssets.Sound(0, new(5, 25, 200, 1945, 0, 0, 0, 0x10, 0, 0), []);
                using var native = Execute("volume", FormattableString.Invariant($"{distance} 5 25 200 1945 16 {(quadratic ? 1 : 0)}\n"));
                Assert.Equal(native.RootElement.GetInt32(), sound.VolumeAtDistance(distance, quadratic));
            }
    }
    [SkippableFact]
    public void ManagedRingsMatchOriginalEeAndVuStripsAcrossFadeModesAndScroll()
    {
        foreach ((int rings, uint color, bool blend, float phase) in new[]
        {
            (8, 0x3a804020u, true, .15f), (6, 0x1dd20a40u, true, 6.95f),
            (-8, 0x3a801040u, true, .5f), (8, 0x3a804020u, false, 0f),
        })
        {
            using var native = Execute(null, $"{rings} {color} {(blend ? 1 : 0)} {phase.ToString(CultureInfo.InvariantCulture)}\n");
            var managed = UyaShockCannonBeam.EllipsoidStrips(Matrix4x4.Identity, rings, color, phase, blend);
            Assert.Equal(managed.Length, native.RootElement.GetArrayLength());
            for (int strip = 0; strip < managed.Length; strip++)
                for (int point = 0; point < managed[strip].Length; point++)
                {
                    var actual = managed[strip][point]; var original = native.RootElement[strip][point];
                    Assert.InRange(Math.Abs(actual.Position.X - original[0].GetDouble()), 0, .000002);
                    Assert.InRange(Math.Abs(actual.Position.Y - original[1].GetDouble()), 0, .000002);
                    Assert.InRange(Math.Abs(actual.Position.Z - original[2].GetDouble()), 0, .000002);
                    Assert.Equal(original[3].GetUInt32(), actual.Rgba);
                    Assert.InRange(Math.Abs(actual.Uv.X - original[4].GetDouble()), 0, .000002);
                    Assert.InRange(Math.Abs(actual.Uv.Y - original[5].GetDouble()), 0, .000002);
                }
        }
    }

    [SkippableFact]
    public void ManagedBeamLayersMatchOriginalAimMatrixRotationAndDrawArguments()
    {
        foreach (Vector3 aim in new[] { Vector3.UnitX, Vector3.UnitY, new(.6f, .8f, 0), Vector3.Normalize(new(.7f, .2f, .6f)), Vector3.UnitZ, -Vector3.UnitZ })
            CompareBeam(aim);
    }

    private static void CompareBeam(Vector3 aim)
    {
        using var native = Execute("beam", FormattableString.Invariant($"{aim.X} {aim.Y} {aim.Z}\n"));
        var session = new UyaShockCannonSession(); session.Equip();
        UyaShockCannonFrame frame = session.Advance(new(true));
        for (int i = 1; i < 79; i++) frame = session.Advance(new(true));
        var layers = UyaShockCannonBeam.Build(frame, UyaShockCannonBeam.Basis.FromNativeAim(aim, new(2, 3, 4)),
            new UyaShockCannonBeam.Scroll().Advance());
        Assert.Equal(layers.Count, native.RootElement.GetArrayLength());
        for (int index = 0; index < layers.Count; index++)
        {
            var layer = layers[index]; var original = native.RootElement[index];
            Assert.Equal(original.GetProperty("texture").GetInt32(), layer.FxTextureId);
            Assert.Equal(original.GetProperty("color").GetUInt32(), layer.NativeRgba);
            Assert.Equal(original.GetProperty("rings").GetInt32(), layer.RingCount);
            float[] m = original.GetProperty("matrix").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            var matrix = new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
            var expected = UyaShockCannonBeam.EllipsoidStrips(matrix, layer.RingCount, layer.NativeRgba,
                original.GetProperty("phase").GetSingle()).SelectMany(s => s).ToArray();
            for (int point = 0; point < expected.Length; point++)
            {
                var p = expected[point].Position;
                Assert.InRange(Math.Abs(p.X - layer.Mesh.Positions[point * 3]), 0, .00002);
                Assert.InRange(Math.Abs(p.Z - layer.Mesh.Positions[point * 3 + 1]), 0, .00002);
                Assert.InRange(Math.Abs(p.Y - layer.Mesh.Positions[point * 3 + 2]), 0, .00002);
            }
        }
    }

    private static JsonDocument Execute(string? mode, string? input = null)
    {
        string? program = Environment.GetEnvironmentVariable("OBP_UYA_CANNON_ORACLE"), ram = Environment.GetEnvironmentVariable("OBP_UYA_CANNON_RAM");
        Skip.If(string.IsNullOrWhiteSpace(program) || string.IsNullOrWhiteSpace(ram), "Run tools/uya-shock-cannon-native-trace.py for original-instruction oracle.");
        var start = new ProcessStartInfo(program!) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(ram!); if (mode is not null) start.ArgumentList.Add(mode);
        using var process = Process.Start(start)!;
        process.StandardInput.Write(input); process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Native beam oracle did not finish."); }
        Assert.True(process.ExitCode == 0, errors.GetAwaiter().GetResult());
        return JsonDocument.Parse(output.GetAwaiter().GetResult());
    }
}
