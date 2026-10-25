using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using OBP.IO;
using OBP.PS2.Animation;
using OBP.PS2.Compression;
using OBP.PS2.Elf;
using OBP.PS2.Iso;
using OBP.RAC1.Level;
using OBP.RAC1.Player;
using OBP.RAC2.Level;
using OBP.RAC2.Player;
using OBP.RAC3.Level;
using OBP.RAC3.Player;
using OBP.Runtime.Player;

namespace OBP.Tests;

[CollectionDefinition("Trilogy skeleton retail", DisableParallelization = true)]
public sealed class TrilogySkeletonRetailCollection { }

[Collection("Trilogy skeleton retail")]
public sealed partial class TrilogySkeletalRetailTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public TrilogySkeletalRetailTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public void ThreeRetailCarriersShareTheRigAndKeepNativeEquipmentSelection()
    {
        using var data = new Sources();
        var r1 = data.Rac1(); var rac2 = data.Rac2; var uya = data.Uya;
        var sampler = new PlayerSkeletalSampler(r1.Asset.Skeleton, r1.Asset.Skin);
        sampler.RequireCompatible(rac2.Skeleton); sampler.RequireCompatible(uya.Skeleton);
        foreach (var source in new[] { r1.Asset.Skeleton, rac2.Skeleton, uya.Skeleton })
        {
            Assert.Equal(111, source.Joints.Count);
            Assert.Equal("16460b6fab59e95ab095a580d1db58bfab2d1145b538486d385afe76396cac20",
                Convert.ToHexString(SHA256.HashData(source.Joints.SelectMany(j => BitConverter.GetBytes(j.Parent)).ToArray())).ToLowerInvariant());
            Assert.Equal(31, source.Attachments.Count);
            Assert.Equal(56, source.Attachments[0].Joint);
            Assert.Equal(Enumerable.Range(52, 20), source.Attachments[12].LayerJoints);
            Assert.Equal(Enumerable.Range(72, 20), source.Attachments[13].LayerJoints);
        }
        Assert.Equal((102, 2408), (rac2.Body.Count, rac2.Body.Values.Sum(c => c.Frames.Count)));
        Assert.Equal((127, 2968), (uya.Body.Count, uya.Body.Values.Sum(c => c.Frames.Count)));
        Assert.Equal((1, 56, -1, 62, true, 5), (rac2.ArmCount, rac2.StandingUseSequence, rac2.MovingUseSequence,
            rac2.CrouchingUseSequence, rac2.SustainUse, rac2.UseBlendTicks));
        Assert.Equal((2, 49, -1, 65, false, 2), (uya.ArmCount, uya.StandingUseSequence, uya.MovingUseSequence,
            uya.CrouchingUseSequence, uya.SustainUse, uya.UseBlendTicks));
        // These level carriers contain animation, not the separately streamed skins.
        Assert.Null(rac2.Avatar.Skin); Assert.Null(uya.Avatar.Skin);
        for (int side = 0; side < 2; side++)
        {
            Assert.Equal(24, rac2.Arms[side].Count);
            Assert.Equal(24, uya.Arms[side].Count);
            foreach (int id in rac2.Arms[side].Keys)
            {
                var a = rac2.Arms[side][id]; var b = uya.Arms[side][id];
                Assert.Equal(a.JointIndices, b.JointIndices);
                Assert.Equal(a.Frames.Count, b.Frames.Count);
                // The left-arm sequence20 was revised in UYA. Preserve that
                // source difference instead of deduplicating compatible rigs.
                if (side != 1 || id != 20)
                    for (int f = 0; f < a.Frames.Count; f++) Assert.Equal(a.Frames[f].Joints, b.Frames[f].Joints);
                else Assert.False(a.Frames.SelectMany(f => f.Joints).SequenceEqual(b.Frames.SelectMany(f => f.Joints)));
            }
        }
    }

    [SkippableFact]
    public void ConvertedRac1SkinAndCrossSourceArmsUseTheCommonSampler()
    {
        using var data = new Sources(); var rig = data.Rac1();
        var sampler = new PlayerSkeletalSampler(rig.Asset.Skeleton, rig.Asset.Skin);
        var original = Rac1RatchetAvatar.Decode(data.Rac1Core);
        foreach (int id in new[] { 0, 3, 4, 7, 8, 20 })
        {
            var clip = rig.Asset.Clips[id]; int f = clip.Frames.Count / 2;
            sampler.Reset(clip, new(f, f, 0));
            var positions = sampler.Skin(); var expected = original.Clip(id).LocalFrames[f];
            Assert.InRange(positions.Zip(expected, (a, b) => Math.Abs(a - b)).Max(), 0, .00001);
        }
        foreach (var source in new[] { data.Rac2, data.Uya })
        {
            var player = new PlayerEquipmentAnimationPlayer();
            foreach (var clip in rig.Asset.Clips.Values.Where(c => c.SequenceId < 23))
                for (int f = 0; f < clip.Frames.Count; f++)
                {
                    var sample = new PlayerSkeletalSample(f, (f + 1) % clip.Frames.Count, .5f);
                    var pose = player.Step(source, clip.SequenceId, sample, true, false, false, false, clip);
                    Assert.False(pose.FullBody); Assert.Null(player.CurrentUseSequence);
                    sampler.Reset(clip, sample);
                    foreach (var layer in pose.Layers) sampler.Apply(layer.Clip, layer.Sample, layer.Weight);
                    Assert.All(sampler.Skin(), p => Assert.True(double.IsFinite(p)));
                }
        }
    }

    [SkippableFact]
    public void EquippedTransitionsRetainTheLastSkinAndHandAtTheNativeBlendBoundary()
    {
        using var data = new Sources();
        var avatar = Rac1PlayerAvatarProvider.Instance.Load(Environment.GetEnvironmentVariable("OBP_RAC1_ISO")!, "ratchet");
        foreach (var source in new[] { data.Rac2, data.Uya })
        {
            var controller = (Rac1PlayerAnimationPresentationController)Rac1PlayerAvatarProvider.Instance.CreateAnimationController(avatar);
            int tick = 0, boundaries = 0;
            Step(PlayerAnimationState.Idle, new(false, true, 0));
            for (int i = 0; i < 25; i++) Step(PlayerAnimationState.Idle, new(false, true, 0));
            // Idle -> launch -> run -> skid/stop -> idle, including restarting a stop.
            for (int i = 0; i < 50; i++) Step(PlayerAnimationState.Run, new(true, true, .095f, InputMagnitude: 1));
            for (int i = 0; i < 8; i++) Step(PlayerAnimationState.Idle, new(false, true, .08f, InputMagnitude: 0));
            for (int i = 0; i < 30; i++) Step(PlayerAnimationState.Run, new(true, true, .095f, InputMagnitude: 1));
            for (int i = 0; i < 100; i++) Step(PlayerAnimationState.Idle, new(false, true, 0));
            // A jump and the native standing preset on landing.
            var jump = new Rac1MovementAnimationSelection(7, 7, 1, BlendTicks: 5, Loop: false);
            for (int i = 0; i < 30; i++) Step(PlayerAnimationState.JumpRise, new(false, false, 0, Traversal: jump));
            Step(PlayerAnimationState.Land, new(false, true, 0, GroundEntry: new(0, 0, 2, BlendTicks: -1)));
            for (int i = 0; i < 30; i++) Step(PlayerAnimationState.Idle, new(false, true, 0));
            Assert.True(boundaries >= 5, $"{source.Avatar.Identity.SourceGame}: exercised only {boundaries} transitions.");

            void Step(PlayerAnimationState state, Rac1LocomotionAnimationInput input)
            {
                var previous = controller.Current.SampledPose?.LocalPositions;
                var previousHand = controller.CurrentAttachmentPose(PlayerEquipmentPose.HandAttachmentKey);
                controller.StepOrdinaryAnimation(state, tick++ / 60d, input);
                var pose = controller.ApplySourceEquipment(source, state is PlayerAnimationState.Run or PlayerAnimationState.JumpRise, false, false, false);
                if (!controller.SourceEquipmentIsBlending || controller.SourceEquipmentBlendFraction != 0 || previous is null || previousHand is null) return;
                boundaries++;
                Assert.InRange(pose.SampledPose!.LocalPositions.Zip(previous, (a, b) => Math.Abs(a - b)).Max(), 0, .00001);
                var hand = controller.CurrentAttachmentPose(PlayerEquipmentPose.HandAttachmentKey)!;
                double Error(PlayerAvatarPoint a, PlayerAvatarPoint b) => Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));
                Assert.InRange(Error(previousHand.Origin, hand.Origin), 0, .000001);
                Assert.InRange(Error(previousHand.X, hand.X), 0, .000001);
                Assert.InRange(Error(previousHand.Y, hand.Y), 0, .000001);
                Assert.InRange(Error(previousHand.Z, hand.Z), 0, .000001);
            }
        }
    }

    private sealed class Sources : IDisposable
    {
        private readonly FileRandomAccessReader _rac1 = Open("OBP_RAC1_ISO"), _gc = Open("OBP_GC_ISO"), _uya = Open("OBP_UYA_ISO");
        public Rac1LevelCore.Core Rac1Core { get; }
        public Rac2LevelCore.Core Rac2Core { get; }
        public UyaLevelCore.Core UyaCore { get; }
        public PlayerEquipmentAnimationSource Rac2 { get; }
        public PlayerEquipmentAnimationSource Uya { get; }
        private readonly IRandomAccessReader _rac2Wad, _uyaPrimary;
        private readonly Iso9660Filesystem _rac2Fs;

        public Sources()
        {
            Rac1Core = Rac1LevelCore.Open(_rac1, Rac1DiscIndex.Read(_rac1).Levels.Single(l => l.LevelId == 0));
            _rac2Fs = Iso9660Filesystem.Open(_gc);
            _rac2Wad = _rac2Fs.OpenFile("/G/LEVEL1.WAD")!;
            Rac2Core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(_rac2Wad, Rac2LevelWad.ReadHeader(_rac2Wad), 0));
            var opened = UyaLevelCore.Open(_uya, 1); UyaCore = opened.Core; _uyaPrimary = opened.PrimaryReader;
            Rac2 = Rac2RatchetSkeletalSource.ReadLancer(Rac2Core); Uya = UyaRatchetSkeletalSource.ReadShockCannon(UyaCore);
        }

        public Rac1RatchetEquipmentRig Rac1() => new(Rac1RatchetAvatar.Decode(Rac1Core), Enumerable.Range(52, 40).ToArray());

        public (byte[] Index, byte[] Assets, int Sequences, byte[][] Classes) Raw(string game)
        {
            if (game == "gc") return (Rac2Core.Index, Rac2Core.Assets, Rac2Core.Header.RatchetSeqsOffset,
                Enumerable.Range(0, 3).Select(id => Class(Rac2Core.Index, Rac2Core.Assets, Rac2Core.Header.MobyClasses.Offset,
                    Rac2Core.Header.MobyClasses.Count, Rac2Core.SectionBoundaries, id)).ToArray());
            return (UyaCore.Index, UyaCore.Assets, UyaCore.Header.RatchetSeqsOffset,
                Enumerable.Range(0, 3).Select(id => Class(UyaCore.Index, UyaCore.Assets, UyaCore.Header.MobyClasses.Offset,
                    UyaCore.Header.MobyClasses.Count, UyaCore.SectionBoundaries, id)).ToArray());
        }

        public void WriteOracleInputs(string directory, string game)
        {
            var (index, assets, sequences, classes) = Raw(game);
            var ram = new byte[32 * 1024 * 1024];
            if (game == "gc")
            {
                var elf = _rac2Fs.OpenFile("/SCUS_972.68")!;
                foreach (var segment in Elf32Reader.ReadProgramHeaders(elf).Where(s => s.Type == 1))
                    elf.Read(segment.Offset, (int)segment.FileSize).CopyTo(ram, (int)segment.VirtualAddress);
                Overlay(Rac2LevelOverlay.Open(Rac2LevelWad.RequireLump(_rac2Wad, Rac2LevelWad.ReadHeader(_rac2Wad), 0)));
            }
            else
            {
                byte[] boot = WadLz.ReadBlock(_uya, 1463L * 2048 + 0x7080, 16 * 1024 * 1024).Data;
                for (int at = 0; at < boot.Length;)
                {
                    int dest = I32(boot, at), size = I32(boot, at + 4), kind = I32(boot, at + 8); at += 16;
                    if (kind is 1 or 8) boot.AsSpan(at, size).CopyTo(ram.AsSpan(dest));
                    at += size;
                }
                Overlay(Rac2LevelOverlay.Parse(_uyaPrimary.Read(UyaCore.Data.Overlay.Offset, UyaCore.Data.Overlay.Size)));
            }
            File.WriteAllBytes(Path.Combine(directory, game + "-ram.bin"), ram);
            File.WriteAllBytes(Path.Combine(directory, game + "-assets.bin"), assets);
            File.WriteAllBytes(Path.Combine(directory, game + "-sequences.bin"), index.AsSpan(sequences, 1024).ToArray());
            File.WriteAllBytes(Path.Combine(directory, game + "-player-class.bin"), classes[0]);
            for (int id = 1; id <= 2; id++) File.WriteAllBytes(Path.Combine(directory, $"{game}-class-{id}.bin"), classes[id]);

            void Overlay(Rac2LevelOverlay.Image overlay)
            {
                foreach (var section in overlay.Sections)
                    overlay.Raw.AsSpan(section.DataOffset, section.CopySize).CopyTo(ram.AsSpan((int)section.DestinationAddress));
            }
        }

        private static byte[] Class(byte[] index, byte[] assets, int table, int count, IReadOnlyList<int> bounds, int id)
        {
            int row = Enumerable.Range(0, count).Select(i => table + i * 32).Single(at => I32(index, at + 4) == id);
            int start = I32(index, row), end = bounds.FirstOrDefault(b => b > start, assets.Length);
            return assets[start..end];
        }

        private static FileRandomAccessReader Open(string variable)
        {
            string? path = Environment.GetEnvironmentVariable(variable);
            Skip.If(string.IsNullOrWhiteSpace(path), variable + " is required for retail skeleton comparison.");
            return new(path!);
        }
        public void Dispose() { _rac1.Dispose(); _gc.Dispose(); _uya.Dispose(); }
    }

    private static int I32(byte[] bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
}
