using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class Rac1JumpAnticipationContactTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrdinaryPrelaunchReceivesNativeFortyEightDownEvenBeforeContact(bool supported)
    {
        var result = Resolve(Anticipation(0d), supported, 0d, 1d);
        Assert.Equal(-(double)(48f * Rac1AirborneJumpProfile.SquaredSecondsPerUpdate), result.Vertical);
        Assert.Equal(.095d, result.PlanarX);
    }

    [Fact]
    public void AlreadyComputedSpecialPrelaunchRequestIsNotAppliedTwice()
    {
        double nativeRequest = -(48f * Rac1AirborneJumpProfile.SquaredSecondsPerUpdate);
        var result = Resolve(Anticipation(nativeRequest), true, 0d, 1d);
        Assert.Equal(nativeRequest, result.Vertical);
    }

    [Fact]
    public void LedgeProfileOwnedZeroPrelaunchIsNotReplacedWithOrdinaryDownwardRequest()
    {
        var result = Rac1OrdinaryGroundContactMotion.ResolvePreContactStep(Anticipation(0d),
            new PlayerContactFacts(false), 0d, 0d, 1d, applyOrdinaryJumpAnticipationRequest: false);
        Assert.Equal(0d, result.Vertical);
    }

    [Fact]
    public void OrdinaryPrelaunchDoesNotClaimGroundLocomotionTangentConstruction()
    {
        double angle = 20d * Math.PI / 180d;
        var result = Resolve(Anticipation(0d), true, Math.Sin(angle), Math.Cos(angle));
        Assert.Equal(.095d, result.PlanarX);
        Assert.Equal(-Rac1OrdinaryGroundContactMotion.JumpAnticipationDownwardRequestPerTick, result.Vertical);
    }

    [Fact]
    public void LaunchedJumpIsNotPulledBackOntoItsPreviousSupportPlane()
    {
        var step = Anticipation(.15d) with { Phase = Rac1RatchetMovementPhase.Rising };
        var result = Resolve(step, true, Math.Sin(.1d), Math.Cos(.1d));
        Assert.Equal(.15d, result.Vertical);
        Assert.Equal(step.PlanarX, result.PlanarX);
    }

    private static Rac1RatchetMovementController.StepResult Anticipation(double vertical) => new(
        .095d, 0d, vertical, Rac1RatchetMovementPhase.JumpAnticipation,
        Rac1RatchetLocomotionState.JumpAnticipation, Rac1RatchetYawMode.Air);

    private static Rac1OrdinaryGroundContactMotion.PreContactStep Resolve(
        Rac1RatchetMovementController.StepResult step, bool supported, double normalX, double normalUp) =>
        Rac1OrdinaryGroundContactMotion.ResolvePreContactStep(step, new PlayerContactFacts(supported),
            normalX, 0d, normalUp);
}
