using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    private readonly HashSet<Rac1MobyRuntimeKey> _rac1ChickenCargo = [];
    private void StepRac1Vacuum()
    {
        if (_rac1AdditionalWeapons is null || _player is null) return;
        var muzzle = EquipmentMuzzle(Rac1WeaponId.SuckCannon);
        if (_rac1AdditionalWeapons.Vacuuming)
        {
            if (_rac1WeaponWorldTick % 3 == 0)
            {
                var mote = EquipmentParticle(27, new(.25f, .7f, 1, .6f), .12f);
                _sceneResult!.Root.AddChild(mote);
                mote.GlobalPosition = muzzle + EquipmentForward * 3 + new Vector3(
                    Rac1EffectRandom.Signed(0, .8f), Rac1EffectRandom.Signed(0, .6f), Rac1EffectRandom.Signed(0, .8f));
                _rac1WeaponMotes.Add(new(mote, (muzzle - mote.GlobalPosition) / 18));
            }
            // Bounded OBP eligibility adapter for opening Veldin. Full native
            // capture callbacks remain unpromoted: accept small class749 and
            // ordinary wood crates; do not vacuum large robots or metal crates.
            foreach (var target in EquipmentTargets.Where(node => node.Source.NativeClassId is 749 or 500 ||
                _rac1Chickens.ContainsKey(node.Source.InstanceIndex)).ToArray())
            {
                var point = target.Root.GlobalPosition + Vector3.Up * .4f;
                var offset = point - muzzle;
                if (offset.Length() > Rac1WeaponReconstructionPolicy.SuckCaptureRange || offset.LengthSquared() > .01f &&
                    offset.Normalized().Dot(EquipmentForward) < Rac1WeaponReconstructionPolicy.SuckCaptureConeDot ||
                    !BombHasLineOfSight(muzzle, point)) continue;
                var key = new Rac1MobyRuntimeKey(target.Source.NativeClassId, target.Source.InstanceIndex);
                if (!_rac1AdditionalWeapons.TryCapture(key)) break;
                if (_rac1Chickens.ContainsKey(target.Source.InstanceIndex)) _rac1ChickenCargo.Add(key);
                var visual = CloneEquipmentCargo(target);
                _rac1CargoModels.Add(key, visual);
                _rac1Captures.Add(new(key, visual, visual.GlobalPosition, visual.Scale));
                var instance = Rac1World.Context.MobyRuntime.Require(target.Source);
                // Capture is a removal, not an invented damage/bolt reward.
                Rac1World.Context.MobyRuntime.SetNativeState(instance, Rac1MobyRuntime.TerminalNativeStateFe);
                Rac1World.Context.MobyRuntime.Terminalize(instance);
                Rac1World.Enemies.SetRac1HostedPresence(target, RuntimeEntityPresence.Inactive);
                if (Rac1World.Enemies.Roster.TryGetValue(target.Source.InstanceIndex, out var enemy))
                {
                    enemy.RewardEmitted = enemy.DeathPresented = true;
                    enemy.Body.CollisionLayer = enemy.Body.CollisionMask = 0; Rac1World.Enemies.SetRac1EnemyFlame(enemy, false);
                }
                else DisableRac1CrateCollider(target.Source.InstanceIndex);
                PlayRac1PickupSound(849, 5, point); RefreshRac1HudState();
            }
        }
        foreach (var capture in _rac1Captures.ToArray())
        {
            if (!IsInstanceValid(capture.Visual)) { _rac1Captures.Remove(capture); continue; }
            float amount = Math.Clamp(++capture.Age / (float)Rac1WeaponReconstructionPolicy.SuckCaptureTicks, 0, 1);
            float eased = amount * amount;
            capture.Visual.GlobalPosition = capture.Origin.Lerp(muzzle, eased);
            capture.Visual.Scale = capture.Scale * MathF.Max(.02f, 1 - eased);
            capture.Visual.RotateY(.15f);
            if (amount >= 1) { capture.Visual.Visible = false; _rac1Captures.Remove(capture); }
        }
    }

    // Called both by world retirement and by movement-state restoration.
    // A prior capture may still own a visual while the new save has no cargo.
    private void RetireRac1CapturedCargoPresentation()
    {
        foreach (var node in _rac1Captures.Select(capture => capture.Visual).Concat(_rac1CargoModels.Values).Distinct())
            if (IsInstanceValid(node)) node.QueueFree();
        _rac1Captures.Clear();
        _rac1CargoModels.Clear();
    }

    private Node3D CloneEquipmentCargo(RuntimeWorldScene.DynamicObjectNode source)
    {
        var key = new Rac1MobyRuntimeKey(source.Source.NativeClassId, source.Source.InstanceIndex);
        if (_rac1ChickenCargo.Contains(key) && !_rac1Chickens.ContainsKey(source.Source.InstanceIndex))
        {
            var restored = CreateEquipmentWorldView(270); restored.GlobalPosition = source.Root.GlobalPosition; return restored;
        }
        var root = new Node3D { Name = $"Rac1SuckCargo_{source.Source.NativeClassId}_{source.Source.InstanceIndex}" };
        var visual = _rac1Chickens.TryGetValue(source.Source.InstanceIndex, out var chicken) ? chicken.View : source.Root;
        foreach (var mesh in visual.GetChildren().OfType<MeshInstance3D>())
            root.AddChild(new MeshInstance3D
            {
                Mesh = mesh.Mesh,
                MaterialOverride = mesh.MaterialOverride,
                Transform = source.Root.GlobalTransform.AffineInverse() * mesh.GlobalTransform
            });
        _sceneResult!.Root.AddChild(root); root.GlobalTransform = source.Root.GlobalTransform;
        return root;
    }

    private void FireRac1CapturedMoby(Rac1AdditionalWeaponSession.Shot shot)
    {
        if (shot.Cargo is not { } key) return;
        if (!_rac1CargoModels.Remove(key, out var visual))
        {
            var target = Rac1World.Enemies.Targets.Concat(Rac1World.Presentation.CrateNodes).FirstOrDefault(node =>
                node.Source.NativeClassId == key.NativeClassId && node.Source.InstanceIndex == key.InstanceIndex);
            if (target is null) throw new InvalidDataException("Captured Moby is absent from the loaded level.");
            visual = CloneEquipmentCargo(target);
        }
        _rac1ChickenCargo.Remove(key);
        var pending = _rac1Captures.FirstOrDefault(capture => capture.Key == key);
        if (pending is not null) _rac1Captures.Remove(pending);
        visual.Scale = pending?.Scale ?? Vector3.One; visual.Visible = true;
        visual.GlobalPosition = EquipmentMuzzle(Rac1WeaponId.SuckCannon);
        var direction = (EquipmentAim(visual.GlobalPosition, 30, .94f) - visual.GlobalPosition).Normalized();
        _rac1EquipmentShots.Add(new(Rac1WeaponId.SuckCannon, shot.Generation, visual, visual.GlobalPosition, direction * Rac1WeaponReconstructionPolicy.SuckProjectileStep));
        SpawnEquipmentFlash(visual.GlobalPosition, 27, new(.5f, .7f, 1, 1), .6f, 7);
    }
}
