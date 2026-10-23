namespace OBP.RAC1.Gameplay;

/// <summary>
/// Provisional host presentation policy for Kerwan taxi models. Neither the
/// visual forward-axis correction nor the accelerated route playback is a
/// native PVar constant or a reconstructed cinematic timing contract.
/// </summary>
public static class Rac1TaxiHostPresentationPolicy
{
    // Retail-sampled waypoints cause the faithful per-point seek_1d to
    // repeatedly brake: i962's full two-leg trip takes ~111 seconds at 60 Hz.
    // A bounded 4x host route clock brings both playable passenger rides under
    // 35 seconds while measured motion remains below the source's 18 u/s cap.
    // Do not apply to HUD, player animation, cooldown, or hover timing.
    public const double RouteClockMultiplier = 4d;

    /// <summary>
    /// World-X-mirrored Godot presentation: the taxi mesh's long/front axis
    /// comes from native +X, which becomes local -X in the imported vertices.
    /// Godot yaw zero points local +Z, so aim local -X at the travel tangent.
    /// </summary>
    public static double SceneVisualYaw(double deltaX, double deltaZ)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaZ) ||
            deltaX * deltaX + deltaZ * deltaZ < 1e-8)
            throw new ArgumentOutOfRangeException(nameof(deltaX),
                "Taxi heading needs a nonzero finite horizontal travel tangent.");
        return Math.IEEERemainder(Math.Atan2(deltaX, deltaZ) + Math.PI / 2d,
            2d * Math.PI);
    }
}
