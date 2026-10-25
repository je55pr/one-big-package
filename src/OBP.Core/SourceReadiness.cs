namespace OBP.Core;

public enum SourceReadinessKind { Ready, Missing, Unavailable, Unsupported }
public sealed record SourceReadinessEntry(ObpSourceGame Game, string DisplayName, SourceReadinessKind Kind, string? Problem)
{
    public bool Ready => Kind == SourceReadinessKind.Ready;
}
public sealed record SourceReadiness(IReadOnlyList<SourceReadinessEntry> Entries)
{
    public bool Ready => Entries.Count > 0 && Entries.All(entry => entry.Ready);
}
