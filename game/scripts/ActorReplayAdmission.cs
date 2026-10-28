using OBP.Core;
using OBP.Fusion;

namespace OBP.Game;

internal sealed record ActorReplayAdapter(ActorAdapterDescriptor Descriptor, bool Supported);

/// <summary>Source-qualified replay admission. A registered factory must explicitly supply its deterministic witness.</summary>
internal static class ActorReplayAdmission
{
    internal static string Require(ActorRegistration registration, IEnumerable<ActorReplayAdapter> factories)
    {
        var target = registration.Instance.Target;
        bool original = registration.AdapterId == "source-world/rac1" && target.SourceGame == ObpSourceGame.Rac1 && target.NativeClassId is 500 or 501 or 511 or 749 or 1440 ||
            registration.AdapterId == "source-world/rac2" && target.SourceGame == ObpSourceGame.Rac2 && target.NativeClassId == 2827 ||
            registration.AdapterId == "source-world/rac3" && target.SourceGame == ObpSourceGame.Rac3 && target.NativeClassId == 500;
        bool registered = factories.Any(factory => factory.Supported && factory.Descriptor.Source == target.SourceGame &&
            factory.Descriptor.Build == registration.Source.Build && factory.Descriptor.NativeClassId == target.NativeClassId &&
            factory.Descriptor.AdapterId == registration.AdapterId && factory.Descriptor.NativeContextId == registration.NativeContextId);
        if (!original && !registered)
            throw new InvalidOperationException($"Capture is unavailable for native actor adapter {registration.AdapterId}, class {target.NativeClassId}; no deterministic fixture is admitted.");
        return registration.AdapterId;
    }
}
