using OBP.Runtime.Player;

namespace OBP.Tests;

public class PlayerAnimationStateMachineTests
{
    [Fact]
    public void GroundedSpeed_TransitionsIdleToWalkToRun()
    {
        var machine = new PlayerAnimationStateMachine();

        Assert.Equal(PlayerAnimationState.Idle, machine.Update(Grounded(0)));
        Assert.Equal(PlayerAnimationState.Walk, machine.Update(Grounded(0.5)));
        Assert.Equal(PlayerAnimationState.Run, machine.Update(Grounded(4.5)));
    }

    [Fact]
    public void RunningPlayer_JumpsThenFallsFromVerticalVelocity()
    {
        var machine = new PlayerAnimationStateMachine();
        Assert.Equal(PlayerAnimationState.Run, machine.Update(Grounded(5)));

        Assert.Equal(
            PlayerAnimationState.JumpRise,
            machine.Update(Airborne(planarSpeed: 5, verticalVelocity: 3)));
        Assert.Equal(
            PlayerAnimationState.Fall,
            machine.Update(Airborne(planarSpeed: 5, verticalVelocity: 0)));
    }

    [Fact]
    public void FallingPlayer_EmitsLandOnlyOncePerGroundedEpisode()
    {
        var machine = new PlayerAnimationStateMachine();
        machine.Update(Airborne(planarSpeed: 0, verticalVelocity: -4));

        Assert.Equal(
            PlayerAnimationState.Land,
            machine.Update(Grounded(0, justLanded: true)));
        Assert.Equal(
            PlayerAnimationState.Idle,
            machine.Update(Grounded(0, justLanded: true)));

        machine.Update(Airborne(planarSpeed: 0, verticalVelocity: -1));
        Assert.Equal(
            PlayerAnimationState.Land,
            machine.Update(Grounded(0, justLanded: true)));
    }

    [Fact]
    public void Land_ReturnsToIdleOrMovementOnTheNextUpdate()
    {
        var idleMachine = NewFallingMachine();
        Assert.Equal(PlayerAnimationState.Land, idleMachine.Update(Grounded(0, justLanded: true)));
        Assert.Equal(PlayerAnimationState.Idle, idleMachine.Update(Grounded(0)));

        var movingMachine = NewFallingMachine();
        Assert.Equal(PlayerAnimationState.Land, movingMachine.Update(Grounded(2, justLanded: true)));
        Assert.Equal(PlayerAnimationState.Walk, movingMachine.Update(Grounded(2)));
    }

    [Fact]
    public void GroundedThresholds_UseHysteresisToAvoidChatter()
    {
        var machine = new PlayerAnimationStateMachine();

        Assert.Equal(PlayerAnimationState.Idle, machine.Update(Grounded(0.15)));
        Assert.Equal(PlayerAnimationState.Walk, machine.Update(Grounded(0.20)));
        Assert.Equal(PlayerAnimationState.Walk, machine.Update(Grounded(0.15)));
        Assert.Equal(PlayerAnimationState.Idle, machine.Update(Grounded(0.10)));

        Assert.Equal(PlayerAnimationState.Run, machine.Update(Grounded(4.00)));
        Assert.Equal(PlayerAnimationState.Run, machine.Update(Grounded(3.75)));
        Assert.Equal(PlayerAnimationState.Walk, machine.Update(Grounded(3.49)));
        Assert.Equal(PlayerAnimationState.Walk, machine.Update(Grounded(3.75)));
        Assert.Equal(PlayerAnimationState.Run, machine.Update(Grounded(4.00)));
    }

    [Fact]
    public void Attack_OverridesPresentationThenReturnsToObservedMovement()
    {
        var machine = new PlayerAnimationStateMachine();
        Assert.Equal(PlayerAnimationState.Run, machine.Update(Grounded(5)));

        Assert.Equal(
            PlayerAnimationState.Attack,
            machine.Update(Grounded(3.75, attackRequested: true)));
        Assert.Equal(PlayerAnimationState.Run, machine.Update(Grounded(3.75)));
    }

    private static PlayerAnimationStateMachine NewFallingMachine()
    {
        var machine = new PlayerAnimationStateMachine();
        machine.Update(Airborne(planarSpeed: 0, verticalVelocity: -2));
        return machine;
    }

    private static PlayerAnimationFacts Grounded(
        double planarSpeed,
        bool justLanded = false,
        bool attackRequested = false) =>
        new(
            IsGrounded: true,
            PlanarSpeed: planarSpeed,
            VerticalVelocity: 0,
            JustLanded: justLanded,
            AttackRequested: attackRequested);

    private static PlayerAnimationFacts Airborne(double planarSpeed, double verticalVelocity) =>
        new(
            IsGrounded: false,
            PlanarSpeed: planarSpeed,
            VerticalVelocity: verticalVelocity);
}
