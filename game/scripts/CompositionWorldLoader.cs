using Godot;
using OBP.Composition;
using OBP.Core;
using OBP.Runtime;

namespace OneBigPackage;

/// <summary>
/// Resolves a neutral <see cref="WorldPlacement"/> to a reconstructed
/// <see cref="RuntimeWorld"/> for the composition lab, through the shared
/// <see cref="ObpWorldProviderRegistry"/> (<see cref="TrilogyWorldProviders"/>).
///
/// <para>
/// The lab holds no game-specific knowledge: a placement names a canonical
/// destination id (<c>rac1:LEVEL0</c>, <c>rac2:LEVEL1</c>, <c>rac3:TABLE1</c>), the
/// registry routes it to the right <see cref="IObpWorldProvider"/>, and that
/// provider decodes it from the registered retail source. R&amp;C1, Going Commando,
/// and UYA are all exercised through this same neutral provider path; the loader
/// contains no per-game decode or alignment behavior.
/// </para>
/// </summary>
public sealed class CompositionWorldLoader
{
    private readonly System.Collections.Generic.Dictionary<ObpSourceGame, string> _sourceByGame = new();

    public void RegisterSource(ObpSourceGame game, string path) => _sourceByGame[game] = path;

    /// <summary>Register from a loose token ("gc" / "rc1" / "uya" / "rac2" …).</summary>
    public void RegisterSource(string gameTag, string path)
    {
        if (TryParseGame(gameTag, out var game))
        {
            _sourceByGame[game] = path;
        }
    }

    public bool CanLoad(WorldPlacement placement) =>
        ResolveDestination(placement) is { } d
        && _sourceByGame.ContainsKey(d.Game)
        && (TrilogyWorldProviders.Find(d.Game)?.CanLoad(d) ?? false);

    public RuntimeWorld Load(WorldPlacement placement)
    {
        var destination = ResolveDestination(placement)
            ?? throw new System.InvalidOperationException(
                $"composition world '{placement.Id}': could not resolve a canonical destination " +
                $"(destinationId='{placement.DestinationId}', sourceGame='{placement.SourceGame}', levelId={placement.LevelId}).");

        if (!_sourceByGame.TryGetValue(destination.Game, out var sourcePath))
        {
            throw new System.InvalidOperationException(
                $"composition world '{placement.Id}': no retail source registered for {destination.Game} " +
                $"({destination.DestinationId}). Pass --gc-iso / --rac1-iso / --uya-iso.");
        }

        if (TrilogyWorldProviders.Find(destination.Game) is not { } provider)
        {
            throw new System.InvalidOperationException(
                $"composition world '{placement.Id}': no production world provider is registered for {destination.Game} yet.");
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var world = provider.Load(sourcePath, destination);
        sw.Stop();
        GD.Print($"[CompositionLoader] '{placement.Id}' = {world.DisplayName} " +
                 $"({world.Meshes.Count} meshes / {world.TotalRenderTriangles:N0} tris) " +
                 $"via {destination.DestinationId} in {sw.ElapsedMilliseconds} ms");
        return world;
    }

    /// <summary>
    /// A placement's canonical destination: its explicit <c>destinationId</c>
    /// first, else synthesised from <c>sourceGame</c> + <c>levelId</c> (GC:
    /// <c>rac2:LEVEL{n}</c>) for the pre-registry authoring path.
    /// </summary>
    public ObpDestination? ResolveDestination(WorldPlacement placement)
    {
        if (!string.IsNullOrWhiteSpace(placement.DestinationId)
            && TrilogyWorldProviders.Resolve(placement.DestinationId!) is { } byId)
        {
            return byId;
        }

        if (placement.LevelId is { } level && TryParseGame(placement.SourceGame ?? "gc", out var game))
        {
            string prefix = game switch
            {
                ObpSourceGame.Rac1 => "rac1",
                ObpSourceGame.Rac2 => "rac2",
                ObpSourceGame.Rac3 => "rac3",
                _ => "rac2",
            };
            return TrilogyWorldProviders.Resolve($"{prefix}:LEVEL{level}");
        }

        return null;
    }

    public static bool TryParseGame(string tag, out ObpSourceGame game)
    {
        switch (tag.Trim().ToLowerInvariant())
        {
            case "gc" or "rac2" or "going commando" or "r&c2" or "rc2":
                game = ObpSourceGame.Rac2;
                return true;
            case "rc1" or "rac1" or "r&c1" or "ratchet & clank":
                game = ObpSourceGame.Rac1;
                return true;
            case "uya" or "rac3" or "up your arsenal" or "r&c3" or "rc3":
                game = ObpSourceGame.Rac3;
                return true;
            default:
                game = default;
                return false;
        }
    }
}
