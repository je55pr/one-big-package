namespace OBP.RAC1.Gameplay;

/// <summary>
/// Native Kerwan class-1012 camera request lifecycle from L3:0x002dcbc8.
/// The original engine's camera_request (0x00235700) allocates controller
/// slot 12 and returns a serial stored in taxi PVar +0xa4. Repeated focus
/// calls use sub_00235e18 (0x00235e18) to raise the serial's priority
/// floor to 10, not to create another camera or cut instantly.
/// This models the native request contract only. The Godot host does not
/// yet invoke the retail controller callbacks.
/// </summary>
public sealed class Rac1TaxiNativeCameraLease
{
    public const int ControllerSlot = 0x0c;
    public const int RequestKey = 0;
    public const int StartCallback = 0x00226960;
    public const int SecondCallback = 0x002269b0;
    public const int ThirdCallback = 0x002269d0;
    public const int PriorityFloor = 10;
    public const int UnallocatedSerial = -1;

    private int _handle = UnallocatedSerial;

    public enum CameraCommandKind
    {
        Request,
        RaisePriorityFloor,
    }

    public readonly record struct CameraCommand(
        CameraCommandKind Kind,
        int CameraHandle,
        int SlotId,
        int Key,
        int StartAddress,
        int SecondCallbackAddress,
        int ThirdCallbackAddress,
        int Priority);

    public int NativePvarCameraHandle => _handle;

    public CameraCommand FocusCommand()
    {
        if (_handle == UnallocatedSerial)
            return new CameraCommand(
                CameraCommandKind.Request, UnallocatedSerial,
                ControllerSlot, RequestKey,
                StartCallback, SecondCallback, ThirdCallback, 0);

        return new CameraCommand(
            CameraCommandKind.RaisePriorityFloor, _handle,
            ControllerSlot, RequestKey,
            StartCallback, SecondCallback, ThirdCallback, PriorityFloor);
    }

    /// <summary>
    /// Record the result returned by an external native camera_request.
    /// Serial 0 must be preserved: native camera_request can return zero
    /// on refusal and the updater only compares its PVar handle to -1.
    /// This is not called by the provisional Godot camera.
    /// </summary>
    public void AcceptRequestResult(int nativeSerial)
    {
        if (_handle != UnallocatedSerial)
            throw new InvalidOperationException("This native camera handle is already set.");
        if (nativeSerial < 0)
            throw new ArgumentOutOfRangeException(nameof(nativeSerial));
        _handle = nativeSerial;
    }

    /// <summary>
    /// Native shuttle state zero writes -1 to PVar +0xa4. A level reset
    /// recreates camera-controller context, never reusing a stale serial.
    /// </summary>
    public void ResetOnStateZero() => _handle = UnallocatedSerial;
}
