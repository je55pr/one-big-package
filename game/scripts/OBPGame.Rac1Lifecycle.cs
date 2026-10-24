using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OneBigPackage;

public partial class OBPGame
{
    private PanelContainer? _rac1CombatRetry;

    private void ResetRac1CombatRetry()
    {
        if (_rac1CombatRetry is { } panel && IsInstanceValid(panel)) panel.QueueFree();
        _rac1CombatRetry = null;
    }

    private void ShowRac1CombatRetry()
    {
        if (_rac1Nanotech.Probe().DeathCause != Rac1RatchetDeathCause.CombatZeroNanotech ||
            _rac1CombatRetry is not null || _activeDestination is not { } destination) return;
        // OBP application policy: an explicit full level reload. This does not
        // reuse the separately recovered environmental checkpoint transition.
        var panel = new PanelContainer { Name = "CombatRetry", Size = new(260, 110) };
        var rows = new VBoxContainer();
        rows.AddChild(new Label { Text = "Defeated", HorizontalAlignment = HorizontalAlignment.Center });
        var retry = new Button { Text = "Retry level" };
        retry.Pressed += () =>
        {
            Input.MouseMode = GetWindow().Unfocusable ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
            OnDestinationChosen(destination);
        };
        rows.AddChild(retry); panel.AddChild(rows);
        _ui.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        retry.GrabFocus();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _rac1CombatRetry = panel;
    }

    private bool TryCompleteRac1EnvironmentalRestart(bool automatic)
    {
        var death = _rac1Nanotech.Probe();
        if (_world is not { Game: "rac1" } world || _player is null ||
            _rac1CampaignSession.LevelCheckpoint is not { } checkpoint ||
            checkpoint.NativeLevelId != world.LevelId ||
            !death.HasRecoveredEnvironmentalRespawn)
            return false;

        // The recovered restart boundary resets Nanotech, placement, heading and
        // player motion. Godot does not infer a checkpoint trigger: level 0 reaches
        // this naturally through the recovered Veldin gate; level 2 is admitted only
        // when a separately identified checkpoint/death witness has been supplied.
        Rac1RestartPlacement restart = checkpoint.ResolveEnvironmentalRestart();
        ClearFusionTransients();
        var respawn = _rac1Nanotech.Respawn();
        _player.Rac1GameplayState = respawn;
        _player.ApplyRecoveredRac1Restart(restart.Placement);
        _rac1HeldItem.ResetForLevelEntry();
        RefreshRac1WrenchPresentationVisibility();
        string source = automatic ? "automatic" : "development manual";
        _rac1CombatStatus =
            $"{source} environmental respawn L{world.LevelId} ({restart.Kind}): Nanotech {respawn.Nanotech}";
        RefreshRac1HudState();
        GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
        return true;
    }

    private void TickRac1VeldinEnvironmentalDeath()
    {
        if (_world is not { Game: "rac1", LevelId: 0, Environment: { } environment } ||
            _player is null || _rac1Nanotech.Probe().IsDead)
            return;

        double contactSeparation = MeasureRac1ContactSeparation();
        var dead = _rac1Nanotech.TryApplyVeldinEnvironmentalDeath(
            new Rac1VeldinEnvironmentalDeathFacts(
                NativeVerticalPosition: _player.GlobalPosition.Y,
                DeathHeight: environment.DeathHeight,
                ContactSeparation: contactSeparation,
                NativeSpecialPlayerState20A4: _rac1NativePlayerState20A4));
        if (dead is null) return;

        _rac1LastEnvironmentalDeathBoundary = dead;
        _rac1LastEnvironmentalDeathPosition = _player.GlobalPosition;
        _rac1LastEnvironmentalDeathContactSeparation = contactSeparation;
        _player.Rac1GameplayState = dead;
        _rac1CombatStatus = $"Veldin death plane: state 0x{dead.NativePlayerState:x2}, sequence {dead.NativeSequence} frame {dead.NativeSequenceFrame}; Nanotech {dead.Nanotech}";
        RefreshRac1HudState();
        GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
    }

    private double MeasureRac1ContactSeparation()
    {
        if (_player is null) return double.NaN;

        // PlayerHost's CharacterBody origin is its feet. A downward ray therefore
        // supplies the live host contact-gap fact without moving the recovered threshold.
        Vector3 origin = _player.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(
            origin + Vector3.Up * 0.1f,
            origin + Vector3.Down * 1024f);
        query.Exclude = new global::Godot.Collections.Array<Rid> { _player.GetRid() };
        var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return double.PositiveInfinity;

        return Math.Max(0d, origin.Y - ((Vector3)hit["position"]).Y);
    }

}
