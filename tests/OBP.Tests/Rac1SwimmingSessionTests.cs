using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1SwimmingSessionTests
{
    [Fact]
    public void SurfaceSwimDistinguishesMovingIdleAndRecoveredUnderwaterEntry()
    {
        var session = new Rac1SwimmingSession();
        var surface = session.Step(new(Rac1WaterDomain.Surface, Moving: false));
        Assert.Equal(Rac1TraversalEquipmentWater.SurfaceSwimDomain, surface.NativeDomain);
        Assert.Equal(Rac1TraversalEquipmentWater.SurfaceIdleAction, surface.NativeAction);
        Assert.Null(surface.NativeSequence);

        var moving = session.Step(new(Rac1WaterDomain.Surface, Moving: true));
        Assert.Equal(Rac1TraversalEquipmentWater.SurfaceMovingAction, moving.NativeAction);

        var blocked = session.Step(new(Rac1WaterDomain.Surface, Moving: true, DivePressed: true));
        Assert.Equal(Rac1WaterDomain.Surface, blocked.Water);

        var submerged = session.Step(new(Rac1WaterDomain.Surface, Moving: true,
            DivePressed: true, DiveAdmitted: true));
        Assert.Equal(Rac1WaterDomain.Underwater, submerged.Water);
        Assert.Equal(Rac1TraversalEquipmentWater.UnderwaterMovingAction, submerged.NativeAction);
        Assert.Equal(59, submerged.NativeSequence);
        Assert.Equal(9_989, submerged.Oxygen);
    }

    [Fact]
    public void UnderwaterIdleHydroAndSurfacingRetainNativeIdentity()
    {
        var session = new Rac1SwimmingSession();
        var idle = session.Step(new(Rac1WaterDomain.Underwater, Moving: false));
        Assert.Equal(Rac1TraversalEquipmentWater.UnderwaterIdleAction, idle.NativeAction);
        Assert.Equal(60, idle.NativeSequence);

        var hydro = session.Step(new(Rac1WaterDomain.Underwater, Moving: true,
            OwnsHydroPack: true, HydroRequested: true));
        Assert.Equal(Rac1TraversalEquipmentWater.HydroPackAction, hydro.NativeAction);
        Assert.Equal(61, hydro.NativeSequence);

        var noHydro = session.Step(new(Rac1WaterDomain.Underwater, Moving: true,
            HydroRequested: true));
        Assert.Equal(Rac1TraversalEquipmentWater.UnderwaterMovingAction, noHydro.NativeAction);

        var blocked = session.Step(new(Rac1WaterDomain.Underwater, Moving: true,
            AscendPressed: true));
        Assert.Equal(Rac1WaterDomain.Underwater, blocked.Water);
        var surfaced = session.Step(new(Rac1WaterDomain.Underwater, Moving: false,
            AscendPressed: true, SurfaceAdmitted: true));
        Assert.Equal(Rac1WaterDomain.Surface, surfaced.Water);
        Assert.Equal(Rac1TraversalEquipmentWater.MaximumOxygen, surfaced.Oxygen);
    }

    [Fact]
    public void BreathDrainsAtRetailIntegerRateAndRefillsOnExit()
    {
        var session = new Rac1SwimmingSession();
        Rac1SwimSnapshot state = default;
        for (int i = 0; i < 909; i++)
            state = session.Step(new(Rac1WaterDomain.Underwater, Moving: true));
        Assert.Equal(1, state.Oxygen);
        Assert.False(state.Drowning);

        state = session.Step(new(Rac1WaterDomain.Underwater, Moving: false));
        Assert.Equal(0, state.Oxygen);
        Assert.False(state.Drowning); // Exhaustion is not the entire retail death guard.
        Assert.Equal(Rac1TraversalEquipmentWater.UnderwaterIdleAction, state.NativeAction);

        state = session.Step(new(Rac1WaterDomain.Dry, Moving: false));
        Assert.Equal(10_000, state.Oxygen);
        Assert.Equal(0, state.NativeDomain);
    }

    [Fact]
    public void NativeDrowningGuardLatchesAndNeverRefillsUntilReset()
    {
        var session = new Rac1SwimmingSession();
        for (int i = 0; i < 909; i++)
            session.Step(new(Rac1WaterDomain.Underwater, Moving: false));
        var drowned = session.Step(new(Rac1WaterDomain.Underwater, Moving: false,
            NativeDrowningGuardSatisfied: true));
        Assert.True(drowned.Drowning);
        Assert.Equal(0, drowned.Oxygen);
        Assert.Equal(Rac1PlayerActionDomain.Drown, drowned.NativeAction);

        var leftWater = session.Step(new(Rac1WaterDomain.Dry, Moving: false));
        Assert.True(leftWater.Drowning);
        Assert.Equal(0, leftWater.Oxygen);
        session.Reset();
        Assert.Equal(10_000, session.Step(new(Rac1WaterDomain.Surface, Moving: false)).Oxygen);
    }

    [Fact]
    public void OxygenMaskPreventsLossButDoesNotImplyHydroPack()
    {
        var session = new Rac1SwimmingSession();
        for (int i = 0; i < 1_000; i++)
        {
            var masked = session.Step(new(Rac1WaterDomain.Underwater, Moving: true,
                OwnsOxygenMask: true, HydroRequested: true,
                NativeDrowningGuardSatisfied: true));
            Assert.Equal(10_000, masked.Oxygen);
            Assert.False(masked.Drowning);
            Assert.Equal(Rac1TraversalEquipmentWater.UnderwaterMovingAction, masked.NativeAction);
        }
    }

    [Fact]
    public void InvalidNativeDomainDoesNotSilentlyChooseOrdinaryMovement()
    {
        var session = new Rac1SwimmingSession();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.Step(new((Rac1WaterDomain)99, Moving: true)));
    }
}
