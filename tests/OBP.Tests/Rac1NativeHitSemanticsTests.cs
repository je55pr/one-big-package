using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1NativeHitSemanticsTests
{
    private static readonly Rac1NativeDamageEnvelope RepresentativeDamage = new(1d, 0x00010000u);

    [Fact]
    public void DirectVictimRecordRetainsSourceAndPreselectedVictim()
    {
        var handoff = Rac1NativeDamageHandoff.DirectVictim(RepresentativeDamage);

        Assert.Equal(Rac1NativeDamageHandoffKind.DirectVictimRecord, handoff.Kind);
        Assert.Equal(RepresentativeDamage, handoff.Damage);
        Assert.True(handoff.RetainsSourceMoby);
        Assert.True(handoff.VictimIsPreselected);
        Assert.False(handoff.ExcludesSourceMobyFromCandidates);
    }

    [Fact]
    public void ContactVolumeRetainsSourceAndExcludesItFromCandidateVictims()
    {
        var handoff = Rac1NativeDamageHandoff.ContactVolume(RepresentativeDamage);

        Assert.Equal(Rac1NativeDamageHandoffKind.ContactVolume, handoff.Kind);
        Assert.Equal(RepresentativeDamage, handoff.Damage);
        Assert.True(handoff.RetainsSourceMoby);
        Assert.False(handoff.VictimIsPreselected);
        Assert.True(handoff.ExcludesSourceMobyFromCandidates);
    }

    [Fact]
    public void ContactCandidateRejectsOnlyTheSourceMobyAtTheCommonBoundary()
    {
        Assert.False(Rac1NativeHitSemantics.IsDistinctContactCandidate(isSourceMoby: true));
        Assert.True(Rac1NativeHitSemantics.IsDistinctContactCandidate(isSourceMoby: false));
    }

    [Fact]
    public void HostContactFactsCarryTargetStateWithoutOwningCollisionGeometry()
    {
        var target = new RuntimeDynamicObject(
            "rac1",
            Rac1Class749Hostile.NativeClassId,
            149,
            null,
            "moby:749",
            "moby:149",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>());

        var sourceSelf = new Rac1MobyContactFacts(
            target,
            Rac1Class749Hostile.TargetSearchNativeState,
            IsSourceMoby: true);
        var distinct = sourceSelf with { IsSourceMoby = false };

        Assert.Same(target, sourceSelf.Target);
        Assert.Equal(Rac1Class749Hostile.TargetSearchNativeState, sourceSelf.TargetNativeState);
        Assert.False(Rac1NativeHitSemantics.IsDistinctContactCandidate(sourceSelf));
        Assert.True(Rac1NativeHitSemantics.IsDistinctContactCandidate(distinct));
    }

    [Fact]
    public void ProjectileMotionAdvancesPositionThenAppliesRecoveredVerticalStepDelta()
    {
        var motion = new Rac1NativeProjectileMotion(
            155.40013122558594,
            121.16759490966797,
            29.975194931030273,
            0.06286147236824036,
            0.12695619463920593,
            0.09166651964187622);

        var next = motion.Advance(Rac1BombGlove.NativeVerticalStepDeltaAtAuthorityScale);

        Assert.Equal(155.46299269795418, next.X, 10);
        Assert.Equal(121.29455110430717, next.Y, 10);
        Assert.Equal(30.06686145067215, next.Z, 10);
        Assert.Equal(motion.StepX, next.StepX);
        Assert.Equal(motion.StepY, next.StepY);
        Assert.Equal(0.08861096389591694, next.StepZ, 10);
    }

    [Fact]
    public void ProjectileMotionRejectsNegativeOrNonFiniteVerticalDelta()
    {
        var motion = new Rac1NativeProjectileMotion(0d, 0d, 0d, 1d, 2d, 3d);

        Assert.Throws<ArgumentOutOfRangeException>(() => motion.Advance(-0.01d));
        Assert.Throws<ArgumentOutOfRangeException>(() => motion.Advance(double.NaN));
    }
}
