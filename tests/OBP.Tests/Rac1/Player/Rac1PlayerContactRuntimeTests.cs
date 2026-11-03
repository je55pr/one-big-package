using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1PlayerContactRuntimeTests
{
    [Fact]
    public void NativeContactLayoutPinsRecoveredPlayerSlotsWithoutExposingRawMemory()
    {
        Assert.Equal(0x0f0, Rac1PlayerContactNativeLayout.SupportCarryVectorOffset);
        Assert.Equal(0x2fc, Rac1PlayerContactNativeLayout.CurrentDynamicContactMobyOffset);
        Assert.Equal(0x360, Rac1PlayerContactNativeLayout.PersistentSupportMobyOffset);
        Assert.Equal(0x364, Rac1PlayerContactNativeLayout.SupportAnchorStateOffset);
        Assert.Equal(0x12e0, Rac1PlayerContactNativeLayout.SurfaceClassOffset);
        Assert.Equal(0x12ed, Rac1PlayerContactNativeLayout.SurfaceEffectModeOffset);
    }

    [Fact]
    public void CollisionFacePreservesSurfaceEffectAndSpecialHighBitSeparately()
    {
        var ordinary = Rac1CollisionFaceSemantics.Decode(0x47);

        Assert.Equal(7, ordinary.SurfaceClass);
        Assert.Equal(2, ordinary.RawEffectMode);
        Assert.Equal(2, ordinary.EffectMode);
        Assert.False(ordinary.IsSpecialHighBitFace);

        var special = Rac1CollisionFaceSemantics.Decode(0x87);

        Assert.Null(special.SurfaceClass);
        Assert.Equal(0, special.EffectMode);
        Assert.True(special.IsSpecialHighBitFace);

        var modeThree = Rac1CollisionFaceSemantics.Decode(0x67);
        Assert.Equal(3, modeThree.RawEffectMode);
        Assert.Equal(0, modeThree.EffectMode);
    }

    [Fact]
    public void ContactKeepsCurrentContactPersistentSupportAndAnchorDistinct()
    {
        var current = new Rac1MobyRuntimeKey(300, 12);
        var support = new Rac1MobyRuntimeKey(301, 13);
        var contacted = new Rac1MobyRuntimeKey(
            Rac1PlayerContactResult.MagnebootSupportMobyClass,
            14);
        var contact = new Rac1PlayerContactResult(
            true,
            false,
            Rac1CollisionFaceSemantics.Decode(2),
            contacted,
            current,
            support,
            new Rac1SupportAnchorState(0x00000001u, true),
            new Rac1SupportCarry(new Rac1NativeVector3(0.25, 0.5, -0.25)),
            Rac1ConveyorTransfer.None);

        Assert.Equal(current, contact.CurrentDynamicContact);
        Assert.Equal(support, contact.PersistentSupportMoby);
        Assert.Equal(contacted, contact.ContactedMoby);
        Assert.True(contact.SupportAnchor.IsValid);
        Assert.Equal(
            Rac1SurfaceInteractionKind.MagnebootSupport,
            contact.SurfaceInteraction);
        Assert.True(contact.MovementFacts.IsGrounded);
    }

    [Theory]
    [InlineData(0, Rac1SurfaceInteractionKind.ShallowWaterWade)]
    [InlineData(3, Rac1SurfaceInteractionKind.MudSink)]
    [InlineData(7, Rac1SurfaceInteractionKind.IceSlide)]
    public void RecoveredSurfaceClassesRouteThroughContactRuntime(
        int surfaceClass,
        Rac1SurfaceInteractionKind expected)
    {
        var contact = Rac1PlayerContactResult.StaticWorld(
            true,
            rawFaceType: surfaceClass);

        Assert.Equal(expected, contact.SurfaceInteraction);
    }

    [Fact]
    public void MovingSupportCarryRequiresPersistentSupportAndValidAnchor()
    {
        var support = new Rac1MobyRuntimeKey(900, 2);
        var carry = new Rac1SupportCarry(
            new Rac1NativeVector3(0.25, -0.5, 1.0));
        var ordinary = new Rac1NativeVector3(1.0, 2.0, 3.0);
        var contact = new Rac1PlayerContactResult(
            true,
            false,
            null,
            support,
            support,
            support,
            new Rac1SupportAnchorState(1u, true),
            carry,
            Rac1ConveyorTransfer.None);

        Assert.Equal(
            new Rac1NativeVector3(1.25, 1.5, 4.0),
            contact.ApplySupportAndConveyor(ordinary));
        var invalidAnchor = contact with
        {
            SupportAnchor = new Rac1SupportAnchorState(0u, false),
        };
        Assert.Equal(
            ordinary,
            invalidAnchor.ApplySupportAndConveyor(ordinary));
    }

    [Fact]
    public void ConveyorTransferStaysSeparateFromPlatformCarryAndKeepsClass1250Bias()
    {
        var support = new Rac1MobyRuntimeKey(1250, 7);
        var conveyor = Rac1ConveyorTransfer.ForClass1250(
            new Rac1NativeVector3(3, 0, 4),
            new Rac1NativeVector3(0.1, 0, -0.2));
        var contact = new Rac1PlayerContactResult(
            true,
            false,
            null,
            support,
            support,
            support,
            new Rac1SupportAnchorState(1u, true),
            new Rac1SupportCarry(new Rac1NativeVector3(0.25, 0, 0)),
            conveyor);

        Assert.Equal(
            Rac1ConveyorTransfer.Class1250NativeBiasPerTick,
            conveyor.PreTransformBias.Length,
            precision: 12);
        Assert.Equal(new Rac1NativeVector3(0.25, 0, 0), contact.SupportCarry.TransformDelta);
        Assert.Equal(new Rac1NativeVector3(0.1, 0, -0.2), contact.Conveyor.TangentialTransfer);
        Assert.Equal(
            new Rac1NativeVector3(1.35, 0, 0.8),
            contact.ApplySupportAndConveyor(new Rac1NativeVector3(1, 0, 1)));
    }
}
