using OBP.Runtime;

namespace OBP.RAC1.Progression;

/// <summary>
/// Source of the restart placement selected by the bounded recovered checkpoint model.
/// </summary>
public enum Rac1RestartPlacementKind
{
    AuthoredClass0 = 0,
    ActiveCheckpoint = 1,
}

/// <summary>
/// An already-identified native checkpoint activation, normalized to OBP's
/// engine-neutral Y-up runtime placement. This type deliberately does not encode
/// trigger volumes, script predicates, or checkpoint writers because those remain
/// unrecovered.
/// </summary>
public sealed record Rac1CheckpointActivation(
    int NativeLevelId,
    RuntimeSpawn RestartPlacement);

/// <summary>
/// Deterministic restart choice for a recovered environmental-death boundary.
/// </summary>
public sealed record Rac1RestartPlacement(
    int NativeLevelId,
    Rac1RestartPlacementKind Kind,
    RuntimeSpawn Placement);

/// <summary>
/// Engine-neutral, level-local R&amp;C1 checkpoint state. A new instance is created
/// for every full level entry. Checkpoint serialization across planet reload or
/// revisit is not recovered, so this session starts inactive by explicit OBP policy.
/// </summary>
public sealed class Rac1LevelCheckpointSession
{
    public Rac1LevelCheckpointSession(int nativeLevelId, RuntimeSpawn authoredClass0)
    {
        ValidateDestination(nativeLevelId);
        NativeLevelId = nativeLevelId;
        AuthoredClass0 = authoredClass0
            ?? throw new ArgumentNullException(nameof(authoredClass0));
    }

    public int NativeLevelId { get; }
    public RuntimeSpawn AuthoredClass0 { get; }
    public Rac1CheckpointActivation? ActiveCheckpoint { get; private set; }

    /// <summary>
    /// Apply a checkpoint activation only after some source-specific runtime path
    /// has independently identified it. Repeating the same activation is idempotent.
    /// </summary>
    public bool Activate(Rac1CheckpointActivation activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        if (activation.NativeLevelId != NativeLevelId)
        {
            throw new ArgumentException(
                $"Checkpoint level {activation.NativeLevelId} does not match active level {NativeLevelId}.",
                nameof(activation));
        }
        if (ActiveCheckpoint == activation)
            return false;

        ActiveCheckpoint = activation;
        return true;
    }

    /// <summary>
    /// Resolve the placement branch observed across the retained witnesses:
    /// opening Veldin without an active record restarts at authored class 0, while
    /// loaded level 2 with an active record redirects to that record's transform.
    /// This does not claim a generic activation trigger for other levels.
    /// </summary>
    public Rac1RestartPlacement ResolveEnvironmentalRestart()
    {
        if (ActiveCheckpoint is { } checkpoint)
        {
            return new Rac1RestartPlacement(
                NativeLevelId,
                Rac1RestartPlacementKind.ActiveCheckpoint,
                checkpoint.RestartPlacement);
        }

        return new Rac1RestartPlacement(
            NativeLevelId,
            Rac1RestartPlacementKind.AuthoredClass0,
            AuthoredClass0);
    }

    private static void ValidateDestination(int nativeLevelId)
    {
        if ((uint)nativeLevelId >= Rac1CampaignState.NativeDestinationCount)
            throw new ArgumentOutOfRangeException(nameof(nativeLevelId));
    }
}
