using Godot;
using OBP.Core.Math;
using OBP.Godot;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    private async Task RunMovementStateSmokeAsync()
    {
        try
        {
            Engine.MaxFps = 60;
            PlayerHost player = _player ?? throw new InvalidOperationException("Movement state smoke requires a player.");
            ClearMovementSmokeInput();
            await WaitForGroundedAsync(player, 180);
            await PhysicsFramesAsync(8);
            // Capture lethal reactions before animation terminalization too.
            // Restoring removals must neither resurrect enemies nor emit loot.
            int[] defeated = _rac1Enemies.Values.GroupBy(enemy => enemy.Node.Source.NativeClassId)
                .Select(group => group.First().Node.Source.InstanceIndex).ToArray();
            foreach (int id in defeated)
            {
                var enemy = _rac1Enemies[id];
                Require(ApplyRac1EnemyWeaponDamage(enemy.Node, Rac1DamageRuntime.FromBombGlove(new(
                    100000, enemy.Node.Source.NativeClassId, id, Rac1BombGlove.NativeDamage, Rac1BombGlove.NativeDamageFlags))),
                    "movement save fixture rejected lethal damage");
                Require(enemy.RewardEmitted && IsRac1GameplayActive(enemy.Node), "fixture did not capture the pending lethal reaction");
            }
            var brokenCrate = _rac1CrateNodes.First(node => node.Source.NativeClassId == 500 && IsRac1GameplayActive(node));
            int brokenId = brokenCrate.Source.InstanceIndex;
            Require(TryStrikeRac1Crate(brokenCrate), "movement save fixture rejected crate break");
            int destroyedCount = _rac1BoltCrates.DestroyedCrateCount;
            OnRac1WeaponSelectionRequested(Rac1WeaponId.FirstRanged);
            Require(_rac1Weapons.TryUseEquipped(), "movement save fixture could not consume ammo");
            int savedAmmo = _rac1Weapons.FirstRangedAmmo;
            _rac1BoltWallet.Credit(37);
            int savedBolts = _rac1BoltWallet.Balance;
            void RequireWorldRestored()
            {
                Require(defeated.All(id => !IsRac1GameplayActive(_rac1Enemies[id].Node) &&
                    !_rac1Enemies[id].Node.Root.Visible && _rac1Enemies[id].Body.CollisionLayer == 0 && _rac1Enemies[id].RewardEmitted),
                    "movement load resurrected a defeated enemy or its collision body");
                Require(!IsRac1GameplayActive(_rac1CrateNodes.Single(node => node.Source.InstanceIndex == brokenId)) &&
                    !_rac1CrateBodies.ContainsKey(brokenId) && _rac1BoltCrates.DestroyedCrateCount == destroyedCount,
                    "movement load resurrected a broken crate or lost its UID state");
                Require(_rac1Weapons.Equipped == Rac1WeaponId.FirstRanged && _rac1Weapons.FirstRangedAmmo == savedAmmo &&
                    _rac1BoltWallet.Balance == savedBolts && !_rac1PickupMotion.Keys.Any(id => id > 0),
                    "movement load changed inventory/currency or re-emitted death rewards");
            }
            _movementStatePathOverride = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"),
                "..", "captures", "repros", "smoke-state.json"));
            if (File.Exists(MovementStatePath)) File.Delete(MovementStatePath);

            // Exercise the real development hotkey and persistent reader.
            SendPhysicalKey(Key.F12, true);
            SendPhysicalKey(Key.F12, false);
            await Rac1SmokeWaitAsync(() => File.Exists(MovementStatePath), 30, "F12 movement save");
            MovementReproState saved = MovementReproStateFile.Load(MovementStatePath);
            Vector3 savedPosition = player.GlobalPosition;
            Require(saved.World is not null && saved.World.InactiveObjects.Count >= defeated.Length + 1, "world removals were not saved");
            await LoadMovementStateAsync(MovementStatePath);
            RequireWorldRestored();
            var first = await RecordForwardMovementReplayAsync(25);
            Require(Math.Sqrt(Math.Pow(first[^1].Position.X - savedPosition.X, 2) + Math.Pow(first[^1].Position.Z - savedPosition.Z, 2)) > .25f,
                "saved movement setup produced no forward travel");

            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F12, PhysicalKeycode = Key.F12, ShiftPressed = true, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F12, PhysicalKeycode = Key.F12, ShiftPressed = true, Pressed = false });
            await Rac1SmokeWaitAsync(() => !_movementStateLoading &&
                _player is not null && _player.GlobalPosition.DistanceTo(savedPosition) < .0001f,
                60, "Shift+F12 movement load");
            player = _player!;
            RequireWorldRestored();
            Require(Math.Abs(first[0].ControlHeading - saved.Player.Camera!.ControlHeadingRadians) < .000001,
                "movement load changed the camera/control heading");
            var second = await LoadMovementStateAsync(MovementStatePath, recordFrames: 25)
                ?? throw new InvalidOperationException("Second state replay failed.");
            player = _player!;
            RequireWorldRestored();
            float error = (float)first.Zip(second).Max(pair =>
            {
                Vec3 d = pair.First.Position - pair.Second.Position;
                return Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            });
            Require(error < .03f, $"reloaded forward route diverged by {error:F6} units");

            var fallback = saved with
            {
                Player = saved.Player with
                {
                    ForceHostCamera = true,
                    HostCameraYawRotation = new(0, .7, 0),
                    HostCameraPitchRotation = new(-1.1, 0, 0),
                    HostCameraLocalPosition = new(0, 0, saved.Player.CameraDistance),
                },
            };
            string fallbackPath = Path.Combine(Path.GetDirectoryName(MovementStatePath)!, "smoke-fallback-state.json");
            MovementReproStateFile.Save(fallbackPath, fallback);
            var fallbackFirst = await LoadMovementStateAsync(fallbackPath, recordFrames: 15)
                ?? throw new InvalidOperationException("Fallback camera replay failed.");
            var fallbackSecond = await LoadMovementStateAsync(fallbackPath, recordFrames: 15)
                ?? throw new InvalidOperationException("Fallback camera reload failed.");
            Require(!(_player?.HasActiveRecoveredCamera ?? true) && fallbackFirst.Zip(fallbackSecond).All(pair =>
                pair.First.Position == pair.Second.Position && pair.First.ControlHeading == pair.Second.ControlHeading),
                "fallback camera/control replay changed after reload");
            player = _player!;

            // Check exact restoration independently of physics settling and use
            // a pose beneath an overlapping surface to guard against spawn snap.
            player.RestoreMovementRepro(saved.Player with
            {
                Position = saved.Player.Position with { Y = saved.Player.Position.Y - .5 },
            });
            Require(Math.Abs(player.GlobalPosition.Y - (saved.Player.Position.Y - .5)) < .00001,
                "movement restore resnapped the saved coordinate");
            player.RestoreMovementRepro(saved.Player);
            GD.Print("[movement-state-smoke] defeated toad/robot, broken crate, no duplicate rewards, equipped item/ammo/bolts PASS");
            GD.Print($"[movement-state-smoke] PASS; hotkey save/load, persistent roundtrip, exact pose, " +
                $"native/fallback camera/control, no spawn snap, forward replay error={error:F6}; state={MovementStatePath}");
            ApplicationLifecycle.RequestQuit(this, "movement-state-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[movement-state-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "movement-state-smoke-fail", 3);
        }
        finally { ClearMovementSmokeInput(); }
    }
}
