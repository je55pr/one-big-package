using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using static OBP.Game.Rac1EnemyCoordinates;

namespace OBP.Game;

internal sealed partial class Rac1EnemyHost
{
    private Rac1FxMaterials? _robotBeamMaterials;

    /// <summary>Fx textures the class-1471 beam draws (glow 0x28, beam 0x29, end/robot glow 0xb); read from the active level's own bank.</summary>
    internal void ConfigureRac1RobotEffects(Rac1LevelCore.Core core)
    {
        _robotBeamMaterials?.Dispose();
        _robotBeamMaterials = new(Rac1FxTextures.Read(core, new HashSet<int>
            { Rac1BeamSlot.GlowTexture, Rac1BeamSlot.BeamTexture, Rac1BeamSlot.EndpointTexture }));
    }

    /// <summary>
    /// Once per native tick for a Veldin shooting robot: its glow billboard is always
    /// present, and the plasma beam exists only in firing state 5 (the effect slot is
    /// claimed before and released after, so phases restart on every volley).
    /// </summary>
    internal void UpdateRac1RobotEffects(Rac1HostedEnemy enemy, bool firing)
    {
        if (_robotBeamMaterials is null) return;
        if (!Owns(enemy) || enemy.DeathPresented) { ClearRac1RobotEffects(enemy); return; }
        var view = enemy.Beam ??= new(_sceneResult.Root, _robotBeamMaterials, $"EnemyBeam_i{enemy.Node.Source.InstanceIndex}");
        Vector3 muzzle = EnemySocket(enemy);
        view.SetGlow(enemy.Animation.SecondSocket is { } lamp
            ? ToEffectNative(enemy.Node.Root.ToGlobal(FromEffectNative(lamp))) : null);
        if (firing)
        {
            // 0x2e0b88 state 5: from socket 0 along the heading (flattened), ContactLength long, shortened by the first hit.
            Vector3 forward = EnemyForward(enemy.Node) with { Y = 0 };
            Vector3 end = muzzle + (forward.LengthSquared() > 1e-8f ? forward.Normalized() : Vector3.Forward) * Rac1Class1440Session.ContactLength;
            var query = PhysicsRayQueryParameters3D.Create(muzzle, end);
            query.Exclude = new global::Godot.Collections.Array<Rid> { enemy.Body.GetRid() };
            var hit = enemy.Body.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.TryGetValue("position", out var position)) end = (Vector3)position;
            view.StepBeam(ToEffectNative(muzzle), ToEffectNative(end), Random);
        }
        else view.StopBeam();
        view.Present(_sceneResult.Root.GetViewport()?.GetCamera3D()?.GlobalPosition ?? muzzle + Vector3.Up * 5, Random);
    }

    internal void ClearRac1RobotEffects(Rac1HostedEnemy enemy)
    {
        if (enemy.Beam is not { } view) return;
        enemy.Beam = null;
        view.Dispose();
    }

    private void ResetRac1RobotEffects()
    {
        foreach (var enemy in Roster.Values) ClearRac1RobotEffects(enemy);
        _robotBeamMaterials?.Dispose();
        _robotBeamMaterials = null;
    }
}
