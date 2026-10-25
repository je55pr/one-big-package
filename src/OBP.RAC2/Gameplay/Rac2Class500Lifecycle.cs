using OBP.Core;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// R&C2-owned lifecycle projection for normal class-500 crates. Native state
/// numbers and PVar routing remain here; shared runtime receives only neutral
/// presence/transform/animation effects.
/// </summary>
public static class Rac2Class500Lifecycle
{
    public const int ActiveNativeState = 1;
    public const int BreakTransitionNativeState = 3;
    public const int AlternateNativeState = 6;

    public static Rac2Class500LifecycleResult ApplyRecoveredBreak(
        RuntimeDynamicObject source,
        RuntimeEntityState current)
    {
        if (source.SourceGame != ObpSourceGame.Rac2 || source.NativeClassId != 500)
            throw new ArgumentException("Source is not a Going Commando class-500 crate.", nameof(source));
        current.EnsureMatches(source);

        var authored = Rac2Class500Authority.Read(source)
            ?? throw new InvalidDataException("Class-500 authored authority state is unavailable.");
        if (authored.PvarC8 is not { } c8)
            throw new InvalidDataException("Class-500 PVar+C8 is unavailable.");

        var route = Rac2CrateInteraction.PostBreakRoute(c8);
        var next = route == Rac2Class500PostBreakRoute.Deactivate
            ? current.WithPresence(RuntimeEntityPresence.Inactive)
            : current;

        return new Rac2Class500LifecycleResult(
            authored,
            ActiveNativeState,
            BreakTransitionNativeState,
            route == Rac2Class500PostBreakRoute.State6 ? AlternateNativeState : null,
            route,
            next);
    }
}

/// <summary>
/// Game-specific native transition evidence plus its neutral runtime projection.
/// Native state numbers deliberately stop in OBP.RAC2.
/// </summary>
public sealed record Rac2Class500LifecycleResult(
    Rac2Class500AuthoredState Authored,
    int NativeStateBefore,
    int NativeBreakTransitionState,
    int? NativeStateAfter,
    Rac2Class500PostBreakRoute Route,
    RuntimeEntityState EntityState);
