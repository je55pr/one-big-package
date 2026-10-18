using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1AirborneAdmissionTests
{
    [Fact]
    public void DirectionalClassifierUsesNativeFacingAndStrictSectors()
    {
        Assert.Equal(2, Rac1AirborneAdmission.ClassifyFlipDirection(0, 0));
        Assert.Equal(0, Rac1AirborneAdmission.ClassifyFlipDirection(Rac1AirborneAdmission.NativeHalfPi, 0));
        Assert.Equal(1, Rac1AirborneAdmission.ClassifyFlipDirection(-Rac1AirborneAdmission.NativeHalfPi, 0));
        Assert.Equal(3, Rac1AirborneAdmission.ClassifyFlipDirection(Rac1AirborneAdmission.NativePi, 0));
        Assert.Equal(3, Rac1AirborneAdmission.ClassifyFlipDirection(-Rac1AirborneAdmission.NativePi, 0));
        Assert.Equal(2, Rac1AirborneAdmission.ClassifyFlipDirection(Rac1AirborneAdmission.NativeQuarterPi, 0));
        Assert.Equal(2, Rac1AirborneAdmission.ClassifyFlipDirection(-Rac1AirborneAdmission.NativeQuarterPi, 0));
        Assert.Equal(2, Rac1AirborneAdmission.ClassifyFlipDirection(1, 1));
        Assert.Equal(0, Rac1AirborneAdmission.ClassifyFlipDirection(1, 1 - Rac1AirborneAdmission.NativeHalfPi));
        Assert.Equal(0, Rac1AirborneAdmission.DirectionalChordWindow(2));
        Assert.Equal(12, Rac1AirborneAdmission.DirectionalChordWindow(1));
    }

    [Fact]
    public void DirectionalFlipHasPriorityOverEquipmentAndCrouchAge()
    {
        var input = Ordinary with
        {
            CrouchActionAge = 0,
            CrossBufferedNineUpdates = false,
            DirectionalChordBuffered = true,
            FlipDirection = 3,
            ActivePackId = 3,
        };
        Assert.Equal(Rac1AirborneJumpKind.DirectionalFlip, Select(input));
        Assert.Null(Select(input with { CrouchHeld = false }));
        Assert.Null(Select(input with { FlipDirection = 2 }));
        Assert.Null(Select(input with { CurrentAction = 0x0e }));
    }

    [Fact]
    public void PackHighJumpsRequireTheRecoveredOwnershipAndDisableFacts()
    {
        Assert.Equal(Rac1AirborneJumpKind.HeliPackHigh, Select(Ordinary with { ActivePackId = 2 }));
        Assert.Equal(Rac1AirborneJumpKind.ThrusterPackHigh, Select(Ordinary with { ActivePackId = 3 }));
        Assert.Equal(Rac1AirborneJumpKind.Stationary, Select(Ordinary with { ActivePackId = 4 }));
        Assert.Equal(Rac1AirborneJumpKind.Stationary, Select(Ordinary with { ActivePackId = 2, ClankAvailable = false }));
        Assert.Equal(Rac1AirborneJumpKind.Stationary, Select(Ordinary with { ActivePackId = 3, PackDisabled = true }));
        Assert.Null(Select(Ordinary with { CrouchActionAge = 6 }));
        Assert.Null(Select(Ordinary with { CrossBufferedNineUpdates = false }));
    }

    [Fact]
    public void LongJumpTestsForwardObstacleQueryAndForwardRequestRatherThanGroundHeight()
    {
        var input = Ordinary with
        {
            ActivePackId = 2,
            ForwardClearance = 2.6f,
            ConditionedMagnitude = 0.8f,
            ForwardRequestStep = 0.02f,
        };
        Assert.Equal(Rac1AirborneJumpKind.HeliPackLong, Select(input));
        Assert.Equal(Rac1AirborneJumpKind.HeliPackHigh, Select(input with { ConditionedMagnitude = 0.7f }));
        Assert.Equal(Rac1AirborneJumpKind.HeliPackHigh,
            Select(input with { ForwardRequestStep = Rac1AirborneAdmission.LongJumpMinimumForwardStep }));
        Assert.Equal(Rac1AirborneJumpKind.HeliPackHigh, Select(input with { ForwardClearance = 2.5f }));
        Assert.Equal(Rac1AirborneJumpKind.HeliPackLong,
            Select(input with { ForwardClearance = 1f, ForwardHitNormalAngle = 0 }));
        var thruster = input with { ActivePackId = 3 };
        Assert.Equal(Rac1AirborneJumpKind.ThrusterPackLong, Select(thruster));
        Assert.Equal(Rac1AirborneJumpKind.ThrusterPackHigh, Select(thruster with { ForwardClearance = 1 }));
        Assert.Equal(Rac1AirborneJumpKind.ThrusterPackLong,
            Select(thruster with { ForwardClearance = 1, ForwardHitCrateFamily = true }));
        var blocked = Rac1AirborneAdmission.SelectCrouchJump(thruster with { ThrusterLongJumpGate = true });
        Assert.Null(blocked.Kind);
        Assert.True(blocked.ConsumesJumpInput);
    }

    private static Rac1AirborneJumpKind? Select(Rac1AirborneAdmission.CrouchJumpInput input) =>
        Rac1AirborneAdmission.SelectCrouchJump(input).Kind;

    private static readonly Rac1AirborneAdmission.CrouchJumpInput Ordinary = new(
        CurrentAction: 4, CrouchActionAge: 7, CrouchHeld: true,
        CrossBufferedNineUpdates: true, DirectionalChordBuffered: false,
        FlipDirection: 2, ActivePackId: 0, ClankAvailable: true, PackDisabled: false,
        ForwardClearance: 1, ForwardHitNormalAngle: Rac1AirborneAdmission.NativeHalfPi,
        ForwardHitCrateFamily: false, ConditionedMagnitude: 0,
        ForwardRequestStep: 0, ThrusterLongJumpGate: false);
}
