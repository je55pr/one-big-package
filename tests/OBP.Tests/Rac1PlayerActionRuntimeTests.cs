using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1PlayerActionRuntimeTests
{
    [Fact]
    public void CataloguePreservesAll131NativeSlotsWithoutGuessingUnknownNames()
    {
        Assert.Equal(131, Rac1PlayerActionDomain.Catalogue.Count);
        Assert.Equal(
            Enumerable.Range(0, 131),
            Rac1PlayerActionDomain.Catalogue.Select(item => item.NativeState));

        var unknown = Rac1PlayerActionDomain.Describe(0x01);
        Assert.Null(unknown.RecoveredName);
        Assert.Equal("state-0x01", unknown.DisplayName);

        var wade = Rac1PlayerActionDomain.Describe(Rac1PlayerActionDomain.Wade);
        Assert.Equal(0x72, wade.NativeState);
        Assert.Equal("wade", wade.RecoveredName);

        var death = Rac1PlayerActionDomain.Describe(
            Rac1PlayerActionDomain.EnvironmentalFallDeath);
        Assert.Equal("environmental-fall-death", death.RecoveredName);
    }

    [Fact]
    public void StateEntryAndPerFrameUpdateAreSeparateLifecycleDispatches()
    {
        var session = new Rac1PlayerActionRuntimeSession();

        var entered = session.EnterState(
            Rac1PlayerActionDomain.EnvironmentalFallDeath);
        Assert.Equal(Rac1PlayerActionDomain.EnvironmentalFallDeath, entered.CurrentNativeState);
        Assert.Equal(Rac1PlayerActionDomain.Neutral, entered.PreviousNativeState);
        Assert.Equal(
            Rac1RatchetSequenceSelection.EnvironmentalDeathTerminalSequenceId,
            entered.NativeSequence);
        Assert.Equal(0, entered.NativeSequenceFrame);
        Assert.Equal(Rac1OrdinaryMovementPolicy.Suppressed, entered.OrdinaryMovement);
        Assert.Equal(1, entered.EntryGeneration);
        Assert.Equal(0, entered.UpdateGeneration);

        var firstUpdate = session.Update();
        var secondUpdate = session.Update();

        Assert.Equal(1, secondUpdate.EntryGeneration);
        Assert.Equal(2, secondUpdate.UpdateGeneration);
        Assert.Equal(entered.NativeSequence, secondUpdate.NativeSequence);
        Assert.Equal(entered.NativeSequenceFrame, secondUpdate.NativeSequenceFrame);
        Assert.Equal(firstUpdate.CurrentNativeState, secondUpdate.CurrentNativeState);
    }

    [Fact]
    public void WrenchEntryUsesRecoveredActionAndSequenceWithoutHostBooleanState()
    {
        var session = new Rac1PlayerActionRuntimeSession();

        var wrench = session.EnterState(Rac1PlayerActionDomain.Wrench);

        Assert.Equal(Rac1PlayerActionDomain.Wrench, wrench.CurrentNativeState);
        Assert.Equal(Rac1PlayerActionDomain.Neutral, wrench.PreviousNativeState);
        Assert.Equal(
            Rac1RatchetSequenceSelection.WrenchAttackSequenceId,
            wrench.NativeSequence);
        Assert.Equal(0, wrench.NativeSequenceFrame);
        Assert.Equal(
            Rac1OrdinaryMovementPolicy.Allowed,
            wrench.OrdinaryMovement);

        var neutral = session.EnterState(Rac1PlayerActionDomain.Neutral);
        Assert.Equal(Rac1PlayerActionDomain.Neutral, neutral.CurrentNativeState);
        Assert.Equal(Rac1PlayerActionDomain.Wrench, neutral.PreviousNativeState);
    }

    [Fact]
    public void FirstRangedEntryUsesRecoveredActionAndSequence()
    {
        var session = new Rac1PlayerActionRuntimeSession();

        var fire = session.EnterState(Rac1PlayerActionDomain.FirstRangedFire);

        Assert.Equal(Rac1PlayerActionDomain.FirstRangedFire, fire.CurrentNativeState);
        Assert.Equal(Rac1PlayerActionDomain.Neutral, fire.PreviousNativeState);
        Assert.Equal(
            Rac1RatchetSequenceSelection.FirstRangedFireSequenceId,
            fire.NativeSequence);
        Assert.Equal(0, fire.NativeSequenceFrame);
        Assert.Equal(
            Rac1OrdinaryMovementPolicy.Allowed,
            fire.OrdinaryMovement);
    }

    [Fact]
    public void WeaponAdmissionOwnsTypedActionTransitionAndRejectedUseIsNoOp()
    {
        var session = new Rac1PlayerActionRuntimeSession();
        var before = session.Probe();

        var rejected = session.ApplyWeaponUseAdmission(
            Rac1WeaponUseAdmission.Reject(
                Rac1WeaponId.Wrench,
                Rac1WeaponUseRejection.NotEquipped));

        Assert.Same(before, rejected);
        Assert.Same(before, session.Probe());

        var wrench = session.ApplyWeaponUseAdmission(
            Rac1WeaponUseAdmission.AcceptWrench());
        Assert.Equal(Rac1PlayerActionDomain.Wrench, wrench.CurrentNativeState);
        Assert.Equal(
            Rac1RatchetSequenceSelection.WrenchAttackSequenceId,
            wrench.NativeSequence);

        _ = session.EnterState(Rac1PlayerActionDomain.Neutral);
        var ranged = session.ApplyWeaponUseAdmission(
            Rac1WeaponUseAdmission.AcceptFirstRanged(ammoBefore: 6, ammoAfter: 5));
        Assert.Equal(
            Rac1PlayerActionDomain.FirstRangedFire,
            ranged.CurrentNativeState);
        Assert.Equal(
            Rac1RatchetSequenceSelection.FirstRangedFireSequenceId,
            ranged.NativeSequence);
    }

    [Fact]
    public void WeaponAdmissionRejectsIncoherentSelectorsBeforeMutation()
    {
        var session = new Rac1PlayerActionRuntimeSession();
        var before = session.Probe();
        var mismatched = new Rac1WeaponUseAdmission(
            Rac1WeaponId.Wrench,
            Accepted: true,
            Rac1WeaponUseRejection.None,
            AmmoBefore: null,
            AmmoAfter: null,
            NativePlayerActionState: Rac1PlayerActionDomain.Wrench,
            NativePlayerSequenceId: Rac1RatchetSequenceSelection.FirstRangedFireSequenceId);

        Assert.Throws<InvalidDataException>(() =>
            session.ApplyWeaponUseAdmission(mismatched));
        Assert.Same(before, session.Probe());

        var malformedRejected = new Rac1WeaponUseAdmission(
            Rac1WeaponId.Wrench,
            Accepted: false,
            Rac1WeaponUseRejection.NotEquipped,
            AmmoBefore: null,
            AmmoAfter: null,
            NativePlayerActionState: Rac1PlayerActionDomain.Wrench,
            NativePlayerSequenceId: null);
        Assert.Throws<InvalidDataException>(() =>
            session.ApplyWeaponUseAdmission(malformedRejected));
        Assert.Same(before, session.Probe());
    }

    [Fact]
    public void UnknownStateRemainsNumericAndDoesNotGainOrdinaryMovementSemantics()
    {
        var session = new Rac1PlayerActionRuntimeSession();

        var unknown = session.EnterState(0x01);

        Assert.Equal(0x01, unknown.CurrentNativeState);
        Assert.Null(unknown.Descriptor.RecoveredName);
        Assert.Equal("state-0x01", unknown.Descriptor.DisplayName);
        Assert.Equal(Rac1OrdinaryMovementPolicy.Unknown, unknown.OrdinaryMovement);
        Assert.False(unknown.AllowsOrdinaryCharacterMovement);
    }
}
