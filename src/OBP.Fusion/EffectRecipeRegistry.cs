using OBP.Core;
using OBP.Runtime.Effects;

namespace OBP.Fusion;

/// <summary>OBP registration/admission only. Native data and construction remain in source implementations.</summary>
public sealed class EffectRecipeRegistry
{
    private readonly Dictionary<ObpSourceGame, (string Build, RuntimeEffectProblem? Problem)> _sources = [];
    private readonly Dictionary<RuntimeEffectRecipeKey, IRuntimeEffectFactory> _factories = [];
    public void Source(ObpSourceGame game, string build, RuntimeEffectProblem? problem = null)
    {
        if (game is not (ObpSourceGame.Rac1 or ObpSourceGame.Rac2 or ObpSourceGame.Rac3) || string.IsNullOrWhiteSpace(build) ||
            problem is not null && (problem.Availability == RuntimeEffectAvailability.Ready || string.IsNullOrWhiteSpace(problem.Reason)))
            throw new ArgumentException("Invalid effect source availability.");
        _sources[game] = (build, problem);
    }
    public void Register(IRuntimeEffectFactory factory)
    {
        factory.Key.Validate();
        if (factory.NativeTicksPerSecond is < 1 or > 240 || factory.MaximumEmissionTicks is < 1)
            throw new ArgumentException("Unsupported effect clock or emission bound.");
        foreach (var texture in factory.Textures)
            if (texture.Source.Game != factory.Key.Source.Game || texture.Source.BuildId != factory.Key.Source.BuildId ||
                texture.Source.LevelId != factory.Key.Source.LevelId || string.IsNullOrWhiteSpace(texture.Source.AssetKind) || string.IsNullOrWhiteSpace(texture.Source.OriginalId))
                throw new ArgumentException("Effect texture bindings must retain their own source/build/bank/asset identity.");
        if (factory.Textures.Select(t => t.Source).Distinct().Count() != factory.Textures.Count)
            throw new ArgumentException("An effect factory cannot bind two images to the same qualified texture identity.");
        if (!_factories.TryAdd(factory.Key, factory)) throw new ArgumentException("Duplicate effect recipe.");
    }
    public RuntimeEffectProblem? Resolve(RuntimeEffectRecipeKey key, out IRuntimeEffectFactory? factory)
    {
        key.Validate(); factory = null;
        if (!_sources.TryGetValue(key.Source.Game, out var source))
            return new(RuntimeEffectAvailability.MissingSource, $"Attach {SourceIdentityWire.FormatSource(key.Source.Game)} in Game Sources.");
        if (source.Problem is { } problem) return problem;
        if (source.Build != key.Source.BuildId)
            return new(RuntimeEffectAvailability.WrongBuild, "The effect requires a different original source build.");
        if (!_factories.TryGetValue(key, out factory))
            return new(RuntimeEffectAvailability.UnsupportedRecipe, "This source-qualified effect recipe is not registered.");
        return null;
    }
}
