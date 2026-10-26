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
        menu.GetChildren().OfType<VBoxContainer>().Single().GetChildren().OfType<Button>().Single().EmitSignal(Button.SignalName.Pressed);
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
            throw new DiagnosticUnavailableException("Optional enemy image capture requires a rendered post-draw signal.");
        Directory.CreateDirectory(directory);
        var actor = player() ?? throw new DiagnosticUnavailableException("The enemy capture player is unavailable.");
        var ui = overlay();
        var hud = actor.GetNode<CanvasLayer>("PlayerHud");
        bool visible = ui.Visible, hudVisible = hud.Visible;
        var camera = new Camera3D { Current = true, Fov = 55, Far = 10000 };
        owner.AddChild(camera);
        try
        {
            var point = GodotMovementPlayer.Engine(focus);
            camera.GlobalPosition = point + new Vector3(3, 2, 6);
            camera.LookAt(point);
            ui.Visible = false; hud.Visible = false;
            await clock.NextAsync(DiagnosticFramePhase.Process, cancellationToken);
            await clock.NextAsync(DiagnosticFramePhase.PostDraw, cancellationToken);
            owner.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, name + ".png"));
        }
        finally
        {
            if (GodotObject.IsInstanceValid(ui)) ui.Visible = visible;
            if (GodotObject.IsInstanceValid(hud)) hud.Visible = hudVisible;
            if (GodotObject.IsInstanceValid(camera)) camera.QueueFree();
            if (GodotObject.IsInstanceValid(actor) && GodotObject.IsInstanceValid(actor.Camera)) actor.Camera.MakeCurrent();
        }
    }
}
