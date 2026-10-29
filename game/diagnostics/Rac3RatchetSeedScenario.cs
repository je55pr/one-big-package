using OBP.Core;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;

namespace OBP.Game.Diagnostics;

/// <summary>Read-only, explicitly requested class-5821 Ratchet target-seed evidence.</summary>
internal sealed record Rac3RatchetSeedSnapshot(int Authored, int Admitted, int PrimarySeeded,
    int SecondarySeeded, int PrimaryUnknown, int SecondaryUnknown)
{
    public int Queries => checked(Admitted * 2);
}

/// <summary>
/// Uses the same recovered R&C3 selector and placement facts as ordinary source
/// tests, but never advances a native actor, damages a player or creates an actor.
/// Only the diagnostic runner calls this sampler.
/// </summary>
internal static class Rac3RatchetSeedSampler
{
    internal static Rac3RatchetSeedSnapshot Sample(RuntimeWorld world, Rac3NativePoint ratchet)
    {
        if (world.Game != ObpSourceGame.Rac3 ||
            !Rac3WorldGameplaySidecar.TryGet(world, out Rac3WorldGameplayContext? context) ||
            context is null)
            throw new DiagnosticUnavailableException("R&C3 source-qualified gameplay context is unavailable.");

        int authored = 0, admitted = 0;
        int primary = 0, secondary = 0, primaryUnknown = 0, secondaryUnknown = 0;
        foreach (var source in (world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
            .Where(item => item.NativeClassId == Rac3Class5821Actor.NativeClassId))
        {
            authored++;
            if (!Rac3Class5821Admission.TryRead(source, out var admission, out _))
                continue;
            admitted++;
            if (!TrySelect(context, admission.Authored, admission.Placement, ratchet,
                    Rac3Class5821Actor.NativeObservedSelectorSubtypePrimary, out bool first))
                primaryUnknown++;
            else if (first)
                primary++;
            if (!TrySelect(context, admission.Authored, admission.Placement, ratchet,
                    Rac3Class5821Actor.NativeObservedSelectorSubtypeSecondaryA, out bool second))
                secondaryUnknown++;
            else if (second)
                secondary++;
        }
        return new(authored, admitted, primary, secondary, primaryUnknown, secondaryUnknown);
    }

    private static bool TrySelect(Rac3WorldGameplayContext context,
        Rac3Class5821AuthoredState authored, Rac3MobyPlacementFacts placement,
        Rac3NativePoint ratchet, byte nativeSubtype, out bool selected)
    {
        selected = false;
        Rac3Class5821TargetSelectionRequest request;
        try
        {
            request = Rac3Class5821Actor.BuildTable1TargetSelectionRequest(
                authored, nativeSubtype,
                runtimeModeEnabled: authored.InitialTargetSelectorMode != 0,
                runtimeAuxEnabled: authored.InitialTargetSelectorAux != 0);
        }
        catch (NotSupportedException) { return false; }

        bool eligible;
        if (request.NativeSelectorIndex >= 0)
        {
            if (!context.Gameplay.TryContainsTargetGroup(
                    request.NativeSelectorIndex, ratchet.X, ratchet.Y, ratchet.Z, out eligible))
                return false;
        }
        else
        {
            Rac3Class5821RadiusHeightFacts facts =
                Rac3Class5821TargetSelector.BuildNativeRadiusHeightFacts(
                    placement.NativeX, placement.NativeY, placement.NativeZ,
                    ratchet.X, ratchet.Y, ratchet.Z);
            eligible = Rac3Class5821TargetSelector.IsNativeRadiusHeightEligible(facts, request);
        }

        Rac3Class5821TargetScoreFacts scoreFacts =
            Rac3Class5821TargetSelector.BuildNativeScoreFacts(
                placement.NativeX, placement.NativeY, placement.RotationZ,
                ratchet.X, ratchet.Y, isRatchet: true);
        var ratchetFacts = new Rac3Class5821TargetCandidateFacts(
            Rac3GameplayEntityRef.Player, IsRatchet: true,
            NativeRegistryTag: null, IsEligible: eligible,
            scoreFacts.HorizontalDistance, scoreFacts.ShortestHeadingErrorRadians);
        Rac3Class5821TargetSelectionResult? result =
            Rac3Class5821TargetSelector.SelectOrdinaryTarget(request, ratchetFacts, registryCandidates: []);
        selected = result is { IsRatchet: true };
        return true;
    }
}

/// <summary>Runs only after the explicit scenario scheduler's source/build/world admission.</summary>
internal sealed class Rac3RatchetSeedScenario(
    DiagnosticScenarioDefinition definition,
    Func<Rac3RatchetSeedSnapshot> sample,
    Action<string> log) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition => definition;

    public async Task RunAsync(DiagnosticRunContext run)
    {
        // The former inspector audit waited 0.75 seconds after world entry.
        // A fixed 45 native-physics-frame wait keeps the same settle window.
        await run.WaitAsync(DiagnosticFramePhase.Physics, 45);
        Rac3RatchetSeedSnapshot result = sample();
        log($"[uya-gameplay] class-5821 Ratchet-seed audit: " +
            $"primary={result.PrimarySeeded}/{result.Admitted}, " +
            $"secondary={result.SecondarySeeded}/{result.Admitted}, " +
            $"unknown={result.PrimaryUnknown}/{result.SecondaryUnknown}; " +
            $"authored={result.Authored}, queries={result.Queries}; " +
            "live subtype +0x95 not host-owned");
    }
}
