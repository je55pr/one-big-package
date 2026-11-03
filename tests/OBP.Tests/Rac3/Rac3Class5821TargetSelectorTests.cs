using OBP.RAC3.Gameplay;

namespace OBP.Tests;

public sealed class Rac3Class5821TargetSelectorTests
{
    [Fact]
    public void NativePlanarMathConstantsMatchRetailBits()
    {
        Assert.Equal(
            unchecked((int)0x3F490FDB),
            BitConverter.SingleToInt32Bits(
                Rac3Class5821TargetSelector.NativePiOverFour));
        Assert.Equal(
            unchecked((int)0x3FC90FDB),
            BitConverter.SingleToInt32Bits(
                Rac3Class5821TargetSelector.NativePiOverTwo));
        Assert.Equal(
            unchecked((int)0x40490FDB),
            BitConverter.SingleToInt32Bits(
                Rac3Class5821TargetSelector.NativePi));
    }

    [Fact]
    public void NativeHorizontalDistanceUsesRecoveredPlanarComponents()
    {
        Assert.Equal(
            5f,
            Rac3Class5821TargetSelector.NativeHorizontalDistance(
                sourceX: 10f,
                sourceY: 20f,
                targetX: 13f,
                targetY: 24f));
    }

    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(1f, -1f)]
    [InlineData(-1f, -1f)]
    [InlineData(-1f, 1f)]
    [InlineData(0.5f, 1f)]
    [InlineData(1f, 0.5f)]
    [InlineData(-0.5f, 1f)]
    [InlineData(0.5f, -1f)]
    public void NativeFastAtan2TracksRecoveredAtan2Law(
        float deltaY,
        float deltaX)
    {
        float native =
            Rac3Class5821TargetSelector.NativeFastAtan2(deltaY, deltaX);
        float oracle = MathF.Atan2(deltaY, deltaX);

        Assert.InRange(MathF.Abs(native - oracle), 0f, 0.00001f);
    }

    [Fact]
    public void NativeFastAtan2KeepsRetailDiagonalAndZeroCases()
    {
        Assert.Equal(
            Rac3Class5821TargetSelector.NativePiOverFour,
            Rac3Class5821TargetSelector.NativeFastAtan2(1f, 1f));
        Assert.Equal(
            0f,
            Rac3Class5821TargetSelector.NativeFastAtan2(0f, 0f));
    }

    [Fact]
    public void NativeShortestHeadingErrorUsesRecoveredStrictPiFold()
    {
        float pi = Rac3Class5821TargetSelector.NativePi;
        Assert.Equal(
            0.25f,
            Rac3Class5821TargetSelector.NativeShortestHeadingError(
                0.5f,
                0.25f));
        Assert.Equal(
            0.5f,
            Rac3Class5821TargetSelector.NativeShortestHeadingError(
                pi - 0.25f,
                -pi + 0.25f),
            precision: 5);
        Assert.Equal(
            pi,
            Rac3Class5821TargetSelector.NativeShortestHeadingError(
                pi,
                0f));
    }

    [Fact]
    public void NativeFactBuildersUseLiveNativeXyzAndHeadingLaw()
    {
        Rac3Class5821RadiusHeightFacts radius =
            Rac3Class5821TargetSelector.BuildNativeRadiusHeightFacts(
                sourceX: 10f,
                sourceY: 20f,
                sourceZ: 30f,
                targetX: 13f,
                targetY: 24f,
                targetZ: 33f);
        Assert.Equal(5f, radius.HorizontalDistance);
        Assert.Equal(3f, radius.AbsoluteVerticalSeparation);

        Rac3Class5821TargetScoreFacts score =
            Rac3Class5821TargetSelector.BuildNativeScoreFacts(
                sourceX: 10f,
                sourceY: 20f,
                sourceHeading: 0f,
                targetX: 13f,
                targetY: 24f,
                isRatchet: true);
        Assert.Equal(5f, score.HorizontalDistance);
        Assert.True(score.IsRatchet);
        Assert.InRange(
            MathF.Abs(score.ShortestHeadingErrorRadians - MathF.Atan2(4f, 3f)),
            0f,
            0.00001f);
    }

    [Fact]
    public void IndexMinusOneEligibilityUsesStrictHorizontalAndVerticalThresholds()
    {
        Rac3Class5821TargetSelectionRequest request = Request(index: -1, radius: 32f);

        Assert.True(
            Rac3Class5821TargetSelector.IsNativeRadiusHeightEligible(
                new Rac3Class5821RadiusHeightFacts(31.999f, 9.999f),
                request));
        Assert.False(
            Rac3Class5821TargetSelector.IsNativeRadiusHeightEligible(
                new Rac3Class5821RadiusHeightFacts(32f, 0f),
                request));
        Assert.False(
            Rac3Class5821TargetSelector.IsNativeRadiusHeightEligible(
                new Rac3Class5821RadiusHeightFacts(0f, 10f),
                request));
    }

    [Fact]
    public void OrdinaryScoreMatchesNativeSinglePrecisionAccumulation()
    {
        Rac3Class5821TargetSelectionRequest request = Request(index: -1, radius: 32f);

        float candidate = Rac3Class5821TargetSelector.ScoreOrdinaryCandidate(
            new Rac3Class5821TargetScoreFacts(
                HorizontalDistance: 8f,
                ShortestHeadingErrorRadians: 0.5f,
                IsRatchet: false),
            request);
        Assert.Equal(12f, candidate);

        float ratchet = Rac3Class5821TargetSelector.ScoreOrdinaryCandidate(
            new Rac3Class5821TargetScoreFacts(
                HorizontalDistance: 30f,
                ShortestHeadingErrorRadians: 0f,
                IsRatchet: true),
            request);
        Assert.Equal(10f, ratchet);
    }

    [Fact]
    public void RatchetBiasDiffersBetweenDirectAndAuxIndexedPaths()
    {
        Rac3Class5821TargetSelectionRequest direct =
            Request(index: -1, radius: 32f, aux: true);
        Rac3Class5821TargetSelectionRequest indexedWithoutAux =
            Request(index: 73, radius: 32f, aux: false);
        Rac3Class5821TargetSelectionRequest indexedWithAux =
            Request(index: 73, radius: 32f, aux: true);

        Assert.True(
            Rac3Class5821TargetSelector.UsesNativeRatchetScoreReduction(direct));
        Assert.True(
            Rac3Class5821TargetSelector.UsesNativeRatchetScoreReduction(indexedWithoutAux));
        Assert.False(
            Rac3Class5821TargetSelector.UsesNativeRatchetScoreReduction(indexedWithAux));

        var facts = new Rac3Class5821TargetScoreFacts(30f, 0f, IsRatchet: true);
        Assert.Equal(
            10f,
            Rac3Class5821TargetSelector.ScoreOrdinaryCandidate(facts, direct));
        Assert.Equal(
            10f,
            Rac3Class5821TargetSelector.ScoreOrdinaryCandidate(
                facts,
                indexedWithoutAux));
        Assert.Equal(
            30f,
            Rac3Class5821TargetSelector.ScoreOrdinaryCandidate(
                facts,
                indexedWithAux));
    }

    [Fact]
    public void RegistryTagFilterIsExactForOrdinaryRequests()
    {
        Rac3Class5821TargetSelectionRequest request = Request(index: 73, radius: 32f);

        Assert.True(Rac3Class5821TargetSelector.AdmitsRegistryTag(request, 1));
        Assert.False(Rac3Class5821TargetSelector.AdmitsRegistryTag(request, 2));
        Assert.False(Rac3Class5821TargetSelector.AdmitsRegistryTag(request, 3));
    }

    [Fact]
    public void LowerScoringExactTagCandidateReplacesRatchetSeed()
    {
        Rac3Class5821TargetSelectionRequest request = Request(index: 73, radius: 32f);
        var ratchet = new Rac3Class5821TargetCandidateFacts(
            Rac3GameplayEntityRef.Player,
            IsRatchet: true,
            NativeRegistryTag: null,
            IsEligible: true,
            HorizontalDistance: 30f,
            ShortestHeadingErrorRadians: 0f);
        var child = new Rac3Class5821TargetCandidateFacts(
            Rac3GameplayEntityRef.Moby(new Rac3MobyRuntimeKey(6886, 667)),
            IsRatchet: false,
            NativeRegistryTag: 1,
            IsEligible: true,
            HorizontalDistance: 8f,
            ShortestHeadingErrorRadians: 0f);

        Rac3Class5821TargetSelectionResult? selected =
            Rac3Class5821TargetSelector.SelectOrdinaryTarget(
                request,
                ratchet,
                [child]);

        Assert.NotNull(selected);
        Assert.False(selected.IsRatchet);
        Assert.Equal(child.Entity, selected.Entity);
        Assert.Equal(8f, selected.Score);
    }

    [Fact]
    public void EqualScoreDoesNotReplaceEarlierRatchetSeed()
    {
        Rac3Class5821TargetSelectionRequest request = Request(index: 73, radius: 32f);
        var ratchet = new Rac3Class5821TargetCandidateFacts(
            Rac3GameplayEntityRef.Player,
            IsRatchet: true,
            NativeRegistryTag: null,
            IsEligible: true,
            HorizontalDistance: 28f,
            ShortestHeadingErrorRadians: 0f);
        var child = new Rac3Class5821TargetCandidateFacts(
            Rac3GameplayEntityRef.Moby(new Rac3MobyRuntimeKey(6886, 667)),
            IsRatchet: false,
            NativeRegistryTag: 1,
            IsEligible: true,
            HorizontalDistance: 8f,
            ShortestHeadingErrorRadians: 0f);

        Rac3Class5821TargetSelectionResult? selected =
            Rac3Class5821TargetSelector.SelectOrdinaryTarget(
                request,
                ratchet,
                [child]);

        Assert.NotNull(selected);
        Assert.True(selected.IsRatchet);
        Assert.Equal(Rac3GameplayEntityRef.Player, selected.Entity);
        Assert.Equal(8f, selected.Score);
    }

    [Fact]
    public void WrongTagCandidateCannotReplaceRatchetEvenWithLowerScore()
    {
        Rac3Class5821TargetSelectionRequest request = Request(index: 73, radius: 32f);
        var ratchet = new Rac3Class5821TargetCandidateFacts(
            Rac3GameplayEntityRef.Player,
            IsRatchet: true,
            NativeRegistryTag: null,
            IsEligible: true,
            HorizontalDistance: 30f,
            ShortestHeadingErrorRadians: 0f);
        var cachedTagThree = new Rac3Class5821TargetCandidateFacts(
            Rac3GameplayEntityRef.Moby(new Rac3MobyRuntimeKey(6306, 445)),
            IsRatchet: false,
            NativeRegistryTag: 3,
            IsEligible: true,
            HorizontalDistance: 1f,
            ShortestHeadingErrorRadians: 0f);

        Rac3Class5821TargetSelectionResult? selected =
            Rac3Class5821TargetSelector.SelectOrdinaryTarget(
                request,
                ratchet,
                [cachedTagThree]);

        Assert.NotNull(selected);
        Assert.True(selected.IsRatchet);
        Assert.Equal(10f, selected.Score);
    }

    [Fact]
    public void OrdinarySelectorFailsClosedForAlternateTagThreeProfile()
    {
        Rac3Class5821TargetSelectionRequest request =
            Request(index: 73, radius: 32f) with
            {
                NativeCandidateTag =
                    Rac3Class5821Actor.NativeTargetSelectorAlternateTagThree,
            };

        Assert.Throws<NotSupportedException>(() =>
            Rac3Class5821TargetSelector.ScoreOrdinaryCandidate(
                new Rac3Class5821TargetScoreFacts(8f, 0f, IsRatchet: true),
                request));
        Assert.Throws<NotSupportedException>(() =>
            Rac3Class5821TargetSelector.SelectOrdinaryTarget(
                request,
                ratchet: null,
                registryCandidates: []));
    }

    private static Rac3Class5821TargetSelectionRequest Request(
        int index,
        float radius,
        bool aux = false) =>
        new(
            NativeSubtype: 0,
            NativeSelectorIndex: index,
            Radius: radius,
            NativeCandidateTag: 1,
            NativeAuxEnabled: aux,
            WorkspaceOffset: Rac3Class5821Actor.TargetSelectorWorkspaceOffset,
            NativeF13: Rac3Class5821Actor.NativeTargetSelectorF13,
            NativeF14: Rac3Class5821Actor.NativeTargetSelectorF14,
            SeedRatchetBeforeCandidateReplacement: true);
}
