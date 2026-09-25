using OBP.RAC3.Gameplay;

namespace OBP.Tests;

public sealed class UyaClass5821TargetSelectorTests
{
    [Fact]
    public void IndexMinusOneEligibilityUsesStrictHorizontalAndVerticalThresholds()
    {
        UyaClass5821TargetSelectionRequest request = Request(index: -1, radius: 32f);

        Assert.True(
            UyaClass5821TargetSelector.IsNativeRadiusHeightEligible(
                new UyaClass5821RadiusHeightFacts(31.999f, 9.999f),
                request));
        Assert.False(
            UyaClass5821TargetSelector.IsNativeRadiusHeightEligible(
                new UyaClass5821RadiusHeightFacts(32f, 0f),
                request));
        Assert.False(
            UyaClass5821TargetSelector.IsNativeRadiusHeightEligible(
                new UyaClass5821RadiusHeightFacts(0f, 10f),
                request));
    }

    [Fact]
    public void OrdinaryScoreMatchesNativeSinglePrecisionAccumulation()
    {
        UyaClass5821TargetSelectionRequest request = Request(index: -1, radius: 32f);

        float candidate = UyaClass5821TargetSelector.ScoreOrdinaryCandidate(
            new UyaClass5821TargetScoreFacts(
                HorizontalDistance: 8f,
                ShortestHeadingErrorRadians: 0.5f,
                IsRatchet: false),
            request);
        Assert.Equal(12f, candidate);

        float ratchet = UyaClass5821TargetSelector.ScoreOrdinaryCandidate(
            new UyaClass5821TargetScoreFacts(
                HorizontalDistance: 30f,
                ShortestHeadingErrorRadians: 0f,
                IsRatchet: true),
            request);
        Assert.Equal(10f, ratchet);
    }

    [Fact]
    public void RatchetBiasDiffersBetweenDirectAndAuxIndexedPaths()
    {
        UyaClass5821TargetSelectionRequest direct =
            Request(index: -1, radius: 32f, aux: true);
        UyaClass5821TargetSelectionRequest indexedWithoutAux =
            Request(index: 73, radius: 32f, aux: false);
        UyaClass5821TargetSelectionRequest indexedWithAux =
            Request(index: 73, radius: 32f, aux: true);

        Assert.True(
            UyaClass5821TargetSelector.UsesNativeRatchetScoreReduction(direct));
        Assert.True(
            UyaClass5821TargetSelector.UsesNativeRatchetScoreReduction(indexedWithoutAux));
        Assert.False(
            UyaClass5821TargetSelector.UsesNativeRatchetScoreReduction(indexedWithAux));

        var facts = new UyaClass5821TargetScoreFacts(30f, 0f, IsRatchet: true);
        Assert.Equal(
            10f,
            UyaClass5821TargetSelector.ScoreOrdinaryCandidate(facts, direct));
        Assert.Equal(
            10f,
            UyaClass5821TargetSelector.ScoreOrdinaryCandidate(
                facts,
                indexedWithoutAux));
        Assert.Equal(
            30f,
            UyaClass5821TargetSelector.ScoreOrdinaryCandidate(
                facts,
                indexedWithAux));
    }

    [Fact]
    public void RegistryTagFilterIsExactForOrdinaryRequests()
    {
        UyaClass5821TargetSelectionRequest request = Request(index: 73, radius: 32f);

        Assert.True(UyaClass5821TargetSelector.AdmitsRegistryTag(request, 1));
        Assert.False(UyaClass5821TargetSelector.AdmitsRegistryTag(request, 2));
        Assert.False(UyaClass5821TargetSelector.AdmitsRegistryTag(request, 3));
    }

