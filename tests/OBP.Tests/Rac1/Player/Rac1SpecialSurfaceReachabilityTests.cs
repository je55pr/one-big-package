using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class Rac1SpecialSurfaceReachabilityTests
{
    [SkippableFact]
    public void DecodedRetailLevelsContainRepresentativeRecoveredSpecialSurfaces()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);

        var veldin = ProbeLevel(reader, 0, null);
        Assert.Equal(86_184, veldin.Triangles);
        Assert.Equal(0, veldin.Class0);
        Assert.Equal(0, veldin.Class3);
        Assert.Equal(0, veldin.Class7);

        var novalis = ProbeLevel(reader, 1, 0);
        Assert.Equal(3_936, novalis.Class0);
        AssertSpecialRouting(
            novalis.RepresentativeRawFaceType,
            Rac1SurfaceInteractionKind.ShallowWaterWade,
            Rac1PlayerActionDomain.Wade);

        var aridia = ProbeLevel(reader, 2, 3);
        Assert.Equal(1_975, aridia.Class3);
        AssertSpecialRouting(
            aridia.RepresentativeRawFaceType,
            Rac1SurfaceInteractionKind.MudSink,
            Rac1PlayerActionDomain.Mud);

        var hoven = ProbeLevel(reader, 12, 7);
        Assert.Equal(2_777, hoven.Class7);
        AssertSpecialRouting(
            hoven.RepresentativeRawFaceType,
            Rac1SurfaceInteractionKind.IceSlide,
            Rac1PlayerActionDomain.IceSlide);

        RuntimeWorld quartu = Rac1WorldImport.Build(reader, 15);
        Assert.Equal(703, CountSurfaceClass(quartu, 0));
        Assert.Equal(
            18,
            quartu.DynamicObjects!.Count(
                moby => moby.NativeClassId == Rac1ConveyorTransfer.RecoveredClass1250));
        Assert.Equal(
            1d / 24d,
            Rac1ConveyorTransfer.Class1250NativeBiasPerTick,
            precision: 15);
    }

    private static LevelProbe ProbeLevel(
        IRandomAccessReader reader,
        int levelId,
        int? representativeSurfaceClass)
    {
        RuntimeWorld world = Rac1WorldImport.Build(reader, levelId);
        int? representative = representativeSurfaceClass.HasValue
            ? world.CollisionMeshes
                .SelectMany(collision => collision.TriangleMaterialIds)
                .Where(raw => raw is >= byte.MinValue and <= byte.MaxValue)
                .First(raw =>
                    Rac1CollisionFaceSemantics.Decode(raw).SurfaceClass ==
                    representativeSurfaceClass.Value)
            : null;

        return new LevelProbe(
            world.CollisionMeshes.Sum(collision => collision.Triangles),
            CountSurfaceClass(world, 0),
            CountSurfaceClass(world, 3),
            CountSurfaceClass(world, 7),
            representative);
    }

    private static int CountSurfaceClass(RuntimeWorld world, int expected) =>
        world.CollisionMeshes
            .SelectMany(collision => collision.TriangleMaterialIds)
            .Where(raw => raw is >= byte.MinValue and <= byte.MaxValue)
            .Count(raw => Rac1CollisionFaceSemantics.Decode(raw).SurfaceClass == expected);

    private static void AssertSpecialRouting(
        int? rawFaceType,
        Rac1SurfaceInteractionKind expectedInteraction,
        int expectedState)
    {
        int raw = Assert.IsType<int>(rawFaceType);
        var contact = Rac1PlayerContactResult.StaticWorld(
            isGrounded: true,
            rawFaceType: raw);
        var intent = Assert.IsType<Rac1SurfaceActionIntent>(
            Rac1SurfaceActionRouting.Select(contact));

        Assert.Equal(expectedInteraction, intent.Interaction);
        Assert.Equal(expectedState, intent.NativeActionState);
        Assert.Throws<NotSupportedException>(() =>
            new Rac1RatchetMovementController().Step(
                new PlayerControlIntent(0d, 1d, false, false),
                contact,
                _ => 0d));
    }

    private sealed record LevelProbe(
        int Triangles,
        int Class0,
        int Class3,
        int Class7,
        int? RepresentativeRawFaceType);
}
