using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// R&C2-owned lifecycle projection for normal class-500 crates. Native state
/// numbers and PVar routing remain here; shared runtime receives only neutral
/// presence/transform/animation effects.
/// </summary>
public static class GcClass500Lifecycle
{
    public const int ActiveNativeState = 1;
    public const int BreakTransitionNativeState = 3;
    public const int AlternateNativeState = 6;

    public static GcClass500LifecycleResult ApplyRecoveredBreak(
        RuntimeDynamicObject source,
        RuntimeEntityState current)
    {
        if (source.SourceGame != "rac2" || source.NativeClassId != 500)
            throw new ArgumentException("Source is not a Going Commando class-500 crate.", nameof(source));
        current.EnsureMatches(source);

        var authored = GcClass500Authority.Read(source)
            ?? throw new InvalidDataException("Class-500 authored authority state is unavailable.");
        if (authored.PvarC8 is not { } c8)
            throw new InvalidDataException("Class-500 PVar+C8 is unavailable.");

        var route = GcCrateInteraction.PostBreakRoute(c8);
        var next = route == GcClass500PostBreakRoute.Deactivate
            ? current.WithPresence(RuntimeEntityPresence.Inactive)
            : current;

        return new GcClass500LifecycleResult(
            authored,
            ActiveNativeState,
            BreakTransitionNativeState,
            route == GcClass500PostBreakRoute.State6 ? AlternateNativeState : null,
            route,
            next);
    }
}

/// <summary>
/// Game-specific native transition evidence plus its neutral runtime projection.
/// Native state numbers deliberately stop in OBP.RAC2.
/// </summary>
public sealed record GcClass500LifecycleResult(
    GcClass500AuthoredState Authored,
    int NativeStateBefore,
    int NativeBreakTransitionState,
    int? NativeStateAfter,
    GcClass500PostBreakRoute Route,
    RuntimeEntityState EntityState);
