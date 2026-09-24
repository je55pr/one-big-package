using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1SurfaceActionRoutingTests
{
    [Theory]
    [InlineData(7, Rac1SurfaceInteractionKind.IceSlide, Rac1PlayerActionDomain.IceSlide)]
    [InlineData(3, Rac1SurfaceInteractionKind.MudSink, Rac1PlayerActionDomain.Mud)]
    public void StaticSurfaceClassesProduceOnlyRecoveredActionIntents(
        int surfaceClass,
        Rac1SurfaceInteractionKind interaction,
        int expectedState)
    {
        var contact = Rac1PlayerContactResult.StaticWorld(
            isGrounded: true,
            rawFaceType: surfaceClass);

        var intent = Assert.IsType<Rac1SurfaceActionIntent>(
            Rac1SurfaceActionRouting.Select(contact));

        Assert.Equal(interaction, intent.Interaction);
        Assert.Equal(expectedState, intent.NativeActionState);
    }

    [Fact]
    public void MagnebootIntentRequiresBothSurfaceClassAndRecoveredMobyClass()
    {
        var support = new Rac1MobyRuntimeKey(
            Rac1PlayerContactResult.MagnebootSupportMobyClass,
            12);
        var contact = new Rac1PlayerContactResult(
            true,
            false,
            Rac1CollisionFaceSemantics.Decode(2),
            support,
            support,
            support,
            new Rac1SupportAnchorState(1u, true),
            Rac1SupportCarry.None,
            Rac1ConveyorTransfer.None);

        var intent = Assert.IsType<Rac1SurfaceActionIntent>(
            Rac1SurfaceActionRouting.Select(contact));

        Assert.Equal(Rac1SurfaceInteractionKind.MagnebootSupport, intent.Interaction);
        Assert.Equal(Rac1PlayerActionDomain.Magneboot, intent.NativeActionState);
    }

    [Fact]
    public void ShallowWaterRemainsFailClosedUntilItsExactActionSlotIsReconciled()
    {
        var contact = Rac1PlayerContactResult.StaticWorld(
            isGrounded: true,
            rawFaceType: 0);

        Assert.Equal(
            Rac1SurfaceInteractionKind.ShallowWaterWade,
            contact.SurfaceInteraction);
        Assert.Null(Rac1SurfaceActionRouting.Select(contact));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(0x87)]
    public void OrdinaryOrExcludedFacesDoNotInventActionIntents(int rawFaceType)
    {
        var contact = Rac1PlayerContactResult.StaticWorld(
            isGrounded: true,
            rawFaceType: rawFaceType);

        Assert.Null(Rac1SurfaceActionRouting.Select(contact));
    }
}
