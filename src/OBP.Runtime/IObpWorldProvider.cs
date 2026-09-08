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
    ObpSourceGame Game { get; }
    string BuildId { get; }
    IObpDestinationCatalogue Catalogue { get; }

    bool CanLoad(ObpDestination destination);

    RuntimeWorld Load(string sourcePath, ObpDestination destination);
}
