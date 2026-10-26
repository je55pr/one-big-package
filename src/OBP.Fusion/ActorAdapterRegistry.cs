using OBP.Core;

namespace OBP.Fusion;

/// <summary>An admitted native recipe, not a promise of every member of a source's class table.</summary>
public sealed record ActorAdapterDescriptor(ObpSourceGame Source, string Build, int NativeClassId,
    string AdapterId, string NativeContextId, int LevelId, int InitialNativeState)
{
    public void Validate()
    {
        new FusionSourceIdentity(Source, Build, AdapterId).Validate();
        if (Source is not (ObpSourceGame.Rac1 or ObpSourceGame.Rac2 or ObpSourceGame.Rac3) ||
            NativeClassId < 0 || LevelId < 0 || InitialNativeState < 0 ||
            string.IsNullOrWhiteSpace(AdapterId) || string.IsNullOrWhiteSpace(NativeContextId))
            throw new ArgumentException("Invalid source-qualified actor adapter recipe.");
    }

    internal bool Admits(ActorPlacement placement) => Source == placement.Source.Game && Build == placement.Source.Build &&
        NativeClassId == placement.NativeClassId && AdapterId == placement.AdapterId && NativeContextId == placement.NativeContextId &&
        LevelId == placement.LevelId && InitialNativeState == placement.InitialNativeState;
}

/// <summary>Composition-time factories. ActorRuntimeSet remains the only live actor/tick/damage registry.</summary>
public sealed class ActorAdapterRegistry<TFactory> where TFactory : class
{
    private readonly List<(ActorAdapterDescriptor Descriptor, TFactory Factory)> _registrations = [];

    public void Register(ActorAdapterDescriptor descriptor, TFactory factory)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(factory);
        descriptor.Validate();
        if (_registrations.Any(item => item.Descriptor == descriptor))
            throw new ArgumentException("Duplicate source-qualified actor adapter recipe.");
        _registrations.Add((descriptor, factory));
    }

    public TFactory Resolve(ActorPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        placement.Validate();
        return _registrations.FirstOrDefault(item => item.Descriptor.Admits(placement)).Factory
            ?? throw new NotSupportedException("No actor adapter admits this source, build, native family and context.");
    }
}
