using Godot;

namespace OBP.Game;

/// <summary>World-owned simulation phase: after native input/gameplay (0), before replay checkpoint capture (10000).</summary>
internal sealed partial class EffectNativeClockDriver : Node
{
    public Action<double> Advance { get; init; } = null!;
    public override void _PhysicsProcess(double delta) => Advance(delta);
}
