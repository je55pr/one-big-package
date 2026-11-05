using System.Collections.Immutable;
using System.Numerics;
using OBP.Game;
using OBP.Game.Diagnostics;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class FusionTransitionDiagnosticTests
{
    [Theory]
    [InlineData(6, true)]
    [InlineData(3, false)]
    [InlineData(9, false)]
    public void TransitionContinuityComparesEveryRenderedCoordinate(int actualCoordinates, bool accepted)
    {
        // Synthetic port values isolate coordinate coverage, not retail pose parity.
        var fixture = new FusionFixture(Enumerable.Range(0, actualCoordinates).Select(value => (double)value).ToImmutableArray());
        var probe = new TransitionProbe(fixture);
        var previous = Enumerable.Range(0, 6).Select(value => (double)value).ToImmutableArray();
        if (accepted) Assert.Equal(1, probe.Check(previous));
        else Assert.Contains("coordinate count", Assert.Throws<InvalidOperationException>(() => probe.Check(previous)).Message);
    }

    [Fact]
    public void MissingSkinRemainsExplicitlyUnavailable()
    {
        var probe = new TransitionProbe(new FusionFixture(null));
        Assert.Contains("source skin pose is unavailable", Assert.Throws<DiagnosticUnavailableException>(
            () => probe.Check([0, 1, 2, 3, 4, 5])).Message);
    }

    [Theory]
    [InlineData(.000009, true)]
    [InlineData(.000011, false)]
    public void CompletePoseRetainsTheExistingContinuityTolerance(double offset, bool accepted)
    {
        var probe = new TransitionProbe(new FusionFixture([offset, 1, 2, 3, 4, 5]));
        if (accepted) Assert.Equal(1, probe.Check([0, 1, 2, 3, 4, 5]));
        else Assert.Contains("rendered source pose", Assert.Throws<InvalidOperationException>(
            () => probe.Check([0, 1, 2, 3, 4, 5])).Message);
    }

    private sealed class TransitionProbe(IFusionWeaponDiagnosticFixture fusion) : FusionWeaponScenarioSupport(null!, null!, fusion)
    {
        public int Check(ImmutableArray<double> previous)
        {
            int boundaries = 0;
            RequireFusionTransitionContinuity(FusionFixture.Transform, previous, ref boundaries);
            return boundaries;
        }
    }

    private sealed class FusionFixture(ImmutableArray<double>? coordinates) : IFusionWeaponDiagnosticFixture
    {
        internal static DiagnosticWeaponTransform Transform => new(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        private static SourceWeaponKey Key => FusionWeaponSession.Lancer;
        public FusionWeaponAnimationDiagnostic ReadAnimation() => new(null, null, true, 0, null, coordinates);
        public FusionWeaponSessionDiagnostic ReadSession() => new(Key, Key, false, 40, 20, 0, 0, false, 0, null, false, true, true);
        public FusionWeaponHeldDiagnostic ReadHeld(SourceWeaponKey weapon) => new(weapon, true, true, Transform, Transform, Transform);
        public FusionWeaponDiagnosticStatus ReadStatus() => throw new NotSupportedException();
        public FusionWeaponTransientDiagnostic ReadTransients() => throw new NotSupportedException();
        public FusionWeaponMenuDiagnostic ReadMenu() => throw new NotSupportedException();
        public int HeldModelCount => throw new NotSupportedException();
        public int ReadAmmo(SourceWeaponKey _) => throw new NotSupportedException();
        public string OriginalInventory() => throw new NotSupportedException();
        public string CurrentInventory() => throw new NotSupportedException();
        public IFusionAssetDiagnosticWitness CaptureAssets() => throw new NotSupportedException();
        public IFusionPreparationDiagnosticWitness CapturePreparation() => throw new NotSupportedException();
        public IDisposable UseUnavailableAssets() => throw new NotSupportedException();
        public IFusionSourceIsolation IsolateSources() => throw new NotSupportedException();
        public void PrepareSources() => throw new NotSupportedException();
        public void ResetWorldWeapons() => throw new NotSupportedException();
        public void ClearTransients() => throw new NotSupportedException();
        public bool ReenterWithoutIsolatedLoadout() => throw new NotSupportedException();
        public void ShowDestinations() => throw new NotSupportedException();
        public Task CaptureAsync(string _, bool __, CancellationToken ___) => throw new NotSupportedException();
    }
}
