using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Player;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1VeldinEnemyRetailTests
{
    private static string WitnessPath => Path.Combine(RepoPaths.Root, "research", "generated", "rac1-veldin-enemies.json");

    private static void RequireWitnessCohorts(JsonElement report)
    {
        var blocks = report.GetProperty("blocks").EnumerateObject().ToArray();
        Assert.Equal(22, blocks.Length);
        Assert.Equal(22, blocks.Select(block => (Convert.ToInt32(block.Value.GetProperty("start").GetString(), 16),
            Convert.ToInt32(block.Value.GetProperty("end").GetString(), 16))).Distinct().Count());
        var signatures = report.GetProperty("signatures").EnumerateArray().ToArray();
        Assert.Equal(38, signatures.Length);
        Assert.Equal(38, signatures.Select(row => Convert.ToInt32(row.GetProperty("address").GetString(), 16)).Distinct().Count());
    }

    [Theory]
    [InlineData("blocks", false)]
    [InlineData("blocks", true)]
    [InlineData("signatures", false)]
    [InlineData("signatures", true)]
    public void WitnessRequiresCompleteDistinctObservationCohorts(string cohort, bool duplicate)
    {
        using var witness = JsonDocument.Parse(File.ReadAllText(WitnessPath));
        RequireWitnessCohorts(witness.RootElement);
        var root = JsonNode.Parse(witness.RootElement.GetRawText())!;
        if (cohort == "blocks")
        {
            if (duplicate) root[cohort]!["class749Damage"] = root[cohort]!["class749Update"]!.DeepClone();
            else root[cohort] = new JsonObject();
        }
        else
        {
            if (duplicate) root[cohort]![1] = root[cohort]![0]!.DeepClone();
            else root[cohort] = new JsonArray();
        }
        using var incomplete = JsonDocument.Parse(root.ToJsonString());
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => RequireWitnessCohorts(incomplete.RootElement));
    }

    [SkippableFact]
    public void ExecutableSelectorsSteeringHealthAndOutgoingDamageBridgeMatchAuthority()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay();
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "research", "generated", "rac1-veldin-enemies.json")));
        RequireWitnessCohorts(report.RootElement);
        foreach (var block in report.RootElement.GetProperty("blocks").EnumerateObject())
        {
            int start = Convert.ToInt32(block.Value.GetProperty("start").GetString(), 16), end = Convert.ToInt32(block.Value.GetProperty("end").GetString(), 16);
            Assert.Equal(block.Value.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(read(start, end - start))).ToLowerInvariant());
        }
        foreach (var signature in report.RootElement.GetProperty("signatures").EnumerateArray())
        {
            int address = Convert.ToInt32(signature.GetProperty("address").GetString(), 16);
            Assert.Equal(Convert.ToUInt32(signature.GetProperty("word").GetString(), 16), BinaryPrimitives.ReadUInt32LittleEndian(read(address, 4)));
        }
        Assert.Equal(0x3c014000u, BinaryPrimitives.ReadUInt32LittleEndian(read(0x2e0c94, 4)));
        Assert.Equal(0x44810000u, BinaryPrimitives.ReadUInt32LittleEndian(read(0x2e0c98, 4)));
        float Float(int address) => BitConverter.ToSingle(read(address, 4));
        var frog = Rac1MobyReaction.For(749, true);
        Assert.Equal(frog.Gravity, Float(0x161970) / 3600);
        Assert.Equal(frog.Drag, Float(0x161974) / 3600);
        Assert.Equal(frog.PlanarLaunch, Float(0x161978) / 60);
        Assert.Equal(frog.VerticalLaunch, Float(0x16197c) / 60);
        var robot = Rac1MobyReaction.For(1440, true);
        Assert.Equal(robot.Gravity, Float(0x161bd4) / 3600);
        Assert.Equal(robot.Drag, Float(0x161bd8) / 3600);
        Assert.Equal(robot.PlanarLaunch, Float(0x161bdc) / 60);
        Assert.Equal(robot.VerticalLaunch, Float(0x161be0) / 60);
        Assert.Equal(Rac1RatchetNanotechSession.EnemyHitProtectionTicks,
            BinaryPrimitives.ReadInt32LittleEndian(read(0x22665c, 4)) & 0xffff);
    }

    [SkippableFact]
    public void OpeningPopulationPathsAllPosesSocketsAndVariableAttackClockAreAdmitted()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(path!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == 0);
        var core = Rac1LevelCore.Open(reader, level);
        var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var paths = Rac1Paths.Read(gameplay);
        var placements = Rac1Instances.Parse(gameplay).MobyInstances;
        Assert.Equal(42, paths.Count);
        Assert.Equal(13, paths[0].Length);
        Assert.Equal(147.094879f, paths[0][0].X);
        Assert.Equal(29.682873f, paths[0][0].Z);
        var world = Rac1WorldImport.Build(reader, 0);
        var classes = Rac1StaticClasses.Read(core);
        var robots = new Rac1Class1440Session(new(), paths);
        foreach (var source in world.DynamicObjects!.Where(source => source.NativeClassId == 1440))
        {
            var registered = robots.Register(source, RuntimeEntityState.FromAuthored(source));
            Assert.Equal(2, registered.Health);
            Assert.Equal(22, Rac1EnemyMotion.RewardCentre(source));
            var placement = placements.Single(item => item.Index == source.InstanceIndex);
            int pathIndex = BinaryPrimitives.ReadInt32LittleEndian(placement.PVar!.AsSpan(0x1b8));
            if (pathIndex < 0) continue;
            var route = paths[pathIndex];
            var matrix = registered.Instance.EntityState.Presentation.Transform.Matrix;
            var position = new Vector3((float)matrix[12], (float)matrix[14], (float)matrix[13]);
            Assert.Equal(route[0], position);
            Assert.Equal(source.Transform.Matrix[..12], matrix[..12]);
            var region = robots.ActivationRegion(source.InstanceIndex);
            Vector3 trigger = region.Aggregate(Vector3.Zero, (sum, point) => sum + point) / region.Count;
            Assert.True(Rac1Paths.ContainsPlanar(region, trigger));
            Assert.Equal(2, robots.Step(source.InstanceIndex, new(position, trigger)).NativeState);
            var motion = new Rac1EnemyMotion(1440, placement.Rotation.Z);
            float travelled = 0;
            for (int tick = 0; tick < 1200 && robots.Get(source.InstanceIndex).NativeState == 2; tick++)
            {
                var next = robots.Step(source.InstanceIndex, new(position, new(10000, 10000, 0), StepLength: motion.StepLength));
                Vector3 delta = motion.Step(position, next.Destination ?? next.LookAt, next.Destination, false, true,
                    distanceToPathEnd: next.DistanceToPathEnd);
                travelled += delta.Length(); position += delta;
                Assert.True(DistanceToRoute(route, position) < .4f, $"i{source.InstanceIndex} left its entrance route at tick{tick}");
            }
            Assert.Equal(3, robots.Get(source.InstanceIndex).NativeState);
            Assert.True(Vector3.Distance(position, route[^1]) < .4f, $"i{source.InstanceIndex} stopped before its entrance endpoint");
            float length = route.Zip(route.Skip(1), Vector3.Distance).Sum();
            Assert.InRange(travelled, length - .5f, length + .4f);
        }
        Assert.Equal(9, robots.RegisteredCount);
        foreach (int id in new[] { 749, 1440 })
        {
            var source = world.DynamicObjects!.First(source => source.NativeClassId == id);
            var cls = classes.Mobies[id];
            var assets = Rac1MobyAnimationAssets.Bake(cls, source.Meshes, core.Assets.AsSpan(cls.AssetOffset, cls.AssetLength));
            Assert.Equal(id == 749 ? 8 : 13, assets.Clips.Count);
            Assert.NotEmpty(source.Meshes);
            Assert.Equal(cls.Sequences.Where(slot => slot.Value is not null).Select(slot => slot.Index).Order(), assets.Clips.Keys.Order());
            Assert.Equal(assets.Clips.Keys.Order(), assets.Clips.Values.Select(clip => clip.Sequence).Order());
            Assert.Equal(assets.Clips.Values.Select(clip => clip.Id).Order(), assets.Presentation.Clips.Select(clip => clip.Id).Order());
            foreach (var clip in assets.Presentation.Clips)
            {
                var native = Assert.Single(assets.Clips.Values, candidate => candidate.Id == clip.Id);
                int frames = cls.Sequences.Single(slot => slot.Index == native.Sequence).Value!.Frames.Count;
                Assert.True(frames > 0);
                Assert.Equal(Enumerable.Range(0, source.Meshes.Count), clip.Surfaces.Select(surface => surface.SurfaceIndex).Order());
                Assert.Equal(frames, clip.FrameDurationsSeconds.Count);
                foreach (var surface in clip.Surfaces)
                {
                    Assert.Equal(frames, surface.LocalFrames.Count);
                    foreach (var frame in surface.LocalFrames)
                    {
                        Assert.Equal(source.Meshes[surface.SurfaceIndex].Positions.Length, frame.Length);
                        Assert.All(frame, value => Assert.True(double.IsFinite(value) && Math.Abs(value) < 10));
                    }
                }
            }
            Assert.All(assets.Clips.Values, clip =>
            {
                int frames = cls.Sequences.Single(slot => slot.Index == clip.Sequence).Value!.Frames.Count;
                Assert.Equal(frames, clip.Rates.Length);
                Assert.Equal(frames, clip.Positions.Length);
                Assert.Equal(frames, clip.ContactSocket.Length);
                Assert.All(clip.ContactSocket, socket => Assert.True(float.IsFinite(socket.X) && socket.Length() < 10));
            });
            if (id != 749) continue;
            Assert.Equal(7, Rac1EnemyMotion.RewardCentre(source));
            var clock = new Rac1MobySequenceClock(assets, 5);
            clock.Tick(); // Selector blend1 precedes source phase advance.
            int markerTick = 0, wrapTick = 0;
            for (int tick = 1; tick <= 110; tick++)
            {
                clock.Tick();
                if (markerTick == 0 && clock.Position >= 34) markerTick = tick;
                if (clock.Wrapped) { wrapTick = tick; break; }
            }
            Assert.Equal(68, markerTick);
            Assert.Equal(102, wrapTick);
        }
    }

    private static float DistanceToRoute(IReadOnlyList<Vector3> route, Vector3 point)
    {
        float closest = float.PositiveInfinity;
        for (int i = 1; i < route.Count; i++)
        {
            Vector3 segment = route[i] - route[i - 1];
            float fraction = segment.LengthSquared() == 0 ? 0 : Math.Clamp(Vector3.Dot(point - route[i - 1], segment) / segment.LengthSquared(), 0, 1);
            closest = Math.Min(closest, Vector3.Distance(point, route[i - 1] + segment * fraction));
        }
        return closest;
    }
}
