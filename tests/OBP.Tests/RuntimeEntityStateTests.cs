using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class RuntimeEntityStateTests
{
    [Fact]
    public void AuthoredDefinitionAndLiveStateStaySeparate()
    {
        var transform = new RuntimeObjectTransform(Enumerable.Range(0, 16).Select(i => (double)i).ToArray());
        var payload = new RuntimeOpaquePayload("native", [1, 2, 3]);
        var source = new RuntimeDynamicObject(
            "racx", 77, 5, 900, "moby:77", "level:2:moby:5",
            transform, Array.Empty<RuntimeObjectMesh>(), [payload]);

        var state = RuntimeEntityState.FromAuthored(source);
        var inactive = state.WithPresence(RuntimeEntityPresence.Inactive);

        Assert.Equal(RuntimeEntityIdentity.From(source), state.Identity);
        Assert.Equal(RuntimeEntityPresence.Active, state.Presentation.Presence);
        Assert.Equal(RuntimeObjectAnimationRole.Rest, state.Presentation.AnimationRole);
        Assert.Equal(transform, state.Presentation.Transform);
        Assert.Equal(RuntimeEntityPresence.Inactive, inactive.Presentation.Presence);
        Assert.Same(payload, Assert.Single(source.NativePayloads!));
    }

    [Fact]
    public void AuthoredNeutralAnimationRoleSeedsLiveSnapshot()
    {
        var animations = new RuntimeObjectAnimationSet(
            Array.Empty<RuntimeObjectAnimationClip>(), RuntimeObjectAnimationRole.Reaction);
        var source = new RuntimeDynamicObject(
            "racx", 77, 5, null, "moby:77", "level:2:moby:5",
            new RuntimeObjectTransform(new double[16]), Array.Empty<RuntimeObjectMesh>(),
            Animations: animations);

        Assert.Equal(RuntimeObjectAnimationRole.Reaction,
            RuntimeEntityState.FromAuthored(source).Presentation.AnimationRole);
    }

    [Fact]
    public void StateCannotBeAppliedToDifferentAuthoredIdentity()
    {
        RuntimeDynamicObject Source(int index) => new(
            "racx", 77, index, null, "moby:77", $"level:2:moby:{index}",
            new RuntimeObjectTransform(new double[16]), Array.Empty<RuntimeObjectMesh>());

        var first = Source(1);
        var second = Source(2);
        var state = RuntimeEntityState.FromAuthored(first);

        Assert.Throws<InvalidOperationException>(() => state.EnsureMatches(second));
    }
}
