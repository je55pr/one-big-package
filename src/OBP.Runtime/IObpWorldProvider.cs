using OBP.Core;

namespace OBP.Runtime;

/// <summary>
/// Engine-independent bridge from a source-game destination catalogue to a
/// neutral <see cref="RuntimeWorld"/>. The host owns the local source path; a
/// game-specific provider owns native destination validation and import.
///
/// This deliberately keeps Godot out of the routing layer. R&C1 and UYA can
/// implement the same contract when their production C# importers are ready.
/// </summary>
public interface IObpWorldProvider
{
    /// <summary>The source game whose destinations this provider can import.</summary>
    ObpSourceGame Game { get; }

    /// <summary>The supported retail build this provider was verified against.</summary>
    string BuildId { get; }

    /// <summary>Read-only destination discovery for <see cref="Game"/> and <see cref="BuildId"/>.</summary>
    IObpDestinationCatalogue Catalogue { get; }

    /// <summary>Whether <paramref name="destination"/> belongs to this provider and has a production importer.</summary>
    bool CanLoad(ObpDestination destination);

    /// <summary>
    /// Imports <paramref name="destination"/> from the host-owned <paramref name="sourcePath"/> into a neutral world.
    /// Callers check <see cref="CanLoad"/> first; an unsupported destination is a failure, not a placeholder world.
    /// </summary>
    RuntimeWorld Load(string sourcePath, ObpDestination destination);
}
