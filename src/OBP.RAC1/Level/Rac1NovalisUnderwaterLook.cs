using System.Buffers.Binary;

namespace OBP.RAC1.Level;

/// <summary>
/// What the original draws while the camera is under Novalis water: a full-screen colour fade and a
/// short, tinted depth fog (overlay colour at 0x161200, fog colour / range at 0x161204..0x161214, applied by
/// the frame loop at 0x2a0ba8 / fog_setup 0x1f7170 whenever the camera-underwater flag 0x167014 is set).
/// The class-751 pool controller (update 0x2fd0e8) rewrites both colours each frame it finds the camera
/// inside one of its seven zones: dark teal for zones 1-3 (the cave pools), blue otherwise. The initial
/// values are initialised overlay data, so the river (which has no zone) uses the blue default.
/// </summary>
public sealed class Rac1NovalisUnderwaterLook
{
    private const int OverlayAddress = 0x161200, FogColourAddress = 0x161204, FogRangeAddress = 0x161208;

    /// <summary>GS fade: colour bytes and ALPHA FIX out of 0x80 (the fade is drawn with source alpha = Fix / 128).</summary>
    public readonly record struct Fade(byte Red, byte Green, byte Blue, byte Alpha);

    public Fade Overlay { get; private set; } = new(0x18, 0x30, 0x90, 0x40);
    public (byte Red, byte Green, byte Blue) FogColour { get; private set; } = (0x18, 0x30, 0x90);
    /// <summary>Distances in native units / 1024, intensities in GS fog units (255 = no fog).</summary>
    public float FogNear { get; private set; }
    public float FogFar { get; private set; } = 32768f / 1024f;
    public float FogNearIntensity { get; private set; } = 255f;
    public float FogFarIntensity { get; private set; } = 48f;

    /// <summary>Reads the overlay's initial values (the defaults above are those of the NTSC-U original).</summary>
    public void Initialize(Rac1ExecutableOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var fade = overlay.Read(OverlayAddress, 4);
        var fog = overlay.Read(FogColourAddress, 3);
        float F(int offset) => BinaryPrimitives.ReadSingleLittleEndian(overlay.Read(FogRangeAddress + offset, 4));
        float near = F(0), far = F(4), nearIntensity = F(8), farIntensity = F(12);
        if (!float.IsFinite(near) || !float.IsFinite(far) || far <= near || !float.IsFinite(nearIntensity) || !float.IsFinite(farIntensity))
            throw new InvalidDataException("Novalis underwater fog range is not a valid record.");
        Overlay = new(fade[0], fade[1], fade[2], fade[3]);
        FogColour = (fog[0], fog[1], fog[2]);
        (FogNear, FogFar, FogNearIntensity, FogFarIntensity) = (near / 1024f, far / 1024f, nearIntensity, farIntensity);
    }

    /// <summary>
    /// The controller's per-frame colour choice for the last zone that holds the camera: zones 1-3 are the
    /// cave pools (dark teal fade 0x40), every other zone the blue 0x30 fade.
    /// </summary>
    public void SelectZone(int zone)
    {
        if (zone is > 0 and < 4)
        {
            Overlay = new(0x00, 0x28, 0x30, 0x40);
            FogColour = (0x00, 0x28, 0x30);
        }
        else
        {
            Overlay = new(0x18, 0x30, 0x90, 0x30);
            FogColour = (0x18, 0x30, 0x90);
        }
    }
}
