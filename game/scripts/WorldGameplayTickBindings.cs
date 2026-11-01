using Godot;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Narrow host actions required by one world's tick schedule. These bindings
/// are supplied at activation, never looked up through a global root locator.
/// </summary>
internal sealed record WorldGameplayTickPorts(
    Func<bool> MovementSuspended,
    Func<bool> ReplayWaitingForEntry,
    Func<bool> PlayerDead,
    Action PlayerReactions,
    Action<double> ActorPhysics,
    Action<double> ScenePresentation,
    Action HudPresentation);

/// <summary>
/// Binds the existing world/source owners to the exact order used by the
/// Godot frame callbacks. Source modules register their own native clocks.
/// </summary>
internal static class WorldGameplayTickBindings
{
    internal static void Activate(WorldSceneLease<GodotWorldResources> entry,
        WorldGameplayTickPorts ports)
    {
        var resources = entry.Resources;
        var ticks = new GameplayTickScheduler<InputEvent>(entry.Lifetime);
        resources.BindGameplayTicks(ticks);

        // This preparation stage ran on every process frame, even while a
        // capture was waiting to enter its scene.
        ticks.Register(GameplayTickPhase.Prepare, "effects-prepare", "WorldEffectHost", 0,
            _ => resources.Effects?.PrepareTick());

        resources.Rac1Gameplay?.RegisterTicks(ticks, ports.MovementSuspended,
            ports.ReplayWaitingForEntry, ports.PlayerDead);

        ticks.Register(GameplayTickPhase.Physics, "player-audio", "PlayerAudio", 10,
            _ => ports.PlayerReactions());
        ticks.Register(GameplayTickPhase.Physics, "actors", "ActorWorldHost", 30,
            ports.ActorPhysics);

        ticks.Register(GameplayTickPhase.Presentation, "scene", "WorldHost", 0,
            ports.ScenePresentation);
        ticks.Register(GameplayTickPhase.Presentation, "effects-render", "WorldEffectHost", 10,
            _ => resources.Effects?.Render());

        resources.Rac2Gameplay?.RegisterTicks(ticks);
        // R&C3's class-500 consumer is event-driven, not a manufactured frame tick.

        ticks.Register(GameplayTickPhase.Hud, "world-hud", "HudPresentation", 0,
            _ => ports.HudPresentation());
        ticks.Activate();
    }
}
