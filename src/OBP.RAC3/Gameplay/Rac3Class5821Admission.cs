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
    public Rac3Class5821AuthoredState Authored { get; }
    public Rac3MobyPlacementFacts Placement { get; }

    private Rac3Class5821Admission(RuntimeDynamicObject source, Rac3Class5821AuthoredState authored, Rac3MobyPlacementFacts placement) =>
        (Source, Authored, Placement) = (source, authored, placement);

    public static bool TryRead(RuntimeDynamicObject source, [NotNullWhen(true)] out Rac3Class5821Admission? admission, out string? rejection)
    {
        admission = null;
        try
        {
            var authored = Rac3Class5821Actor.ReadAuthored(source);
            var placement = Rac3MobyPlacement.ReadAuthored(source);
            if (!Rac3Class5821Actor.HasRecoveredTable1DamageProfile(authored) ||
                !Rac3Class5821Actor.HasRecoveredTable1OrdinaryAttackProfile(authored) ||
                !Rac3Class5821Actor.HasRecoveredTable1TargetSelectionProfile(authored))
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
