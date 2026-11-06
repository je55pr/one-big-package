using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed class GodotRac1EncounterLifecycle(Func<Rac1RatchetNanotechSnapshot> health,
    Action<Rac1GameplayDamageEvent> damage, Func<Control?> retry) : IRac1EncounterLifecycle
{
    public Rac1RatchetNanotechSnapshot ReadNanotech() => health();
    public void ApplyAdmittedPlayerDamage(Rac1GameplayDamageEvent value) => damage(value);
    public void RetryLevel()
    {
        var menu = retry();
        if (menu is null || !GodotObject.IsInstanceValid(menu))
            throw new DiagnosticUnavailableException("The explicit encounter retry control is unavailable.");
        var buttons = menu.GetChildren().OfType<VBoxContainer>().SelectMany(box => box.GetChildren().OfType<Button>()).ToArray();
        if (buttons.Length != 1 || buttons[0].Disabled || !buttons[0].IsVisibleInTree())
            throw new DiagnosticUnavailableException("The encounter retry fixture needs one visible, enabled retry button.");
        buttons[0].EmitSignal(Button.SignalName.Pressed);
    }
}
internal sealed class GodotRac1WrenchApproachFixture(Func<PlayerHost?> player,
    Func<double, Rac1WrenchDirection> facing, float rootHeight) : IRac1WrenchApproachFixture
{
    private PlayerHost Player => player() is { } actor && GodotObject.IsInstanceValid(actor) ? actor
        : throw new DiagnosticUnavailableException("The wrench approach player is unavailable.");
    public double ControlYaw => Player.Rac1ControlYaw;
    public bool Admits(NVector3 target)
    {
        var actor = Player;
        var direction = facing(actor.Rac1CurrentYaw);
        NVector3 forward = new(-(float)direction.X, 0, (float)direction.Y);
        if (forward.LengthSquared() <= 1e-5f) return false;
        forward = NVector3.Normalize(forward);
        var root = actor.GlobalPosition + Vector3.Up * rootHeight;
        return Rac1WrenchHostContactPolicy.Admits(new(root.X, root.Y, root.Z),
            new(forward.X, forward.Y, forward.Z), new(target.X, target.Y, target.Z));
    }
}
internal sealed class GodotRac1EncounterLoot(Func<Rac1DiagnosticLootSnapshot> read) : IRac1DiagnosticLootFacts
{
    public Rac1DiagnosticLootSnapshot ReadLoot() => read();
}
internal sealed class GodotRac1EnemyDiagnosticCapture(Node3D owner, Func<CanvasLayer> overlay,
    Func<PlayerHost?> player, IDiagnosticFrameClock clock) : IRac1EnemyDiagnosticCapture
{
    public async Task CaptureIfRequestedAsync(string name, NVector3 focus, CancellationToken cancellationToken)
    {
        string? directory = System.Environment.GetEnvironmentVariable("OBP_ENEMY_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("[enemy-capture] unavailable: optional image capture requires a rendered post-draw signal.");
            return;
        }
        Directory.CreateDirectory(directory);
        var actor = player() ?? throw new DiagnosticUnavailableException("The enemy capture player is unavailable.");
        var ui = overlay();
        var hud = actor.GetNode<CanvasLayer>("PlayerHud");
        bool visible = ui.Visible, hudVisible = hud.Visible;
        var previousCamera = owner.GetViewport().GetCamera3D();
        var camera = new Camera3D { Current = true, Fov = 55, Far = 10000 };
        owner.AddChild(camera);
        try
        {
            var point = GodotMovementPlayer.Engine(focus);
            ui.Visible = false; hud.Visible = false;
            // Optional OBP_ENEMY_CAPTURE_OFFSETS="x,y,z;x,y,z" writes one image per camera offset (default 3,2,6).
            var offsets = DiagnosticCaptureOffsets.Read("OBP_ENEMY_CAPTURE_OFFSETS", "3,2,6");
            for (int index = 0; index < offsets.Length; index++)
            {
                camera.GlobalPosition = point + offsets[index];
                camera.LookAt(point);
                await clock.NextAsync(DiagnosticFramePhase.Process, cancellationToken);
                await clock.NextAsync(DiagnosticFramePhase.PostDraw, cancellationToken);
                owner.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, offsets.Length == 1 ? name + ".png" : $"{name}-{index}.png"));
            }
        }
        finally
        {
            if (GodotObject.IsInstanceValid(ui)) ui.Visible = visible;
            if (GodotObject.IsInstanceValid(hud)) hud.Visible = hudVisible;
            if (GodotObject.IsInstanceValid(camera)) camera.QueueFree();
            if (GodotObject.IsInstanceValid(previousCamera)) previousCamera.MakeCurrent();
        }
    }
}

/// <summary>Camera offsets for optional diagnostic captures: "x,y,z;x,y,z" from an environment variable, validated.</summary>
internal static class DiagnosticCaptureOffsets
{
    internal static Vector3[] Read(string variable, string fallback)
    {
        string text = System.Environment.GetEnvironmentVariable(variable) ?? "";
        if (string.IsNullOrWhiteSpace(text)) text = fallback; // unset, empty or whitespace: the default offsets
        var tuples = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tuples.Length == 0)
            throw new InvalidOperationException($"{variable} must list at least one \"x,y,z\" camera offset separated by ';'.");
        return [.. tuples.Select(tuple =>
        {
            var parts = tuple.Split(',', StringSplitOptions.TrimEntries);
            var values = new float[3];
            if (parts.Length != 3 || parts.Where((part, i) => !float.TryParse(part, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out values[i]) || !float.IsFinite(values[i])).Any())
                throw new InvalidOperationException($"{variable} entry \"{tuple}\" must be three finite numbers \"x,y,z\".");
            // LookAt toward the focus is undefined when the camera sits exactly above or below it.
            if (values[0] == 0 && values[2] == 0)
                throw new InvalidOperationException($"{variable} entry \"{tuple}\" is directly above or below the focus; give it a horizontal component.");
            return new Vector3(values[0], values[1], values[2]);
        })];
    }
}
