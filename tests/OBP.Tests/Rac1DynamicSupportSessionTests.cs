using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1DynamicSupportSessionTests
{
    [Fact]
    public void ConsecutiveAnchorSamplesProduceSupportTransformDelta()
    {
        var session = new Rac1DynamicSupportSession();
        var support = new Rac1MobyRuntimeKey(900, 2);

        var first = session.Step(Facts(
            support,
            new Rac1NativeVector3(10, 20, 30)));
        var second = session.Step(Facts(
            support,
            new Rac1NativeVector3(10.25, 19.5, 31)));

        Assert.Equal(Rac1SupportCarry.None, first.SupportCarry);
        Assert.Equal(
            new Rac1NativeVector3(0.25, -0.5, 1),
            second.SupportCarry.TransformDelta);
        Assert.Equal(support, second.PersistentSupportMoby);
        Assert.Equal(support, session.PreviousSupport);
    }

    [Fact]
    public void SupportSwitchEstablishesFreshHistoryWithoutCrossObjectCarry()
    {
        var session = new Rac1DynamicSupportSession();
        var firstSupport = new Rac1MobyRuntimeKey(900, 2);
        var secondSupport = new Rac1MobyRuntimeKey(901, 3);

        _ = session.Step(Facts(
            firstSupport,
            new Rac1NativeVector3(10, 20, 30)));
        var switched = session.Step(Facts(
            secondSupport,
            new Rac1NativeVector3(100, 200, 300)));

        Assert.Equal(Rac1SupportCarry.None, switched.SupportCarry);
        Assert.Equal(secondSupport, session.PreviousSupport);
    }

    [Fact]
    public void InvalidAnchorClearsHistoryAndReacquisitionStartsAtZero()
    {
        var session = new Rac1DynamicSupportSession();
        var support = new Rac1MobyRuntimeKey(900, 2);

        _ = session.Step(Facts(
            support,
            new Rac1NativeVector3(10, 20, 30)));
        var released = session.StepStatic(isGrounded: false);

        Assert.Null(released.PersistentSupportMoby);
        Assert.Null(session.PreviousSupport);
        Assert.Null(session.PreviousAnchorWorldPosition);

        var reacquired = session.Step(Facts(
            support,
            new Rac1NativeVector3(15, 20, 30)));
        Assert.Equal(Rac1SupportCarry.None, reacquired.SupportCarry);
    }

    [Fact]
    public void CurrentContactAndPersistentSupportRemainIndependent()
    {
        var session = new Rac1DynamicSupportSession();
        var contacted = new Rac1MobyRuntimeKey(400, 4);
        var current = new Rac1MobyRuntimeKey(401, 5);
        var support = new Rac1MobyRuntimeKey(402, 6);
        var facts = new Rac1DynamicSupportFacts(
            IsGrounded: true,
            HitCeiling: false,
            RawFaceType: 7,
            ContactedMoby: contacted,
            CurrentDynamicContact: current,
            PersistentSupportMoby: support,
            SupportAnchor: new Rac1SupportAnchorState(1u, true),
            SupportAnchorWorldPosition: new Rac1NativeVector3(1, 2, 3),
            Conveyor: Rac1ConveyorTransfer.None);

        var contact = session.Step(facts);

        Assert.Equal(contacted, contact.ContactedMoby);
        Assert.Equal(current, contact.CurrentDynamicContact);
        Assert.Equal(support, contact.PersistentSupportMoby);
        Assert.Equal(Rac1SurfaceInteractionKind.IceSlide, contact.SurfaceInteraction);
    }

    [Fact]
    public void ConveyorTransferRemainsSeparateFromDerivedSupportCarry()
    {
        var session = new Rac1DynamicSupportSession();
        var support = new Rac1MobyRuntimeKey(
            Rac1ConveyorTransfer.RecoveredClass1250,
            7);
        _ = session.Step(Facts(
            support,
            new Rac1NativeVector3(0, 0, 0)));

        var conveyor = Rac1ConveyorTransfer.ForClass1250(
            new Rac1NativeVector3(3, 0, 4),
            new Rac1NativeVector3(0.1, 0, -0.2));
        var contact = session.Step(Facts(
            support,
            new Rac1NativeVector3(0.25, 0, 0),
            conveyor));

        Assert.Equal(
            new Rac1NativeVector3(0.25, 0, 0),
            contact.SupportCarry.TransformDelta);
        Assert.Equal(
            new Rac1NativeVector3(0.1, 0, -0.2),
            contact.Conveyor.TangentialTransfer);
        Assert.Equal(
            new Rac1NativeVector3(1.35, 0, 0.8),
            contact.ApplySupportAndConveyor(
                new Rac1NativeVector3(1, 0, 1)));
    }

    [Fact]
    public void InvalidAnchorMayRetainPointerButClearsCarryHistory()
    {
        var session = new Rac1DynamicSupportSession();
        var support = new Rac1MobyRuntimeKey(900, 2);
        _ = session.Step(Facts(
            support,
            new Rac1NativeVector3(10, 20, 30)));

        var contact = session.Step(new Rac1DynamicSupportFacts(
            IsGrounded: true,
            HitCeiling: false,
            RawFaceType: null,
            ContactedMoby: support,
            CurrentDynamicContact: support,
            PersistentSupportMoby: support,
            SupportAnchor: new Rac1SupportAnchorState(0u, false),
            SupportAnchorWorldPosition: null,
            Conveyor: Rac1ConveyorTransfer.None));

        Assert.Equal(support, contact.PersistentSupportMoby);
        Assert.Equal(Rac1SupportCarry.None, contact.SupportCarry);
        Assert.Null(session.PreviousSupport);
        Assert.Null(session.PreviousAnchorWorldPosition);
    }

    [Fact]
    public void ValidAnchorRequiresSupportIdentityAndAnchorSample()
    {
        var session = new Rac1DynamicSupportSession();
        var support = new Rac1MobyRuntimeKey(900, 2);

        Assert.Throws<ArgumentException>(() => session.Step(
            new Rac1DynamicSupportFacts(
                true,
                false,
                null,
                support,
                support,
                support,
                new Rac1SupportAnchorState(1u, true),
                SupportAnchorWorldPosition: null,
                Rac1ConveyorTransfer.None)));
    }

    private static Rac1DynamicSupportFacts Facts(
        Rac1MobyRuntimeKey support,
        Rac1NativeVector3 anchor,
        Rac1ConveyorTransfer? conveyor = null) =>
        new(
            IsGrounded: true,
            HitCeiling: false,
            RawFaceType: null,
            ContactedMoby: support,
            CurrentDynamicContact: support,
            PersistentSupportMoby: support,
            SupportAnchor: new Rac1SupportAnchorState(1u, true),
            SupportAnchorWorldPosition: anchor,
            Conveyor: conveyor ?? Rac1ConveyorTransfer.None);
}
