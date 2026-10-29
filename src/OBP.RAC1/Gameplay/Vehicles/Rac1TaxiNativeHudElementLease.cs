namespace OBP.RAC1.Gameplay;

/// <summary>
/// Native Kerwan class-1012 HUD element request from L3:0x002dcbc8.
/// Despite the historical native symbol name "camera_request" (0x00235700),
/// slot 12 belongs to the 13-slot HUD element system at 0x0017dc50.
/// The three callbacks initialize, update, and draw a HUD element; they do
/// not move or acquire the gameplay camera.
/// </summary>
public sealed class Rac1TaxiNativeHudElementLease
{
    public const int HudSlot = 0x0c;
    public const int RequestKey = 0;
    public const int InitializeCallback = 0x00226960;
    public const int UpdateCallback = 0x002269b0;
    public const int DrawCallback = 0x002269d0;
    public const int VisibilityExtensionTicks = 10;
    public const int UnallocatedSerial = -1;

    private int _serial = UnallocatedSerial;

    public enum HudCommandKind
    {
        Request,
        ExtendVisibility,
    }

    public readonly record struct HudCommand(
        HudCommandKind Kind,
        int Serial,
        int SlotId,
        int Key,
        int InitializeAddress,
        int UpdateAddress,
        int DrawAddress,
        int MinimumVisibleTicks);

    public int NativePvarHudSerial => _serial;

    public HudCommand FocusCommand()
    {
        if (_serial == UnallocatedSerial)
            return new HudCommand(
                HudCommandKind.Request, UnallocatedSerial,
                HudSlot, RequestKey,
                InitializeCallback, UpdateCallback, DrawCallback, 0);

        return new HudCommand(
            HudCommandKind.ExtendVisibility, _serial,
            HudSlot, RequestKey,
            InitializeCallback, UpdateCallback, DrawCallback,
            VisibilityExtensionTicks);
    }

    /// <summary>
    /// Record the serial returned by an external native HUD request.
    /// Serial zero is preserved because the native allocator can refuse a
    /// request with zero, and the taxi tests only for -1 on subsequent focus.
    /// Multiple taxis may receive the same serial from the shared HUD slot
    /// for identical requests. OBP does not allocate native serials.
    /// </summary>
    public void AcceptRequestResult(int nativeSerial)
    {
        if (_serial != UnallocatedSerial)
            throw new InvalidOperationException("This native HUD serial is already set.");
        if (nativeSerial < 0)
            throw new ArgumentOutOfRangeException(nameof(nativeSerial));
        _serial = nativeSerial;
    }

    /// <summary>
    /// Native shuttle state zero writes -1 to PVar +0xa4. This does not
    /// cancel the shared HUD slot or invalidate another taxi's saved serial.
    /// </summary>
    public void ResetOnStateZero() => _serial = UnallocatedSerial;
}
