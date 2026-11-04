using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// Per-world R&C1 retry presentation and environmental death/restart bridge.
/// Persistent player health and the native campaign checkpoint remain owned by
/// their existing sessions. No native threshold, transition or save policy is
/// inferred from this world-scoped adapter.
/// </summary>
internal sealed class Rac1LifecycleWorldHost(
    RuntimeWorld world, Rac1WorldContext context, Rac1WorldPresentation presentation,
    Rac1LifecycleWorldPorts ports) : IDisposable
{
    private PanelContainer? _combatRetry;
    private bool _disposed;

    internal PanelContainer? RetryPanel => _disposed ? null : _combatRetry;

    internal void Reset()
    {
        if (_disposed) return;
        ports.ResetPlayerRetry();
        if (_combatRetry is { } panel && GodotObject.IsInstanceValid(panel))
            panel.QueueFree();
        _combatRetry = null;
    }

    internal void ShowCombatRetry()
    {
        if (_disposed || !ports.WorldStillCurrent() ||
            ports.Health().Probe().DeathCause != Rac1RatchetDeathCause.CombatZeroNanotech ||
            _combatRetry is not null || ports.ActiveDestination() is not { } destination) return;

        // Application policy: a full level reload, not the recovered
        // environmental checkpoint transition.
        var panel = new PanelContainer { Name = "CombatRetry", Size = new(260, 110) };
        var rows = new VBoxContainer();
        rows.AddChild(new Label { Text = "Defeated", HorizontalAlignment = HorizontalAlignment.Center });
        var retry = new Button { Text = "Retry level" };
        retry.Pressed += () =>
        {
            // A retained button callback cannot navigate after its world has
            // retired or a different destination/world has taken its place.
            if (_disposed || !ports.WorldStillCurrent()) return;
            ApplicationPointer.MouseMode = ports.WindowUnfocusable()
                ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
            ports.Navigate(destination);
        };
        rows.AddChild(retry);
        panel.AddChild(rows);
        ports.AttachRetry(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        retry.GrabFocus();
        ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;
        _combatRetry = panel;
    }

    internal bool TryCompleteEnvironmentalRestart(bool automatic)
    {
        if (_disposed || !ports.WorldStillCurrent() || ports.Ordinary() ||
            ports.Player() is not { } player || ResolveEnvironmentalRestart() is not { } restart)
            return false;

        // Exact parity policy is selected by player entry mode, independently
        // of source/world origin. The native health session owns respawn.
        ports.ClearFusionTransients();
        var respawn = ports.Health().Respawn();
        player.Rac1GameplayState = respawn;
        player.ApplyRecoveredRac1Restart(restart.Placement);
        ports.ResetHeldItem();
        ports.RefreshWrenchPresentation();
        string source = automatic ? "automatic" : "development manual";
        context.CombatStatus =
            $"{source} environmental respawn L{world.LevelId} ({restart.Kind}): Nanotech {respawn.Nanotech}";
        ports.RefreshHud();
        GD.Print($"[rac1-gameplay] {context.CombatStatus}");
        return true;
    }

    internal Rac1RestartPlacement? ResolveEnvironmentalRestart()
    {
        var death = ports.Health().Probe();
        if (_disposed || !ports.WorldStillCurrent() || world.Game != ObpSourceGame.Rac1 ||
            ports.Campaign().Session.LevelCheckpoint is not { } checkpoint ||
            checkpoint.NativeLevelId != world.LevelId ||
            !death.HasRecoveredEnvironmentalRespawn)
            return null;

        // Same recovered native checkpoint admission, not an inferred trigger.
        return checkpoint.ResolveEnvironmentalRestart();
    }

    internal void TickEnvironmentalDeath()
    {
        if (_disposed || !ports.WorldStillCurrent() ||
            world.Game != ObpSourceGame.Rac1 || world.LevelId != 0 ||
            world.Environment is not { } environment ||
            ports.Player() is not { } player || ports.Health().Probe().IsDead)
            return;

        double contactSeparation = MeasureContactSeparation(player);
        var dead = ports.Health().TryApplyVeldinEnvironmentalDeath(
            new Rac1VeldinEnvironmentalDeathFacts(
                NativeVerticalPosition: player.GlobalPosition.Y,
                DeathHeight: environment.DeathHeight,
                ContactSeparation: contactSeparation,
                NativeSpecialPlayerState20A4: context.NativePlayerState20A4));
        if (dead is null) return;

        context.LastEnvironmentalDeathBoundary = dead;
        presentation.LastEnvironmentalDeathPosition = player.GlobalPosition;
        context.LastEnvironmentalDeathContactSeparation = contactSeparation;
        player.Rac1GameplayState = dead;
        context.CombatStatus =
            $"Veldin death plane: state 0x{dead.NativePlayerState:x2}, sequence {dead.NativeSequence} frame {dead.NativeSequenceFrame}; Nanotech {dead.Nanotech}";
        ports.RefreshHud();
        GD.Print($"[rac1-gameplay] {context.CombatStatus}");
    }

    internal static double MeasureContactSeparation(PlayerHost player)
    {
        // PlayerHost's CharacterBody origin is its feet. Keep the original
        // downward ray and recovered native death threshold unchanged.
        Vector3 origin = player.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(
            origin + Vector3.Up * 0.1f,
            origin + Vector3.Down * 1024f);
        query.Exclude = new global::Godot.Collections.Array<Rid> { player.GetRid() };
        var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return double.PositiveInfinity;
        return Math.Max(0d, origin.Y - ((Vector3)hit["position"]).Y);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }
}
