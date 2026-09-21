namespace OBP.RAC1.Gameplay;

/// <summary>
/// Native damage handoff shape recovered from the R&amp;C1 engine/common paths.
/// This says how damage is transported after a caller has chosen its geometry;
/// it deliberately does not assign a universal radius, lifetime, cadence or
/// friendly-fire policy to projectiles or attacks.
/// </summary>
public enum Rac1NativeDamageHandoffKind
{
    /// <summary>
    /// A caller already selected one victim and writes a native damage record
    /// retaining both source and victim Mobies.
    /// </summary>
    DirectVictimRecord,

    /// <summary>
    /// A caller supplies source-aware contact geometry to the common gameplay
    /// contact routine. Candidate victims are discovered by that query.
    /// </summary>
    ContactVolume,
}

/// <summary>
/// Copyright-safe semantic projection of the recovered native damage transport.
/// Geometry stays with the weapon/attack caller; this record captures only the
/// common ownership and victim-selection behavior.
/// </summary>
public readonly record struct Rac1NativeDamageHandoff(
    Rac1NativeDamageHandoffKind Kind,
    Rac1NativeDamageEnvelope Damage,
    bool RetainsSourceMoby,
    bool VictimIsPreselected,
    bool ExcludesSourceMobyFromCandidates)
{
    public static Rac1NativeDamageHandoff DirectVictim(Rac1NativeDamageEnvelope damage) =>
        new(
            Rac1NativeDamageHandoffKind.DirectVictimRecord,
            damage,
            RetainsSourceMoby: true,
            VictimIsPreselected: true,
            ExcludesSourceMobyFromCandidates: false);

    public static Rac1NativeDamageHandoff ContactVolume(Rac1NativeDamageEnvelope damage) =>
        new(
            Rac1NativeDamageHandoffKind.ContactVolume,
            damage,
            RetainsSourceMoby: true,
            VictimIsPreselected: false,
            ExcludesSourceMobyFromCandidates: true);
}

/// <summary>
/// Native projectile position/step recurrence recovered from the representative
/// R&C1 item-10 ballistic carrier. Launch owns the initial step vector; the update
/// advances position by that vector and applies a caller-provided vertical step
/// delta. Lifetime/expiry remains caller-specific until a terminal consumer is proven.
/// </summary>
public readonly record struct Rac1NativeProjectileMotion(
    double X,
    double Y,
    double Z,
    double StepX,
    double StepY,
    double StepZ)
{
    public Rac1NativeProjectileMotion Advance(double verticalStepDelta)
    {
        if (!double.IsFinite(verticalStepDelta) || verticalStepDelta < 0d)
            throw new ArgumentOutOfRangeException(nameof(verticalStepDelta));

        return new(
            X + StepX,
            Y + StepY,
            Z + StepZ,
            StepX,
            StepY,
            StepZ - verticalStepDelta);
    }
}

/// <summary>
/// Shared filtering fact actually recovered inside the engine/common contact
/// routine. Broader faction/team/friendly filtering and victim-state filtering
/// remain caller-specific or unresolved and must not be inferred from this check.
/// </summary>
public static class Rac1NativeHitSemantics
{
    public static bool IsDistinctContactCandidate(bool isSourceMoby) =>
        !isSourceMoby;
}
