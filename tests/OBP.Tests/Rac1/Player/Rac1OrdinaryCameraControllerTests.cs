using System.Text.Json;
using System.Text.Json.Nodes;
using OBP.Core.Math;
using OBP.RAC1.Camera;

namespace OBP.Tests;

public sealed class Rac1OrdinaryCameraControllerTests
{
    public static IEnumerable<object[]> NativeScenarios() => FollowPlans.Select(plan => new object[] { plan.Name });

    [Theory]
    [MemberData(nameof(NativeScenarios))]
    public void FollowPipeline_MatchesOriginalMipsExecution(string scenario)
    {
        using var doc = ReadTrace();
        var c = doc.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("name").GetString() == scenario);
        var options = c.GetProperty("options").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var camera = new Rac1OrdinaryCameraController();
        camera.Reset(Vec3.Zero, 0d);
        Vec3 player = Vec3.Zero;
        int frame = 0;
        var samples = c.GetProperty("samples").EnumerateArray().ToDictionary(s => s.GetProperty("frame").GetInt32());
        foreach (var segment in c.GetProperty("segments").EnumerateArray())
        {
            var move = Vector(segment.GetProperty("move"));
            var stick = segment.GetProperty("stick").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            for (int i = 0; i < segment.GetProperty("frames").GetInt32(); ++i)
            {
                player += move;
                var state = camera.Step(new(player, stick[0], stick[1], default,
                    Options: new((Rac1CameraDirection)options[0], (Rac1CameraDirection)options[1], (Rac1CameraSpeed)options[2])));
                if (!samples.TryGetValue(++frame, out var expected)) continue;
                // Independent original-code output. The initial original constructor/profile
                // transition and PS2 math rounding account for up to .014 world units.
                Near(Vector(expected.GetProperty("eye")), state.Framing.EyeNativeZUp, .016d, $"{scenario}/{frame} eye");
                var runtimeForward = state.ToRuntimeState().Forward;
                Near(Vector(expected.GetProperty("forward")), new(runtimeForward.X, runtimeForward.Z, runtimeForward.Y), .002d, $"{scenario}/{frame} view");
                Near(Vector(expected.GetProperty("desiredOffset")), camera.CaptureSnapshot().Pipeline!.DesiredOffset, .016d, $"{scenario}/{frame} desired offset");
                Assert.InRange(Math.Abs(camera.ManualPitchState - expected.GetProperty("manual")[1].GetDouble()), 0d, .001d);
            }
        }
    }

    public static IEnumerable<object[]> NativeReleaseScenarios() => ReleasePlans.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(NativeReleaseScenarios))]
    public void ClearLineRecovery_MatchesOriginalCollisionStageIncludingSignedInputGate(string scenario)
    {
        using var doc = ReadTrace();
        var c = doc.RootElement.GetProperty("obstructionRelease").GetProperty("cases").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == scenario);
        double correction = c.GetProperty("initialCorrection").GetDouble();
        var stick = c.GetProperty("stick").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var camera = new Rac1OrdinaryCameraController();
        camera.Reset(Vec3.Zero, 0d);
        var snapshot = camera.CaptureSnapshot();
        var offset = new Vec3(-(Rac1OrdinaryCameraController.OrdinaryPreferredRadius - correction), 0, 0);
        camera.RestoreSnapshot(snapshot with
        {
            CurrentRadius = Rac1OrdinaryCameraController.OrdinaryPreferredRadius - correction,
            ObstructionCorrection = correction,
            ObstructionReleaseTicks = c.GetProperty("initialTimer").GetInt32(),
            Pipeline = snapshot.Pipeline! with
            {
                DesiredOffset = offset,
                ActualOffset = offset,
                PreviousDesiredEye = offset + new Vec3(0, 0, Rac1OrdinaryCameraController.OrdinaryEyeHeight),
            },
        });
        var geometry = new EmptyGeometry();
        var samples = c.GetProperty("samples").EnumerateArray().ToDictionary(s => s.GetProperty("frame").GetInt32());
        for (int frame = 1; frame <= c.GetProperty("frames").GetInt32(); ++frame)
        {
            camera.Step(new(Vec3.Zero, stick[0], stick[1], default), geometry);
            if (!samples.TryGetValue(frame, out var expected)) continue;
            // Original float cosine approximation differs from the host's double
            // recurrence by up to .000038 world units over these release traces.
            Assert.True(Math.Abs(camera.ObstructionCorrection - expected.GetProperty("correction").GetDouble()) <= .00005d,
                $"{scenario}/{frame}: correction {camera.ObstructionCorrection}, expected {expected.GetProperty("correction")}");
            Assert.Equal(expected.GetProperty("timer").GetInt32(), camera.CaptureSnapshot().ObstructionReleaseTicks);
        }
    }

    [Fact]
    public void NativeNormalAndInvertedOptions_AreIndependentAndUseBootDefaults()
    {
        var defaults = Rac1CameraOptions.Default;
        Assert.Equal(1, (int)defaults.Horizontal);
        Assert.Equal(1, (int)defaults.Vertical);
        Assert.Equal(1, (int)defaults.Speed);
        Assert.Equal((.6d, -.8d), defaults.SelectManualInput(.6d, -.8d));
        Assert.Equal((-.6d, -.8d), (defaults with { Horizontal = Rac1CameraDirection.Inverted }).SelectManualInput(.6d, -.8d));
        Assert.Equal((.6d, .8d), (defaults with { Vertical = Rac1CameraDirection.Inverted }).SelectManualInput(.6d, -.8d));
        Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with { Speed = (Rac1CameraSpeed)3 }).Validate());
    }

    [Fact]
    public void OrdinaryLens_UsesNativeProjectionInsteadOfGodotDefault()
    {
        var camera = new Rac1OrdinaryCameraController();
        var state = camera.Step(new(Vec3.Zero, 0, 0, default)).ToRuntimeState();
        Assert.InRange(state.VerticalFieldOfViewRadians!.Value * 180d / Math.PI, 52.03d, 52.06d);
        Assert.Throws<ArgumentOutOfRangeException>(() => (state with { VerticalFieldOfViewRadians = double.NaN }).Validate());
    }

    [Fact]
    public void NeutralCamera_DoesNotInventAlwaysOnRecenter()
    {
        var camera = new Rac1OrdinaryCameraController();
        camera.Reset(new(10, 20, 30), 1.25d);
        for (int i = 0; i < 600; ++i) camera.Step(new(new(10, 20, 30), 0, 0, default));
        Assert.Equal(1.25d, camera.ControlHeading, 10);
    }

    [Fact]
    public void ReleasingYaw_ClearsManualCommandWhileActualEyeContinuesTurning()
    {
        var camera = new Rac1OrdinaryCameraController();
        for (int i = 0; i < 36; ++i) camera.Step(new(Vec3.Zero, 1, 0, default));
        double before = camera.ControlHeading;
        var state = camera.Step(new(Vec3.Zero, 0, 0, default));
        Assert.Equal(0d, state.Control.HeadingStep); // Separate directional-pad producer, not offset yaw velocity.
        Assert.Equal(0d, camera.ManualYawState);
        Assert.True(camera.ControlHeading > before);
    }

    [Fact]
    public void VerticalRelease_UsesSlowerNativeLimitAndRetainsMotionDuringRelease()
    {
        var camera = new Rac1OrdinaryCameraController();
        for (int i = 0; i < 60; ++i) camera.Step(new(Vec3.Zero, 0, 1, default));
        var atRelease = camera.CaptureSnapshot();
        var first = camera.Step(new(Vec3.Zero, 0, 0, default));
        Assert.InRange(camera.ManualPitchState, .989d, .991d);
        Assert.True(first.Framing.EyeNativeZUp.Z > atRelease.Pipeline!.PositionBase.Z + 2d + atRelease.Pipeline.ActualOffset.Z);
        for (int i = 0; i < 180; ++i) camera.Step(new(Vec3.Zero, 0, 0, default));
        Assert.Equal(0d, camera.ManualPitchState);
        Assert.InRange(camera.VerticalOrbitRadians, -.001d, .001d);
    }

    [Fact]
    public void Snapshot_RetainsAllSpringsAcrossResume()
    {
        var first = new Rac1OrdinaryCameraController();
        for (int i = 0; i < 30; ++i) first.Step(new(new(i * .1, i * .02, i * .03), .7, -.8, default));
        var second = new Rac1OrdinaryCameraController();
        second.RestoreSnapshot(first.CaptureSnapshot());
        for (int i = 30; i < 100; ++i)
        {
            var input = new Rac1OrdinaryCameraController.Input(new(i * .1, i * .02, 0), 0, 0, default);
            Assert.Equal(first.Step(input), second.Step(input));
        }
    }

    [Fact]
    public void LegacyBookmark_WithoutPipelineExtensionStillLoads()
    {
        var first = new Rac1OrdinaryCameraController(); first.Reset(new(1, 2, 3), .4d);
        var second = new Rac1OrdinaryCameraController();
        second.RestoreSnapshot(first.CaptureSnapshot() with { Pipeline = null });
        Assert.NotNull(second.Step(new(new(1, 2, 3), 0, 0, default)).Validate());
        Assert.Equal(.4d, second.ControlHeading, 10);
    }

    [Fact]
    public void SphereProbes_KeepCameraClearOfWallAndFloorWithBoundedWork()
    {
        var camera = new Rac1OrdinaryCameraController();
        var geometry = new Planes();
        for (int frame = 0; frame < 240; ++frame)
        {
            geometry.Spheres = 0;
            var state = camera.Step(new(Vec3.Zero, 0, frame < 120 ? -1 : 0, default), geometry);
            Assert.InRange(geometry.Spheres, 1, 30);
            Assert.InRange(state.EffectiveRadius, 1.5d, 6d);
            if (frame > 100)
            {
                Assert.True(state.Framing.EyeNativeZUp.X > -3d);
                Assert.True(state.Framing.EyeNativeZUp.Z > 0d);
            }
        }
        Assert.True(camera.ObstructionCorrection > 1d);
    }

    private sealed class Planes : IRac1CameraGeometry
    {
        public int Spheres;
        public Rac1CameraContact? Sphere(Vec3 p, double radius)
        {
            ++Spheres;
            if (p.X - radius < -3) return new(new(-3, p.Y, p.Z), new(-3 + radius, p.Y, p.Z));
            if (p.Z < radius) return new(new(p.X, p.Y, 0), new(p.X, p.Y, radius));
            return null;
        }
        public Vec3? Segment(Vec3 a, Vec3 b) => b.Z < 0 && a.Z > 0 ? new(a.X, a.Y, 0) : null;
    }

    [Theory]
    [InlineData(-2.073779582977295)]
    [InlineData(.523598775598299)]
    [InlineData(2.842167616)]
    public void ArbitraryHeadingsAndMovingTargets_DoNotProduceNegativeClearanceFromRounding(double heading)
    {
        var camera = new Rac1OrdinaryCameraController();
        camera.Reset(new(132.09, 115.48, 31.506617), heading);
        var empty = new EmptyGeometry();
        for (int frame = 0; frame < 1200; ++frame)
        {
            var player = new Vec3(132.09 + Math.Sin(frame * .01), 115.48 + frame * .01, 31.506617 + Math.Sin(frame * .03));
            var state = camera.Step(new(player, frame % 120 < 60 ? .7 : -.7, 0, default), empty);
            Assert.InRange(state.EffectiveRadius, 1.5d, 6d);
        }
    }

    private sealed class EmptyGeometry : IRac1CameraGeometry
    {
        public Rac1CameraContact? Sphere(Vec3 p, double radius) => null;
        public Vec3? Segment(Vec3 a, Vec3 b) => null;
    }

    // Preserve discovery independently of the observations under test. Removing a
    // witness must fail its parameter case instead of removing that case too.
    private static readonly (string Name, int Frames)[] FollowPlans =
    [
        ("horizontal-negative-normal", 240), ("horizontal-negative-inverted", 240),
        ("horizontal-positive-normal", 240), ("horizontal-positive-inverted", 240),
        ("vertical-negative-normal", 240), ("vertical-negative-inverted", 240),
        ("vertical-positive-normal", 240), ("vertical-positive-inverted", 240),
        ("horizontal-speed-0", 136), ("horizontal-speed-2", 136),
        ("partial-stick-diagonal", 228), ("sideways-follow", 150),
        ("forward-back-follow", 210), ("jump-follow", 160),
    ];
    private static readonly string[] ReleasePlans =
    [
        "neutral", "horizontal-negative", "horizontal-positive", "vertical-up", "vertical-down",
        "native-negative-diagonal", "native-positive-diagonal", "native-positive-x-negative-y",
        "native-negative-x-positive-y", "horizontal-below-threshold", "horizontal-above-threshold",
        "vertical-below-threshold", "vertical-above-threshold", "positive-input-after-release-started",
    ];

    [Theory]
    [InlineData("complete", "")]
    [InlineData("follow", "missing-case")]
    [InlineData("follow", "duplicate-case")]
    [InlineData("follow", "missing-samples")]
    [InlineData("follow", "missing-checkpoint")]
    [InlineData("follow", "incomplete-frames")]
    [InlineData("release", "missing-case")]
    [InlineData("release", "duplicate-case")]
    [InlineData("release", "missing-samples")]
    [InlineData("release", "missing-checkpoint")]
    [InlineData("release", "incomplete-frames")]
    public void TraceRequiresEveryScenarioAndCheckpoint(string cohort, string fault)
    {
        var witness = JsonNode.Parse(File.ReadAllText(TracePath))!;
        if (cohort != "complete")
        {
            var cases = (cohort == "follow" ? witness["cases"] : witness["obstructionRelease"]!["cases"])!.AsArray();
            switch (fault)
            {
                case "missing-case": cases.RemoveAt(0); break;
                case "duplicate-case": cases[1] = cases[0]!.DeepClone(); break;
                case "missing-samples": cases[0]!["samples"]!.AsArray().Clear(); break;
                case "missing-checkpoint": cases[0]!["samples"]!.AsArray().RemoveAt(0); break;
                case "incomplete-frames" when cohort == "follow": cases[0]!["segments"]![0]!["frames"] = 0; break;
                case "incomplete-frames": cases[0]!["frames"] = 0; break;
                default: throw new ArgumentOutOfRangeException(nameof(fault), fault, "Unknown trace fault.");
            }
        }
        void Check() => RequireTraceCohorts(JsonSerializer.SerializeToElement(witness));
        if (cohort == "complete") Check();
        else Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);
    }

    private static string TracePath => Path.Combine(RepoPaths.Root, "research/generated/rac1-camera-native-traces.json");
    private static JsonDocument ReadTrace()
    {
        var trace = JsonDocument.Parse(File.ReadAllText(TracePath));
        try { RequireTraceCohorts(trace.RootElement); return trace; }
        catch { trace.Dispose(); throw; }
    }

    private static void RequireTraceCohorts(JsonElement trace)
    {
        // Input plan names, lengths and checkpoint schedule are independently
        // retained in tools/rac1-camera-native-trace.py.
        var follow = trace.GetProperty("cases").EnumerateArray().ToArray();
        var release = trace.GetProperty("obstructionRelease").GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(FollowPlans.Select(plan => plan.Name).Order(StringComparer.Ordinal),
            follow.Select(item => item.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
        Assert.Equal(ReleasePlans.Order(StringComparer.Ordinal),
            release.Select(item => item.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
        foreach (var item in follow)
        {
            int frames = FollowPlans.Single(plan => plan.Name == item.GetProperty("name").GetString()).Frames;
            var segments = item.GetProperty("segments").EnumerateArray().ToArray();
            Assert.NotEmpty(segments);
            Assert.All(segments, segment => Assert.True(segment.GetProperty("frames").GetInt32() > 0));
            Assert.Equal(frames, segments.Sum(segment => segment.GetProperty("frames").GetInt32()));
            RequireSamples(item, frames, [1, 6, 12, 24, 36, 48, 60, 72, 90, 120, 180, frames]);
        }
        foreach (var item in release)
        {
            Assert.Equal(240, item.GetProperty("frames").GetInt32());
            RequireSamples(item, 240, [1, 6, 20, 60, 179, 180, 181, 200, 240]);
        }

        static void RequireSamples(JsonElement item, int frames, int[] checkpoints) =>
            Assert.Equal(checkpoints.Where(frame => frame <= frames).Distinct().Order(),
                item.GetProperty("samples").EnumerateArray().Select(sample => sample.GetProperty("frame").GetInt32()).Order());
    }
    private static Vec3 Vector(JsonElement v) => new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
    private static void Near(Vec3 expected, Vec3 actual, double tolerance, string context) => Assert.True(
        Math.Abs(expected.X - actual.X) <= tolerance && Math.Abs(expected.Y - actual.Y) <= tolerance && Math.Abs(expected.Z - actual.Z) <= tolerance,
        $"{context}: expected {expected}, actual {actual}, tolerance {tolerance}");
}
