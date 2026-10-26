using OBP.Core;
using OBP.Fusion;
using OBP.IO;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>The composition list, deliberately separate from world activation and native family implementations.</summary>
internal static class ActorFactoryRegistrations
{
    public static IReadOnlyDictionary<ObpSourceGame, Func<string, int, RuntimeWorld>> SourceImports { get; } =
        new Dictionary<ObpSourceGame, Func<string, int, RuntimeWorld>>
        {
            [ObpSourceGame.Rac1] = (path, level) => { using var reader = new FileRandomAccessReader(path); return OBP.RAC1.Rac1WorldImport.Build(reader, level); },
            [ObpSourceGame.Rac2] = (path, level) => { using var reader = new FileRandomAccessReader(path); return OBP.RAC2.Rac2WorldImport.Build(reader, level); },
            [ObpSourceGame.Rac3] = (path, level) => { using var reader = new FileRandomAccessReader(path); return OBP.RAC3.Rac3WorldImport.Build(reader, level); },
        };

    public static ActorAdapterRegistry<IActorFactory> Create(ActorRuntimeSet actors, params IActorFactory[] factories)
    {
        var registry = new ActorAdapterRegistry<IActorFactory>();
        foreach (var factory in factories)
        {
            registry.Register(factory.Descriptor, factory);
            factory.RegisterUpdateGroups(actors);
        }
        return registry;
    }
}
