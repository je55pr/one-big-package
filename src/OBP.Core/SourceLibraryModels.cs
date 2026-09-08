namespace OBP.Core;

/// <summary>
/// One retail source build that an OBP host knows how to recognize. The
/// definition is game-neutral: title-specific projects provide their authority
/// identity and exact payload size, while the shared PS2 source library performs
/// the bounded boot/serial probe.
/// </summary>
public sealed record ObpSourceDefinition(
    string DisplayName,
    ObpBuildIdentity Authority,
    long ExpectedSizeBytes)
{
    public ObpSourceGame Game => Authority.Game;
}

/// <summary>Result of the cheap bounded source-identification pass.</summary>
public sealed record ObpSourceProbe(
    bool Recognized,
    bool Supported,
    ObpSourceDefinition? Definition,
    string? DiscSerial,
    long SizeBytes,
    bool SizeMatches,
    string? Problem);

/// <summary>
/// A source currently attached to the running OBP session. <see cref="Path"/>
/// is host-local configuration only; retail payload bytes are never copied into
/// OBP storage.
/// </summary>
public sealed record ObpAttachedSource(
    string Path,
    ObpSourceDefinition Definition,
    string DiscSerial,
    long SizeBytes)
{
    public ObpSourceGame Game => Definition.Game;
    public ObpBuildIdentity Identity => Definition.Authority;
}

/// <summary>The intentionally tiny persisted form of an attached source.</summary>
public sealed record ObpSavedSource(
    ObpSourceGame Game,
    string BuildId,
    string Path);
