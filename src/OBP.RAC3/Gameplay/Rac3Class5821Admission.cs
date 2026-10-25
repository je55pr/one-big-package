using System.Diagnostics.CodeAnalysis;
using OBP.Runtime;

namespace OBP.RAC3.Gameplay;

/// <summary>
/// Atomic admission of the recovered TABLE1 profile and its native placement.
/// Parsing and source-native predicates complete before a host can publish an audit entry.
/// </summary>
public sealed record Rac3Class5821Admission
{
    public RuntimeDynamicObject Source { get; }
    public UyaClass5821AuthoredState Authored { get; }
    public UyaMobyPlacementFacts Placement { get; }

    private Rac3Class5821Admission(RuntimeDynamicObject source, UyaClass5821AuthoredState authored, UyaMobyPlacementFacts placement) =>
        (Source, Authored, Placement) = (source, authored, placement);

    public static bool TryRead(RuntimeDynamicObject source, [NotNullWhen(true)] out Rac3Class5821Admission? admission, out string? rejection)
    {
        admission = null;
        try
        {
            var authored = UyaClass5821Actor.ReadAuthored(source);
            var placement = UyaMobyPlacement.ReadAuthored(source);
            if (!UyaClass5821Actor.HasRecoveredTable1DamageProfile(authored) ||
                !UyaClass5821Actor.HasRecoveredTable1OrdinaryAttackProfile(authored) ||
                !UyaClass5821Actor.HasRecoveredTable1TargetSelectionProfile(authored))
            {
                rejection = "Unsupported class-5821 TABLE1 profile.";
                return false;
            }
            admission = new(source, authored, placement);
            rejection = null;
            return true;
        }
        // Native readers use SingleOrDefault: duplicate payload formats are also
        // malformed authored input and cannot escape the rejection boundary.
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException)
        {
            rejection = error.Message;
            return false;
        }
    }
}
