using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using static OBP.Game.Rac1EnemyCoordinates;

namespace OBP.Game;

internal sealed partial class Rac1EnemyHost
{
    private Rac1FxMaterials? _robotBeamMaterials;
    private Rac1EffectAssets? _robotParticleAssets;
    private Rac1ParticleSession? _robotParticles;
    private Rac1ParticleView? _robotParticleView;
    private uint _robotParticleFrame = uint.MaxValue;
    /// <summary>Presentation-only stream, forked once from the shared effect stream so robot visuals cannot shift crate or pickup rolls.</summary>
    private Rac1PickupRandom? _robotEffectRandom;
    private Rac1PickupRandom RobotRandom => _robotEffectRandom ??= Random.Fork();

    /// <summary>Fx textures the class-1471 beam draws (glow 0x28, beam 0x29, end/robot glow 0xb) and the Part texture of its sparks; read from the active level's own banks.</summary>
    internal void ConfigureRac1RobotEffects(Rac1LevelCore.Core core)
    {
        _robotBeamMaterials?.Dispose();
        _robotBeamMaterials = new(Rac1FxTextures.Read(core, new HashSet<int>
            { Rac1BeamSlot.GlowTexture, Rac1BeamSlot.BeamTexture, Rac1BeamSlot.EndpointTexture }));
        _robotParticleAssets = Rac1EffectAssets.DecodeSparks(core);
        _robotParticles = new(_robotParticleAssets, RobotRandom);
        _robotParticleView = new Rac1ParticleView { Name = "Rac1RobotSparks" };
        _sceneResult.Root.AddChild(_robotParticleView);
    }

    /// <summary>
    /// Once per native tick for a Veldin shooting robot: its glow billboard and lamp flash are always present, and
    /// the plasma beam (with its end sparks) exists only in firing state 5 (the effect slot is claimed before and
    /// released after, so phases restart on every volley).
    /// </summary>
    internal void UpdateRac1RobotEffects(Rac1HostedEnemy enemy, bool firing)
    {
        if (_robotBeamMaterials is null) return;
        if (!Owns(enemy) || enemy.DeathPresented) { ClearRac1RobotEffects(enemy); return; }
        AgeRac1RobotParticles(); // the first robot of each native tick ages last tick's sparks before emitting new ones
        var view = enemy.Beam ??= new(_sceneResult.Root, _robotBeamMaterials, $"EnemyBeam_i{enemy.Node.Source.InstanceIndex}");
        Vector3 muzzle = EnemySocket(enemy);
        System.Numerics.Vector3? lamp = enemy.Animation.SecondSocket is { } local
            ? ToEffectNative(enemy.Node.Root.ToGlobal(FromEffectNative(local))) : null;
        view.SetGlow(lamp);
        if (lamp is { } lampPoint && _robotParticles is not null) Rac1RobotLampFlash.Emit(_robotParticles, RobotRandom, lampPoint);
        if (firing)
        {
            // 0x2e0b88 state 5: from socket 0 along the heading (flattened), ContactLength long, shortened by the first hit.
            Vector3 forward = EnemyForward(enemy.Node) with { Y = 0 };
            Vector3 end = muzzle + (forward.LengthSquared() > 1e-8f ? forward.Normalized() : Vector3.Forward) * Rac1Class1440Session.ContactLength;
            var query = PhysicsRayQueryParameters3D.Create(muzzle, end);
            query.Exclude = new global::Godot.Collections.Array<Rid> { enemy.Body.GetRid() };
            query.HitFromInside = true; // as the damage query for this attack: a muzzle inside a collider clips the beam
            var hit = enemy.Body.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.TryGetValue("position", out var position)) end = (Vector3)position;
            view.StepBeam(ToEffectNative(muzzle), ToEffectNative(end), RobotRandom);
            if (_robotParticles is not null) view.EmitSparks(_robotParticles, RobotRandom);
        }
        else view.StopBeam();
        view.Present(_sceneResult.Root.GetViewport()?.GetCamera3D()?.GlobalPosition ?? muzzle + Vector3.Up * 5, RobotRandom);
    }

    /// <summary>Ages every spark once per native frame, whichever caller reaches the new frame first.</summary>
    private void AgeRac1RobotParticles()
    {
        if (_robotParticles is null || _robotParticleFrame == NativeFrame) return;
        _robotParticleFrame = NativeFrame;
        var camera = _sceneResult.Root.GetViewport()?.GetCamera3D();
        _robotParticles.Step(ToEffectNative(camera?.GlobalPosition ?? Vector3.Zero), enforceLevelBounds: false);
    }

    private void PresentRac1RobotParticles()
    {
        if (_robotParticles is not null && _robotParticleView is not null && _robotParticleAssets is not null)
            _robotParticleView.Render(_robotParticles, _robotParticleAssets.PartTextures);
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
        _robotParticles?.Clear(); _robotParticles = null; _robotParticleAssets = null;
        if (_robotParticleView is not null && GodotObject.IsInstanceValid(_robotParticleView) && !_robotParticleView.IsQueuedForDeletion()) _robotParticleView.QueueFree();
        _robotParticleView = null; _robotParticleFrame = uint.MaxValue; _robotEffectRandom = null;
    }
}
