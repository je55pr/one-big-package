using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1TaxiNativeHudElementLeaseTests
{
    [Fact]
    public void FirstFocusRequestsHudElementSlotTwelveWithInitUpdateDrawCallbacks()
    {
        var lease = new Rac1TaxiNativeHudElementLease();
        Assert.Equal(-1, lease.NativePvarHudSerial);
        var command = lease.FocusCommand();
        Assert.Equal(Rac1TaxiNativeHudElementLease.HudCommandKind.Request, command.Kind);
        Assert.Equal(12, command.SlotId);
        Assert.Equal(0, command.Key);
        Assert.Equal(0x00226960, command.InitializeAddress);
        Assert.Equal(0x002269b0, command.UpdateAddress);
        Assert.Equal(0x002269d0, command.DrawAddress);
        Assert.Equal(-1, command.Serial);
        Assert.Equal(0, command.MinimumVisibleTicks);
        // Evaluating an intent never fabricates a native serial.
        Assert.Equal(command, lease.FocusCommand());
        Assert.Equal(-1, lease.NativePvarHudSerial);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(73)]
    [InlineData(int.MaxValue)]
    public void LaterFocusExtendsHudVisibilityWithoutAnotherRequest(int serial)
    {
        var lease = new Rac1TaxiNativeHudElementLease();
        lease.AcceptRequestResult(serial);
        var command = lease.FocusCommand();
        Assert.Equal(
            Rac1TaxiNativeHudElementLease.HudCommandKind.ExtendVisibility,
            command.Kind);
        Assert.Equal(serial, command.Serial);
        Assert.Equal(10, command.MinimumVisibleTicks);
        Assert.Equal(command, lease.FocusCommand());
    }

    [Fact]
    public void NativeRequestRefusalZeroIsNotMisidentifiedAsUnallocated()
    {
        var lease = new Rac1TaxiNativeHudElementLease();
        lease.AcceptRequestResult(0);
        Assert.Equal(0, lease.NativePvarHudSerial);
        Assert.Equal(
            Rac1TaxiNativeHudElementLease.HudCommandKind.ExtendVisibility,
            lease.FocusCommand().Kind);
    }

    [Fact]
    public void LevelResetForgetsSavedHudSerialAndAdmitsNewRequest()
    {
        var lease = new Rac1TaxiNativeHudElementLease();
        lease.AcceptRequestResult(123);
        lease.ResetOnStateZero();
        Assert.Equal(-1, lease.NativePvarHudSerial);
        Assert.Equal(
            Rac1TaxiNativeHudElementLease.HudCommandKind.Request,
            lease.FocusCommand().Kind);
        lease.AcceptRequestResult(124);
        Assert.Equal(124, lease.FocusCommand().Serial);
    }

    [Fact]
    public void TwoOriginalShuttlesCanShareOneGlobalHudSerialWithSeparatePvarSnapshots()
    {
        var incoming = new Rac1TaxiNativeHudElementLease();
        var outgoing = new Rac1TaxiNativeHudElementLease();
        incoming.AcceptRequestResult(6);
        Assert.Equal(6, incoming.FocusCommand().Serial);
        Assert.Equal(
            Rac1TaxiNativeHudElementLease.HudCommandKind.Request,
            outgoing.FocusCommand().Kind);
        // The identical native requests target the same global HUD slot and
        // may legitimately return the same existing serial.
        outgoing.AcceptRequestResult(6);
        incoming.ResetOnStateZero();
        Assert.Equal(-1, incoming.NativePvarHudSerial);
        Assert.Equal(6, outgoing.NativePvarHudSerial);
        Assert.Equal(10, outgoing.FocusCommand().MinimumVisibleTicks);
    }

    [Fact]
    public void DuplicateAllocationAndInvalidNativeSerialAreRejected()
    {
        var lease = new Rac1TaxiNativeHudElementLease();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            lease.AcceptRequestResult(-1));
        Assert.Equal(-1, lease.NativePvarHudSerial);
        lease.AcceptRequestResult(7);
        Assert.Throws<InvalidOperationException>(() =>
            lease.AcceptRequestResult(8));
        Assert.Equal(7, lease.NativePvarHudSerial);
    }
}