    [Fact]
    public void LowerScoringExactTagCandidateReplacesRatchetSeed()
    {
        UyaClass5821TargetSelectionRequest request = Request(index: 73, radius: 32f);
        var ratchet = new UyaClass5821TargetCandidateFacts(
            UyaGameplayEntityRef.Player,
            IsRatchet: true,
            NativeRegistryTag: null,
            IsEligible: true,
            HorizontalDistance: 30f,
            ShortestHeadingErrorRadians: 0f);
        var child = new UyaClass5821TargetCandidateFacts(
            UyaGameplayEntityRef.Moby(new UyaMobyRuntimeKey(6886, 667)),
            IsRatchet: false,
            NativeRegistryTag: 1,
            IsEligible: true,
            HorizontalDistance: 8f,
            ShortestHeadingErrorRadians: 0f);

        UyaClass5821TargetSelectionResult? selected =
            UyaClass5821TargetSelector.SelectOrdinaryTarget(
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
        UyaClass5821TargetSelectionRequest request = Request(index: 73, radius: 32f);
        var ratchet = new UyaClass5821TargetCandidateFacts(
            UyaGameplayEntityRef.Player,
            IsRatchet: true,
            NativeRegistryTag: null,
            IsEligible: true,
            HorizontalDistance: 28f,
            ShortestHeadingErrorRadians: 0f);
        var child = new UyaClass5821TargetCandidateFacts(
            UyaGameplayEntityRef.Moby(new UyaMobyRuntimeKey(6886, 667)),
            IsRatchet: false,
            NativeRegistryTag: 1,
            IsEligible: true,
            HorizontalDistance: 8f,
            ShortestHeadingErrorRadians: 0f);

        UyaClass5821TargetSelectionResult? selected =
            UyaClass5821TargetSelector.SelectOrdinaryTarget(
                request,
                ratchet,
                [child]);

        Assert.NotNull(selected);
        Assert.True(selected.IsRatchet);
        Assert.Equal(UyaGameplayEntityRef.Player, selected.Entity);
        Assert.Equal(8f, selected.Score);
    }

    [Fact]
    public void WrongTagCandidateCannotReplaceRatchetEvenWithLowerScore()
    {
        UyaClass5821TargetSelectionRequest request = Request(index: 73, radius: 32f);
        var ratchet = new UyaClass5821TargetCandidateFacts(
            UyaGameplayEntityRef.Player,
            IsRatchet: true,
            NativeRegistryTag: null,
            IsEligible: true,
            HorizontalDistance: 30f,
            ShortestHeadingErrorRadians: 0f);
        var cachedTagThree = new UyaClass5821TargetCandidateFacts(
            UyaGameplayEntityRef.Moby(new UyaMobyRuntimeKey(6306, 445)),
            IsRatchet: false,
            NativeRegistryTag: 3,
            IsEligible: true,
            HorizontalDistance: 1f,
            ShortestHeadingErrorRadians: 0f);

        UyaClass5821TargetSelectionResult? selected =
            UyaClass5821TargetSelector.SelectOrdinaryTarget(
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
        UyaClass5821TargetSelectionRequest request =
            Request(index: 73, radius: 32f) with
            {
                NativeCandidateTag =
                    UyaClass5821Actor.NativeTargetSelectorAlternateTagThree,
            };

        Assert.Throws<NotSupportedException>(() =>
            UyaClass5821TargetSelector.ScoreOrdinaryCandidate(
                new UyaClass5821TargetScoreFacts(8f, 0f, IsRatchet: true),
                request));
        Assert.Throws<NotSupportedException>(() =>
            UyaClass5821TargetSelector.SelectOrdinaryTarget(
                request,
                ratchet: null,
                registryCandidates: []));
    }

    private static UyaClass5821TargetSelectionRequest Request(
        int index,
        float radius,
        bool aux = false) =>
        new(
            NativeSubtype: 0,
            NativeSelectorIndex: index,
            Radius: radius,
            NativeCandidateTag: 1,
            NativeAuxEnabled: aux,
            WorkspaceOffset: UyaClass5821Actor.TargetSelectorWorkspaceOffset,
            NativeF13: UyaClass5821Actor.NativeTargetSelectorF13,
            NativeF14: UyaClass5821Actor.NativeTargetSelectorF14,
            SeedRatchetBeforeCandidateReplacement: true);
}
