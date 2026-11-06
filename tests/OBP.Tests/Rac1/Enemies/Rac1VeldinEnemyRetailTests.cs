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
        // Independently pinned to tools/rac1-veldin-enemy-probe.py; the witness is the subject.
        (string Name, int Start, int End)[] expectedBlocks =
        [
            ("class749Update", 0x2d4610, 0x2d5160), ("class749Damage", 0x2d5160, 0x2d549c),
            ("class749Navigation", 0x2d54c8, 0x2d56dc), ("class749IdleRoaming", 0x261630, 0x261858),
            ("class1440Update", 0x2e0b88, 0x2e1678), ("class1440Damage", 0x2e1678, 0x2e1aa8),
            ("class1440AttackSlots", 0x2e1f28, 0x2e2110), ("meleeDescriptor", 0x2598b0, 0x259bc8),
            ("queryDamageRecordBridge", 0x1f0c40, 0x1f0d60), ("scalarSteering", 0x25b970, 0x25bb38),
            ("angularSteering", 0x25be00, 0x25bfdc), ("sequenceSelection", 0x257880, 0x257b50),
            ("playerDamageIntake", 0x210ce8, 0x211248), ("ordinaryPlayerHurtEntry", 0x2265e0, 0x22668c),
            ("playerNativeTimerUpdate", 0x221604, 0x2216b0), ("integerTimerDecrement", 0x1fef68, 0x1fef98),
            ("nativeDistanceHelpers", 0x1ff450, 0x1ff4b8), ("nativeVectorLength", 0x1ff3d8, 0x1ff408),
            ("reactionLaunch", 0x25c558, 0x25c698), ("reactionStep", 0x25c698, 0x25c758),
            ("class749ReactionParameters", 0x161970, 0x161980), ("class1440ReactionParameters", 0x161bd4, 0x161be4),
        ];
        Assert.Equal(expectedBlocks.Order(), blocks.Select(block => (block.Name,
            Convert.ToInt32(block.Value.GetProperty("start").GetString(), 16),
            Convert.ToInt32(block.Value.GetProperty("end").GetString(), 16))).Order());
        var signatures = report.GetProperty("signatures").EnumerateArray().ToArray();
        int[] expectedAddresses =
        [
            0x2d46f8, 0x2d4adc, 0x2d4b14, 0x2d4fec, 0x2e0d20, 0x2e0d24, 0x2e0d28,
            0x2e0f2c, 0x2e0fa4, 0x2e0fac, 0x2d51cc, 0x2e16e4, 0x2d5288, 0x2d5334,
            0x2598bc, 0x25998c, 0x2599a8, 0x259a04, 0x259a5c, 0x2e0cb4, 0x2e1784,
            0x2e1850, 0x2e1238, 0x2e12ac, 0x2e1310, 0x1f0ccc, 0x1f0cd0, 0x1f0cdc,
            0x1f0ce0, 0x22666c, 0x22665c, 0x226678, 0x210d34, 0x210d40, 0x22163c,
            0x221640, 0x1fef78, 0x1ff490,
        ];
        Assert.Equal(expectedAddresses.Order(), signatures.Select(row => Convert.ToInt32(row.GetProperty("address").GetString(), 16)).Order());
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

    [Theory]
    [InlineData("block-range")]
    [InlineData("block-name")]
    [InlineData("signature-address")]
    public void WitnessRejectsSubstitutedObservationIdentities(string cohort)
    {
        using var witness = JsonDocument.Parse(File.ReadAllText(WitnessPath));
        RequireWitnessCohorts(witness.RootElement);
        var root = JsonNode.Parse(witness.RootElement.GetRawText())!;
        if (cohort == "block-range")
        {
            var block = root["blocks"]!["class749Update"]!;
            block["start"] = block["end"]!.DeepClone();
            block["sha256"] = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant();
        }
        else if (cohort == "block-name")
        {
            var block = root["blocks"]!["class749Update"]!.DeepClone();
            root["blocks"]!.AsObject().Remove("class749Update");
            root["blocks"]!["missing-code-observation"] = block;
        }
        else root["signatures"]![0]!["address"] = "0x002d46fc";
        using var replaced = JsonDocument.Parse(root.ToJsonString());
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => RequireWitnessCohorts(replaced.RootElement));
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
            // Class 1440 authors a second semantic socket (the glow billboard's chain point 1); the toad does not need one.
            if (id == 1440)
                Assert.All(assets.Clips.Values, clip =>
                {
                    Assert.NotNull(clip.SecondSocket);
                    Assert.Equal(clip.ContactSocket.Length, clip.SecondSocket!.Length);
                    Assert.All(clip.SecondSocket, point => Assert.True(float.IsFinite(point.X + point.Y + point.Z)));
                    Assert.Contains(clip.SecondSocket.Zip(clip.ContactSocket), pair => pair.First != pair.Second);
                });
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
