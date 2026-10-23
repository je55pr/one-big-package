using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1TaxiNativeCameraLeaseTests
{
    [Fact]
    public void FirstFocusRequestsNativeControllerSlotTwelveWithExactCallbacks()
    {
        var lease = new Rac1TaxiNativeCameraLease();
        Assert.Equal(-1, lease.NativePvarCameraHandle);
        var command = lease.FocusCommand();
        Assert.Equal(Rac1TaxiNativeCameraLease.CameraCommandKind.Request, command.Kind);
        Assert.Equal(12, command.SlotId);
        Assert.Equal(0, command.Key);
        Assert.Equal(0x00226960, command.StartAddress);
        Assert.Equal(0x002269b0, command.SecondCallbackAddress);
        Assert.Equal(0x002269d0, command.ThirdCallbackAddress);
        Assert.Equal(-1, command.CameraHandle);
        Assert.Equal(0, command.Priority);
        // Evaluating an intent never fabricates a native serial.
        Assert.Equal(command, lease.FocusCommand());
        Assert.Equal(-1, lease.NativePvarCameraHandle);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(73)]
    [InlineData(int.MaxValue)]
    public void LaterFocusRaisesExistingNativeSerialPriorityWithoutRerequest(int serial)
    {
        var lease = new Rac1TaxiNativeCameraLease();
        lease.AcceptRequestResult(serial);
        var command = lease.FocusCommand();
        Assert.Equal(
            Rac1TaxiNativeCameraLease.CameraCommandKind.RaisePriorityFloor,
            command.Kind);
        Assert.Equal(serial, command.CameraHandle);
        Assert.Equal(10, command.Priority);
        Assert.Equal(command, lease.FocusCommand());
    }

    [Fact]
    public void NativeRequestRefusalZeroIsNotMisidentifiedAsUnallocated()
    {
        var lease = new Rac1TaxiNativeCameraLease();
        lease.AcceptRequestResult(0);
        Assert.Equal(0, lease.NativePvarCameraHandle);
        Assert.Equal(
            Rac1TaxiNativeCameraLease.CameraCommandKind.RaisePriorityFloor,
            lease.FocusCommand().Kind);
    }

    [Fact]
    public void LevelResetForgetsOldSerialAndAdmitsNewCameraRequest()
    {
        var lease = new Rac1TaxiNativeCameraLease();
        lease.AcceptRequestResult(123);
        lease.ResetOnStateZero();
        Assert.Equal(-1, lease.NativePvarCameraHandle);
        Assert.Equal(
            Rac1TaxiNativeCameraLease.CameraCommandKind.Request,
            lease.FocusCommand().Kind);
        lease.AcceptRequestResult(124);
        Assert.Equal(124, lease.FocusCommand().CameraHandle);
    }

    [Fact]
    public void TwoOriginalShuttleInstancesHaveIndependentSerialOwnership()
    {
        var incoming = new Rac1TaxiNativeCameraLease();
        var outgoing = new Rac1TaxiNativeCameraLease();
        incoming.AcceptRequestResult(6);
        Assert.Equal(6, incoming.FocusCommand().CameraHandle);
        Assert.Equal(
            Rac1TaxiNativeCameraLease.CameraCommandKind.Request,
            outgoing.FocusCommand().Kind);
        outgoing.AcceptRequestResult(7);
        incoming.ResetOnStateZero();
        Assert.Equal(-1, incoming.NativePvarCameraHandle);
        Assert.Equal(7, outgoing.NativePvarCameraHandle);
        Assert.Equal(10, outgoing.FocusCommand().Priority);
    }

    [Fact]
    public void DuplicateAllocationAndInvalidNativeSerialAreRejected()
    {
        var lease = new Rac1TaxiNativeCameraLease();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            lease.AcceptRequestResult(-1));
        Assert.Equal(-1, lease.NativePvarCameraHandle);
        lease.AcceptRequestResult(7);
        Assert.Throws<InvalidOperationException>(() =>
            lease.AcceptRequestResult(8));
        Assert.Equal(7, lease.NativePvarCameraHandle);
    }
}
