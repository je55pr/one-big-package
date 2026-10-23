using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1LedgeTraversalTests
{
    [Fact]
    public void NativeCollisionProfileKeepsQueryHeightAndSolverLimitsSeparateFromHostShape()
    {
        var profile = Rac1CollisionProfile.Ordinary;
        Assert.Equal(.45f, profile.HorizontalRadius);
        Assert.Equal(.8f, profile.UpperContactHeight);
        Assert.Equal(.7f, profile.LowerContactHeight);
        Assert.Equal(.8f - .7f, profile.AxialQuerySpan);
        Assert.Equal(8, Rac1CollisionProfile.MaximumNativeCorrectionPasses);
        var crouch = profile.ApproachHeights(.35000002f, .7f);
        Assert.Equal(.8f - .02f, crouch.UpperContactHeight);
        Assert.Equal(.7f, crouch.LowerContactHeight);
        for (int update = 0; update < 30; update++)
            crouch = crouch.ApproachHeights(.35000002f, .7f);
        Assert.Equal(.35000002f, crouch.UpperContactHeight);
        Assert.Equal(.05f, crouch.AxialQuerySpan);
    }

    [Fact]
    public void LedgeProbeFindsTopThenWallThenOutwardClearanceAndProducesNativeHangTarget()
    {
        var world = new LedgeFixture();
        Assert.True(Probe(world, out var candidate));
        Assert.Equal(1.4d, candidate.TopPoint.Z);
        Assert.Equal(1.4d - Rac1LedgeProbe.HangHeightBelowTop, candidate.HangPosition.Z, 12);
        Assert.Equal(0d, candidate.FacingYaw, 12);
        Assert.InRange(candidate.HangPosition.X, .13d, .14d);
        Assert.False(candidate.ClimbBodyBlocked);
        Assert.Contains(world.Rays, ray => ray.Mask == Rac1TraversalRayMask.Support);
        Assert.All(world.BodyQueries, body =>
        {
            Assert.Equal((double).45f, body.HorizontalRadius);
            Assert.Equal((double).6f, body.AxialSpan);
            Assert.Equal(1.4d + .6f, body.Origin.Z);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(12)]
    public void WaterAndExcludedAuthoredTopClassesCannotBecomeLedges(int surfaceClass)
    {
        var world = new LedgeFixture { SurfaceClass = surfaceClass };
        Assert.False(Probe(world, out _));
        Assert.DoesNotContain(world.Rays, ray => ray.Mask == Rac1TraversalRayMask.Support);
    }

    [Theory]
    [InlineData(0x1f)]
    [InlineData(0x3f)]
    [InlineData(0x5f)]
    [InlineData(0x7f)]
    public void StaticUnclassifiedFacesAreClimbableWithoutInventingAnOrdinarySurfaceClass(int rawFaceType)
    {
        Assert.Null(Rac1CollisionFaceSemantics.Decode(rawFaceType).SurfaceClass);
        var hit = Rac1TraversalRayHit.StaticWorld(default, new(0, 0, 1), rawFaceType);
        Assert.Equal(-1, hit.SurfaceClass);
        Assert.True(hit.TraversalAllowed);
        Assert.True(Probe(new LedgeFixture { RawFaceType = rawFaceType }, out _));
    }

    [Fact]
    public void StaticFaceMappingRetainsNativeExclusionsAndUnsupportedSignedMembership()
    {
        foreach (int raw in Enumerable.Range(0, 128))
        {
            bool excluded = (raw & 31) is 0 or 9 or 12;
            Assert.Equal(!excluded, Probe(new LedgeFixture { RawFaceType = raw }, out _));
        }
        Assert.False(Probe(new LedgeFixture { RawFaceType = 0xbf }, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac1TraversalRayHit.StaticWorld(default, default, 256));
    }

    [Fact]
    public void TopRequiresEnoughDropBelowItAndCannotBeGrabbedThroughCeiling()
    {
        var tooLow = new LedgeFixture();
        Assert.False(Rac1LedgeProbe.TryFindCandidate(
            Input with { SolidFloorHeight = 0d }, tooLow.Raycast, tooLow.Overlaps, out _));
        var ceiling = new LedgeFixture { CeilingBlocks = true };
        Assert.False(Probe(ceiling, out _));
        var dynamicTop = new LedgeFixture { TraversalAllowed = false };
        Assert.False(Probe(dynamicTop, out _));
    }

    [Fact]
    public void SurfaceExitLedgeAdmissionUsesTheSubmergedSolidFloorRatherThanWaterOrPreviousPlatform()
    {
        // Translate the reported pool to feet Z=0: water=.12, step=1.4,
        // bottom=-3.6. Only the solid-floor query is P+2d8 in the native gate.
        Assert.True(Rac1LedgeProbe.AdmitsOrdinaryRatchetCandidate(0x12, 4, false, 2d / 60d, 0));
        Assert.False(Rac1LedgeProbe.AdmitsOrdinaryRatchetCandidate(0x37, 18, false, 0, 0));
        var pool = new LedgeFixture();
        Assert.True(Rac1LedgeProbe.TryFindCandidate(Input with { SolidFloorHeight = -3.6 },
            pool.Raycast, pool.Overlaps, out _));
        foreach (double wrongFloor in new[] { .12, 3.35 })
            Assert.False(Rac1LedgeProbe.TryFindCandidate(Input with { SolidFloorHeight = wrongFloor },
                pool.Raycast, pool.Overlaps, out _));
    }

    [Fact]
    public void TopAngleAndWallFacingAreIndependentAdmissionGates()
    {
        var steep = new LedgeFixture
        {
            TopNormal = new(Math.Sin(Rac1LedgeProbe.MaximumTopAngle), 0d,
                Math.Cos(Rac1LedgeProbe.MaximumTopAngle)),
        };
        Assert.False(Probe(steep, out _));
        var backOfWall = new LedgeFixture { WallNormal = new(1d, 0d, 0d) };
        Assert.False(Probe(backOfWall, out _));
    }

    [Fact]
    public void BodyClearanceAnnotatesTheCandidateRatherThanMakingItAClimbTeleport()
    {
        var world = new LedgeFixture { BodyBlocked = true };
        Assert.True(Probe(world, out var candidate));
        Assert.True(candidate.ClimbBodyBlocked);
        var session = new Rac1LedgeTraversalSession();
        session.TryGrab(candidate);
        Assert.Equal(Rac1LedgeTraversalSession.GrabAction, session.NativeAction);
    }

    [Fact]
    public void CandidatePredictionUsesTwoPreviousMotionStepsAndThreeGravityIncrements()
    {
        var rays = new List<Rac1TraversalRay>();
        Rac1LedgeProbe.TryFindCandidate(Input with
        {
            PreviousMotion = new(.04d, -.02d, .1d),
            PredictionTicks = 2,
            PredictionGravityPerTick = .01d,
        }, ray => { rays.Add(ray); return null; }, _ => false, out _);
        Assert.Equal(.08d + Rac1LedgeProbe.Radius + Rac1LedgeProbe.ForwardProbeIncrement, rays[0].From.X, 12);
        Assert.Equal(-.04d, rays[0].From.Y, 12);
        Assert.Equal(.2d - .03d + Rac1LedgeProbe.TopRayHigh, rays[0].From.Z, 12);
        Assert.Equal(4, rays.Count);
    }

    [Fact]
    public void GeometricLedgeQueriesRemainSubjectToNativeAirAndCooldownAdmission()
    {
        Assert.True(Admission(6, 2, .1d));
        Assert.True(Admission(8, 5, .1d));
        Assert.True(Admission(0x1a, 3, 0d));
        Assert.True(Admission(7, 4, -.01d));
        Assert.False(Admission(7, 4, .1d));
        Assert.False(Admission(2, 1, 0d));
        Assert.True(Rac1LedgeProbe.AdmitsOrdinaryRatchetCandidate(7, 4, true, .1d, 0d));
        Assert.False(Rac1LedgeProbe.AdmitsOrdinaryRatchetCandidate(0xe, 4, true, .1d, 36d));
        Assert.False(Rac1LedgeProbe.AdmitsOrdinaryRatchetCandidate(6, 2, false, 0d, 0d, collisionMode: 2));
        Assert.False(Rac1LedgeProbe.AdmitsOrdinaryRatchetCandidate(6, 2, false, 0d, 0d, grabCooldownTicks: 1));
        static bool Admission(int action, int domain, double vertical) =>
            Rac1LedgeProbe.AdmitsOrdinaryRatchetCandidate(action, domain, false, vertical, 0d);
    }

    [Fact]
    public void AscendingSteepWallCorrectionRemovesOnlyOpposedPlanarMotionAndAddsNativeDownRequest()
    {
        var request = new Rac1NativeVector3(.1d, .03d, .05d);
        var facts = new Rac1TraversalContactFacts(1, -.01d, .05d, true,
            new(-Math.Sin(Math.PI / 3d), 0d, Math.Cos(Math.PI / 3d)));
        var corrected = Rac1TraversalContactMotion.ResolveAscendingSteepContact(request, facts);
        Assert.Equal(0d, corrected.X, 12);
        Assert.Equal(request.Y, corrected.Y);
        Assert.Equal(request.Z - Rac1TraversalContactMotion.AdditionalDownwardRequest, corrected.Z);
        foreach (var inactive in new[]
        {
            facts with { UnsupportedTicks = 0 },
            facts with { SupportCorrectionMetric = .15f },
            facts with { PreviousDisplacementVertical = 0d },
            facts with { HasRadialContact = false },
            facts with { RadialContactNormal = new(-1d, 0d, 0d) },
            facts with { RadialContactNormal = new(-.1d, 0d, 1d) },
            facts with { RadialContactNormal = new(Math.Sin(Math.PI / 3d), 0d, Math.Cos(Math.PI / 3d)) },
        })
            Assert.Equal(request, Rac1TraversalContactMotion.ResolveAscendingSteepContact(request, inactive));
    }

    [Fact]
    public void GrabPreservesItsEntryPoseAndWaitsForStrictAuthoredTimeGate()
    {
        var session = Grab();
        var result = session.Step(StepInput with { AuthoredAnimationTime = 13.5d }, _ => Candidate);
        Assert.Equal(32, result.Sequence);
        Assert.Equal(2, result.EntryFrame);
        Assert.Equal(9, result.BlendTicks);
        Assert.Equal(Rac1LedgeTraversalSession.HoldStepLimit, result.Motion.Length, 12);
        result = session.Step(StepInput with { AuthoredAnimationTime = 13.5001d }, _ => Candidate);
        Assert.Equal(33, result.Sequence);
        Assert.Equal(0, result.EntryFrame);
        Assert.Equal(6, result.BlendTicks);
        Assert.Equal(2, result.SelectionGeneration);
        Assert.Equal(new Rac1NativeVector3(.01d, 0d, 0d),
            Rac1LedgeTraversalSession.ResolveHoldMotion(default, new(.01d, 0d, 0d)));
        var fast = new Rac1LedgeTraversalSession();
        fast.TryGrab(Candidate, .09d);
        Assert.Equal(0, fast.Step(StepInput, _ => Candidate).EntryFrame);
    }

    [Fact]
    public void HangingCanJumpFromLedgeDropOrLoseContactWithNativeActionExits()
    {
        var jump = Hanging();
        var result = jump.Step(StepInput with { JumpPressAgeTicks = 16 }, _ => Candidate);
        Assert.Equal(Rac1LedgeExitKind.LedgeJump, result.ExitKind);
        Assert.Equal(0x1c, result.NativeAction);
        Assert.Equal(34, result.Sequence);
        Assert.Equal(4, result.BlendTicks);
        Assert.False(jump.Active);

        var drop = Hanging();
        result = drop.Step(StepInput with { DropPressed = true }, _ => Candidate);
        Assert.Equal(Rac1LedgeExitKind.Drop, result.ExitKind);
        Assert.Equal(6, result.NativeAction);
        var lost = Hanging();
        Assert.Equal(Rac1LedgeExitKind.Drop,
            lost.Step(StepInput with { ContactRetained = false }, _ => null).ExitKind);

        var away = Hanging();
        result = away.Step(StepInput with
        {
            JumpPressAgeTicks = 0,
            InputMagnitude = 1d,
            DesiredYaw = Math.PI,
        }, _ => Candidate);
        Assert.Equal(Rac1LedgeExitKind.Drop, result.ExitKind);
    }

    [Fact]
    public void ShimmyUsesNativeSignedAuthoredTimeTableAndWaitsForCycleBeforeResting()
    {
        var session = Hanging();
        var lateral = StepInput with { InputMagnitude = 1d, DesiredYaw = Math.PI / 2d };
        var selected = session.Step(lateral, _ => Candidate);
        Assert.Equal(0x1a, selected.NativeAction);
        Assert.Equal(36, selected.Sequence);
        Rac1LedgeCandidate? AlongLedge(Rac1NativeVector3 position) => Candidate with
        {
            HangPosition = new(position.X, position.Y, position.Z),
        };
        var forward = session.Step(lateral with { AuthoredAnimationTime = 5d }, AlongLedge);
        Assert.Equal((double)(.162f * .5f), forward.Motion.Y, 10);
        var backward = session.Step(lateral with { AuthoredAnimationTime = 13d }, AlongLedge);
        Assert.Equal((double)(-.035f * .5f), backward.Motion.Y, 10);
        var slowing = session.Step(StepInput, AlongLedge);
        Assert.Equal(36, slowing.Sequence);
        var rest = session.Step(StepInput with { AnimationCycleCompleted = true }, AlongLedge);
        Assert.Equal(33, rest.Sequence);
        Assert.Equal(4, rest.SelectionGeneration);
    }

    [SkippableFact]
    public void CanonicalVeldinOverlayPinsCollisionProfileLedgeQueriesAndAuthoredMotion()
    {
        var read = ReadOverlay();
        foreach (var (start, end, hash) in new[]
        {
            (0x204f80, 0x204fe0, "f88b40932748ecbdf0f30f11af1fb9aee38e1b968c536dd93f83fdac1d4615be"),
            (0x211380, 0x21165c, "3ba8576be944958edccff69f6de5580728aada73040672604844bb0115ee09b5"),
            (0x2133a8, 0x2137a4, "000d07da032f1afc4673a02be9260517261ff0ce1115bb47ddbb8348a39457d7"),
            (0x2121c0, 0x212318, "b17a1f4c8f07c816fea1e1931a0405b6b8be69265e80f396b1e026ee90ce8115"),
            (0x20bed0, 0x20c704, "f6d62613f9847ab74cd9b45ddef1548a9ea006ac23b5aeeeaa60c7d63d077966"),
            (0x20c758, 0x20cd04, "e5f157d32bea9140f12ec78bda114a37578b4273785a06fdd0de46a7e9d0c4d4"),
            (0x21dfd0, 0x21e3a0, "b0f7093c486e8ec8cdadcba272956fc376189bcf9b8a67c1a867c75697d6a7e3"),
            (0x224158, 0x2242d4, "c7dbaf7765682d9881778f80a5fd9a3847885595456e629927ecf7ac6abcb326"),
            (0x22d884, 0x22dd68, "ad2598856424639631ed809fa977d4539d4828d53713e49af798e9b3cca32c31"),
            (0x1c3cb0, 0x1c3cec, "30306d081c60f6d9b0826f0b46ffc9c414152578f4c66382394d88511da7e6d8"),
        })
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(read(start, end - start))).ToLowerInvariant());
        int word(int address) => BinaryPrimitives.ReadInt32LittleEndian(read(address, 4));
        float literal(int address)
        {
            int high = (word(address) & 0xffff) << 16;
            int next = word(address + 4);
            return BitConverter.Int32BitsToSingle(high | ((next >> 16) == 0x3421 ? next & 0xffff : 0));
        }
        Assert.Equal(Rac1CollisionProfile.OrdinaryHorizontalRadius, literal(0x204fa0));
        Assert.Equal(Rac1CollisionProfile.OrdinaryUpperContactHeight, literal(0x204f84));
        Assert.Equal(Rac1CollisionProfile.OrdinaryLowerContactHeight, literal(0x204f90));
        Assert.Equal(-Rac1LedgeProbe.HangHeightBelowTop, literal(0x20c0bc));
        Assert.Equal(Rac1LedgeProbe.MaximumTopAngle, literal(0x20c294));
        Assert.Equal(Rac1LedgeProbe.MaximumFacingError, literal(0x20c618));
        Assert.Equal(Rac1TraversalContactMotion.NearSupportMetric, literal(0x2121ec));
        Assert.Equal(0x224158, word(0x1e8030 + 0x18 * 4));
        Assert.Equal(0x224218, word(0x1e8030 + 0x19 * 4));
        Assert.Equal(0x22d884, word(0x1e82b0 + 0x18 * 4));
        Assert.Equal(0x21dfd0, word(0x1e7e20 + 0x18 * 4));
        Assert.Equal(0x21e148, word(0x1e7e20 + 0x1a * 4));
        for (int time = 0; time < 15; time++)
        {
            float native = BitConverter.Int32BitsToSingle(word(0x1c3cb0 + time * 4));
            Assert.Equal(native * .5f, Rac1LedgeTraversalSession.ShimmyStepAt(time));
        }
    }

    private static Rac1LedgeProbeInput Input => new(default, default, 0d, -1d, 0d, 0);
    private static Rac1LedgeCandidate Candidate => new(new(.6d, 0d, 1.4d), new(.15d, 0d, 0d), Math.PI, false);
    private static Rac1LedgeStepInput StepInput => new(default, 0d, 0d, 0d, 0d, false, false, null);
    private static bool Probe(LedgeFixture world, out Rac1LedgeCandidate candidate) =>
        Rac1LedgeProbe.TryFindCandidate(Input, world.Raycast, world.Overlaps, out candidate);
    private static Rac1LedgeTraversalSession Grab()
    {
        var session = new Rac1LedgeTraversalSession();
        Assert.True(session.TryGrab(Candidate));
        return session;
    }
    private static Rac1LedgeTraversalSession Hanging()
    {
        var session = Grab();
        session.Step(StepInput with { AuthoredAnimationTime = 14d }, _ => Candidate);
        return session;
    }

    private sealed class LedgeFixture
    {
        public int SurfaceClass { get; init; } = 1;
        public int? RawFaceType { get; init; }
        public bool TraversalAllowed { get; init; } = true;
        public bool CeilingBlocks { get; init; }
        public bool BodyBlocked { get; init; }
        public Rac1NativeVector3 TopNormal { get; init; } = new(0d, 0d, 1d);
        public Rac1NativeVector3 WallNormal { get; init; } = new(-1d, 0d, 0d);
        public List<Rac1TraversalRay> Rays { get; } = [];
        public List<Rac1TraversalBodyQuery> BodyQueries { get; } = [];
        public Rac1TraversalRayHit? Raycast(Rac1TraversalRay ray)
        {
            Rays.Add(ray);
            if (Math.Abs(ray.From.X - ray.To.X) < 1e-9d &&
                ray.From.X >= .6d - 1e-6d && ray.From.Z >= 1.4d && ray.To.Z <= 1.4d)
                return Hit(new(ray.From.X, ray.From.Y, 1.4d), TopNormal);
            if (Math.Abs(ray.From.Z - ray.To.Z) < 1e-9d &&
                ray.From.X < .6d && ray.To.X >= .6d && ray.From.Z < 1.4d)
                return Hit(new(.6d, ray.From.Y, ray.From.Z), WallNormal);
            if (CeilingBlocks && Math.Abs(ray.From.Z - ray.To.Z) < 1e-9d && ray.From.Z > 1.4d)
                return new(new(.3d, 0d, ray.From.Z), new(-1d, 0d, 0d), 1);
            return null;
        }
        private Rac1TraversalRayHit Hit(Rac1NativeVector3 position, Rac1NativeVector3 normal) =>
            RawFaceType is int raw
                ? Rac1TraversalRayHit.StaticWorld(position, normal, raw)
                : new(position, normal, SurfaceClass, TraversalAllowed);
        public bool Overlaps(Rac1TraversalBodyQuery body)
        {
            BodyQueries.Add(body);
            return BodyBlocked;
        }
    }

    private static Func<int, int, byte[]> ReadOverlay()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == 0);
        var outer = level.CoreRanges[0];
        var data = new SubRangeReader(reader, outer.OffsetBytes, outer.SizeBytes, "level data");
        var directory = Rac1LevelData.ReadDirectory(data);
        var range = Rac1LevelData.OpenRange(data, directory, 0);
        byte[] payload = range.Read(0, checked((int)range.Length));
        Assert.Equal("eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804",
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());
        var sections = new List<(int Address, int Offset, int Size)>();
        for (int offset = 0; offset < payload.Length;)
        {
            int address = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
            int size = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4));
            Assert.Equal(0x245c28, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 12)));
            sections.Add((address, offset + 16, size));
            offset += 16 + size;
        }
        Assert.Equal(7, sections.Count);
        return (address, size) =>
        {
            var section = sections.Single(section => address >= section.Address && address + size <= section.Address + section.Size);
            return payload.AsSpan(section.Offset + address - section.Address, size).ToArray();
        };
    }
}
