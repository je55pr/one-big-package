namespace OBP.RAC1.Player;

/// <summary>
/// Water domain supplied by an independently recovered volume/contact owner.
/// A class-zero floor triangle alone does not establish a water volume.
/// </summary>
public enum Rac1WaterDomain
{
    Dry,
    Surface,
    Underwater,
}

public readonly record struct Rac1SwimInput(
    Rac1WaterDomain Water,
    bool Moving,
    bool DivePressed = false,
    bool AscendPressed = false,
    bool DiveAdmitted = false,
    bool SurfaceAdmitted = false,
    bool OwnsHydroPack = false,
    bool HydroRequested = false,
    bool OwnsOxygenMask = false,
    bool NativeDrowningGuardSatisfied = false);

/// <summary>
/// Deterministic source-owned action/oxygen boundary. Does not invent swim
/// velocities, water geometry, animation for surface states or death resets.
/// </summary>
public readonly record struct Rac1SwimSnapshot(
    Rac1WaterDomain Water,
    int NativeDomain,
    int NativeAction,
    int? NativeSequence,
    int Oxygen,
    bool Drowning)
{
    public double OxygenFraction =>
        (double)Oxygen / Rac1TraversalEquipmentWater.MaximumOxygen;
}

public sealed class Rac1SwimmingSession
{
    private int _oxygen = Rac1TraversalEquipmentWater.MaximumOxygen;
    private bool _drowning;

    public int Oxygen => _oxygen;
    public bool Drowning => _drowning;

    public void Reset()
    {
        _oxygen = Rac1TraversalEquipmentWater.MaximumOxygen;
        _drowning = false;
    }

    /// <summary>
    /// Advance one original 60 Hz update. The native drowning guard is an
    /// explicit external fact: oxygen exhaustion alone is not sufficient.
    /// </summary>
    public Rac1SwimSnapshot Step(Rac1SwimInput input)
    {
        if (!Enum.IsDefined(input.Water))
            throw new ArgumentOutOfRangeException(nameof(input));

        if (_drowning)
            return Snapshot(input.Water, Rac1TraversalEquipmentWater.UnderwaterDomain,
                Rac1PlayerActionDomain.Drown, null);

        Rac1WaterDomain next = input.Water;
        if (next == Rac1WaterDomain.Surface && input.DivePressed && input.DiveAdmitted)
            next = Rac1WaterDomain.Underwater;
        else if (next == Rac1WaterDomain.Underwater && input.AscendPressed && input.SurfaceAdmitted)
            next = Rac1WaterDomain.Surface;

        int domain = next switch
        {
            Rac1WaterDomain.Surface => Rac1TraversalEquipmentWater.SurfaceSwimDomain,
            Rac1WaterDomain.Underwater => Rac1TraversalEquipmentWater.UnderwaterDomain,
            _ => 0,
        };

        int action = next switch
        {
            Rac1WaterDomain.Surface when input.Moving => Rac1TraversalEquipmentWater.SurfaceMovingAction,
            Rac1WaterDomain.Surface => Rac1TraversalEquipmentWater.SurfaceIdleAction,
            Rac1WaterDomain.Underwater when input.OwnsHydroPack && input.HydroRequested =>
                Rac1TraversalEquipmentWater.HydroPackAction,
            Rac1WaterDomain.Underwater when input.Moving => Rac1TraversalEquipmentWater.UnderwaterMovingAction,
            Rac1WaterDomain.Underwater => Rac1TraversalEquipmentWater.UnderwaterIdleAction,
            _ => 0,
        };

        _oxygen = Rac1TraversalEquipmentWater.StepOxygen(
            _oxygen, domain, action, input.OwnsOxygenMask);
        if (next == Rac1WaterDomain.Underwater && _oxygen == 0 &&
            !input.OwnsOxygenMask && input.NativeDrowningGuardSatisfied)
        {
            _drowning = true;
            action = Rac1PlayerActionDomain.Drown;
        }

        int? sequence = action switch
        {
            Rac1TraversalEquipmentWater.UnderwaterMovingAction => 59,
            Rac1TraversalEquipmentWater.UnderwaterIdleAction => 60,
            Rac1TraversalEquipmentWater.HydroPackAction => 61,
            _ => null,
        };
        return Snapshot(next, domain, action, sequence);
    }

    private Rac1SwimSnapshot Snapshot(Rac1WaterDomain water, int domain,
        int action, int? sequence) =>
        new(water, domain, action, sequence, _oxygen, _drowning);
}
