using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>Staged host integration; does not claim a live retail replay.</summary>
    private async Task RunRac1BombGloveSmokeAsync()
    {
        Node3D? fixture = null;
        try
        {
            Require(_player is not null && _world is { Game: "rac1", LevelId: 0 }, "Bomb Glove smoke needs Veldin");
            Engine.MaxFps = 60;
            ClearMovementSmokeInput();
            string? savedRepro = System.Environment.GetEnvironmentVariable("OBP_BOMB_REPRO_STATE");
            if (!string.IsNullOrWhiteSpace(savedRepro))
            {
                await RunRac1SavedBombReproAsync(savedRepro);
                ApplicationLifecycle.RequestQuit(this, "rac1-bomb-glove-repro-pass", 0);
                return;
            }
            fixture = new Node3D { Name = "SyntheticBombGloveFixture", Position = new(-2000, 64, 2600) };
            AddChild(fixture);
            AddSyntheticTraversalMesh(fixture, "BombFloor", 11,
                (new(-24, 0, -24), new(24, 0, -24), new(24, 0, 24)),
                (new(-24, 0, -24), new(24, 0, 24), new(-24, 0, 24)));
            await PhysicsFramesAsync(3);
            Vector3 start = fixture.GlobalPosition + new Vector3(6, .05f, 0);
            _player!.ApplyDevelopmentReset(start);
            await WaitForGroundedAsync(_player, 180);
            Rac1SmokePlaceGroundedFacing(_player.GlobalPosition, Vector3.Left);
            OnRac1WeaponSelectionRequested(Rac1WeaponId.FirstRanged);
            await PhysicsFramesAsync(4);
            Require(_rac1BombGloveView is { Visible: true } && _rac1StagedBombView is { Visible: true }, "glove/staged bomb is absent");
            Require(_rac1BombGloveAsset is { VertexCount: 1140, TriangleCount: 1200 }, "placeholder glove geometry");
            Require(_rac1WrenchView is { Visible: false }, "equipping Bomb Glove left wrench visible");
            Vector3 socket = _rac1BombGloveView!.BombLocal;
            bool socketMoved = false;
            for (int tick = 0; tick < 24; tick++)
            {
                await PhysicsFramesAsync(1);
                socketMoved |= _rac1BombGloveView.BombLocal.DistanceTo(socket) > .001f;
                Require(_rac1StagedBombView!.Position.IsEqualApprox(_rac1BombGloveView.BombLocal), "staged bomb lost animated glove socket");
            }
            Require(socketMoved, "idle glove attachment is frozen");
            await CaptureRac1BombAsync("held-glove", _player.GlobalPosition + Vector3.Up * .8f, close: true);
            int ammo = _rac1Weapons.FirstRangedAmmo;
            OnRac1PrimaryAttackRequested();
            await PhysicsFramesAsync(2);
            Require(_rac1Weapons.FirstRangedAmmo == ammo - 1 && _rac1Projectiles.Count == 1, "fire did not consume exactly one round");
            Require(CurrentPlayerAvatarSourceSequence() == 44, "throw did not select retail sequence44");
            var shot = _rac1Projectiles.Single().Value;
            Require(shot.Node.GetChildren().OfType<MeshInstance3D>().All(mesh => mesh.Mesh is ArrayMesh), "projectile still uses procedural sphere");
            Vector3 initial = FromEffectNative(shot.Motion.Position);
            float peak = initial.Y;
            await CaptureRac1BombAsync("throw", _player.GlobalPosition + Vector3.Up * .8f, close: true);
            while (shot.Motion.Flying)
            {
                await PhysicsFramesAsync(1);
                peak = MathF.Max(peak, FromEffectNative(shot.Motion.Position).Y);
            }
            Vector3 impact = FromEffectNative(shot.Motion.Position);
            Require(peak > initial.Y + 1 && impact.Y < initial.Y, "bomb did not rise and fall onto floor");
            Require(_rac1BombFragments.Count == 15 && _rac1BombBursts.Count >= 3, "impact lacks retail fragments/layers");
            await CaptureRac1BombAsync("floor-impact", impact);
            await PhysicsFramesAsync(6);
            await CaptureRac1BombAsync("expanding-blast", impact);
            await Rac1SmokeWaitAsync(() => _rac1Projectiles.Count == 0 && CurrentPlayerAvatarSourceSequence() == 0, 90, "blast/throw recovery");
            GD.Print("[bomb-glove-smoke] retail model/socket, sequence44, arc and floor impact PASS");

            var crates = _rac1CrateNodes.Where(node => IsRac1GameplayActive(node) && node.Source.NativeClassId == 500).Take(2).ToArray();
            Require(crates.Length == 2, "two live crate fixtures unavailable");
            StageRac1CrateForSmoke(crates[0], new(impact.X, fixture.GlobalPosition.Y, impact.Z - .7f));
            StageRac1CrateForSmoke(crates[1], new(impact.X, fixture.GlobalPosition.Y, impact.Z + .7f));
            await PhysicsFramesAsync(3);
            Require(BombHasLineOfSight(crates[0].Root.GlobalPosition + new Vector3(.9f, .5f, .9f),
                crates[0].Root.GlobalPosition + Vector3.Up * .5f), "crate's own diagonal bounds occluded its blast query");
            int destroyed = _rac1BoltCrates.DestroyedCrateCount;
            OnRac1PrimaryAttackRequested();
            await Rac1SmokeWaitAsync(() => _rac1BoltCrates.DestroyedCrateCount >= destroyed + 2, 150, "one blast reaching two crates");
            Require(_rac1Weapons.FirstRangedAmmo == ammo - 2, "blast spent additional ammo");
            Require(_rac1LastBombContactResolution!.DamageResults.All(result => result.NativeDamage == 2), "blast damage differs from retail envelope");
            await PhysicsFramesAsync(35);
            GD.Print("[bomb-glove-smoke] nearby-target blast and once-per-victim damage PASS");

            var hiddenCrate = _rac1CrateNodes.First(node => IsRac1GameplayActive(node) && node.Source.NativeClassId == 500);
            StageRac1CrateForSmoke(hiddenCrate, fixture.GlobalPosition + new Vector3(0, 0, 6));
            await PhysicsFramesAsync(3);
            Vector3 centre = hiddenCrate.Root.GlobalPosition + Vector3.Up * .4f;
            Require(ProbeBombWorldContact(centre + Vector3.Left * 2, centre + Vector3.Right * 2) is not null,
                "crate collision fixture was not active");
            SetRac1HostedPresence(hiddenCrate, OBP.Runtime.Gameplay.RuntimeEntityPresence.Inactive);
            Require(ProbeBombWorldContact(centre + Vector3.Left * 2, centre + Vector3.Right * 2) is null,
                "inactive crate retained an invisible projectile obstruction");
            GD.Print("[bomb-glove-smoke] inactive crate collision removal PASS");

            var wall = new StaticBody3D { Name = "BombWall", Position = fixture.GlobalPosition + new Vector3(3, 3, 0) };
            wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(.15f, 6, 8) } });
            AddChild(wall);
            var shieldedCrate = _rac1CrateNodes.First(node => IsRac1GameplayActive(node) && node.Source.NativeClassId == 500);
            StageRac1CrateForSmoke(shieldedCrate, fixture.GlobalPosition + new Vector3(2, 0, 0));
            await PhysicsFramesAsync(3);
            int impacts = _rac1BombImpactCount;
            OnRac1PrimaryAttackRequested();
            await Rac1SmokeWaitAsync(() => _rac1BombImpactCount > impacts, 40, "wall impact");
            var wallShot = _rac1Projectiles.Single().Value;
            Require(FromEffectNative(wallShot.Motion.Position).X > wall.GlobalPosition.X, "bomb passed through wall");
            Require(!BombHasLineOfSight(FromEffectNative(wallShot.Motion.Position), shieldedCrate.Root.GlobalPosition + Vector3.Up * .5f),
                "wall did not occlude the blast");
            await PhysicsFramesAsync(20);
            Require(IsRac1GameplayActive(shieldedCrate), "blast damaged a crate through a wall");
            wall.QueueFree();
            OnRac1WeaponSelectionRequested(Rac1WeaponId.Wrench);
            Require(!_rac1BombGloveView.Visible && !_rac1StagedBombView!.Visible && _rac1WrenchView!.Visible, "weapon switch presentation did not recover");
            await PhysicsFramesAsync(210);
            Require(_rac1Projectiles.Count == 0 && _rac1BombBursts.Count == 0 && _rac1BombFragments.Count == 0 && (_rac1Particles?.Count ?? 0) == 0,
                "bomb effect lifecycle leaked nodes");
            GD.Print("[bomb-glove-smoke] wall contact, weapon switch and effect cleanup PASS");
            OnRac1WeaponSelectionRequested(Rac1WeaponId.FirstRanged);
            // A thin wall between body and muzzle must block the launch itself,
            // even if the muzzle endpoint has already crossed to its far side.
            var nearWall = new StaticBody3D { Name = "ThinMuzzleWall", Position = _player.GlobalPosition + new Vector3(-.58f, 2, 0) };
            nearWall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(.08f, 4, 4) } });
            AddChild(nearWall);
            await PhysicsFramesAsync(3);
            impacts = _rac1BombImpactCount;
            OnRac1PrimaryAttackRequested();
            await PhysicsFramesAsync(2);
            Require(_rac1BombImpactCount == impacts + 1 && _rac1Projectiles.Single().Value.Motion.NativeState == 2 &&
                FromEffectNative(_rac1Projectiles.Single().Value.Motion.Position).X > nearWall.GlobalPosition.X,
                "bomb spawned through a thin wall between Ratchet and the muzzle");
            nearWall.QueueFree();
            await PhysicsFramesAsync(25);
            GD.Print("[bomb-glove-smoke] body-to-muzzle clearance PASS");
            // Stage two remaining shots for this bounded empty-ammo check;
            // the ordinary opening now has twenty rounds rather than six.
            Require(_rac1Weapons.FirstRangedAmmo >= 2 &&
                _rac1Weapons.TryConsumeAmmo((int)Rac1WeaponId.FirstRanged, _rac1Weapons.FirstRangedAmmo - 2),
                "unable to stage the held-fire ammo fixture");
            SetAnalogueSmokeInput(0, 1);
            Input.ActionPress(RawGamepadInput.Traversal);
            await PhysicsFramesAsync(45);
            Input.ActionRelease(RawGamepadInput.Traversal);
            ClearMovementSmokeInput();
            Require(_rac1Weapons.FirstRangedAmmo == 0 && _rac1BombGloveView.Visible && !_rac1StagedBombView!.Visible,
                "held firing did not follow 20-tick cadence or empty glove still displays a bomb");
            await PhysicsFramesAsync(150);
            Require(CurrentPlayerAvatarSourceSequence() == 0, "running throw did not recover standing");
            var controller = (OBP.RAC1.Player.Rac1PlayerAnimationPresentationController)_playerAvatarAnimationController!;
            Require(controller.OrdinaryStandingSample is not null || controller.OrdinaryStopSample is { Sequence: 0 },
                "throw recovery lost interpolated native standing clock");
            GD.Print("[bomb-glove-smoke] held fire, empty ammo, run/throw/idle recovery PASS");
            GD.Print("[bomb-glove-smoke] PASS");
            ApplicationLifecycle.RequestQuit(this, "rac1-bomb-glove-smoke-pass", 0);
        }
        catch (Exception exception)
        {
            GD.PrintErr($"[bomb-glove-smoke] FAIL: {exception.Message}\n{exception.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-bomb-glove-smoke-fail", 1);
        }
        finally
        {
            ClearMovementSmokeInput();
            if (fixture is not null && IsInstanceValid(fixture)) fixture.QueueFree();
        }
    }

    private async Task RunRac1SavedBombReproAsync(string path)
    {
        await LoadMovementStateAsync(path);
        Require(_player is not null && !_movementStateLoading, "saved bomb setup did not load");
        OnRac1WeaponSelectionRequested(Rac1WeaponId.FirstRanged);
        await PhysicsFramesAsync(2);
        int destroyed = _rac1BoltCrates.DestroyedCrateCount;
        OnRac1PrimaryAttackRequested();
        await PhysicsFramesAsync(2);
        Require(_rac1Projectiles.Count == 1, "saved bomb setup did not fire");
        var motion = _rac1Projectiles.Single().Value.Motion;
        GD.Print($"[bomb-repro] launch position={FromEffectNative(motion.Position)} velocity={motion.Velocity}");
        for (int tick = 0; motion.Flying && tick < 310; tick++) await PhysicsFramesAsync(1);
        Require(!motion.Flying, "saved bomb never impacted");
        Vector3 impact = FromEffectNative(motion.Position);
        GD.Print($"[bomb-repro] impact={impact}");
        var nearby = _rac1CrateNodes.Where(IsRac1GameplayActive)
            .OrderBy(node => (node.Root.GlobalPosition + Vector3.Up * .5f).DistanceSquaredTo(impact)).Take(3).ToArray();
        Require(nearby.Length > 0 && nearby.All(node =>
            (node.Root.GlobalPosition + Vector3.Up * .5f).DistanceTo(impact) < 2.5f), "saved bomb setup has no nearby crate cluster");
        foreach (var crate in nearby)
        {
            Vector3 centre = crate.Root.GlobalPosition + Vector3.Up * .5f;
            var ray = PhysicsRayQueryParameters3D.Create(impact, centre);
            ray.Exclude = new global::Godot.Collections.Array<Rid> { _player!.GetRid() };
            var obstruction = _player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            GD.Print($"[bomb-repro] crate={crate.Source.InstanceIndex} active={IsRac1GameplayActive(crate)} " +
                $"position={crate.Root.GlobalPosition} distance={impact.DistanceTo(centre)} " +
                $"los={BombHasLineOfSight(impact, centre)} ray={obstruction}");
        }
        await CaptureRac1BombAsync("saved-impact", impact);
        await PhysicsFramesAsync(20);
        GD.Print($"[bomb-repro] broken={_rac1BoltCrates.DestroyedCrateCount - destroyed}");
        Require(nearby.All(node => !IsRac1GameplayActive(node)), "saved bomb explosion missed the nearby crate cluster");
        GD.Print("[bomb-repro] PASS");
    }

    private async Task CaptureRac1BombAsync(string name, Vector3 focus, bool close = false)
    {
        string? directory = System.Environment.GetEnvironmentVariable("OBP_BOMB_CAPTURE_DIR") ??
            System.Environment.GetEnvironmentVariable("OBP_WEAPON_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory) || DisplayServer.GetName() == "headless") return;
        Directory.CreateDirectory(directory);
        var camera = new Camera3D { Current = true, Fov = 50, Far = 10000 };
        AddChild(camera);
        bool uiVisible = _ui.Visible;
        var playerHud = _player!.GetNode<CanvasLayer>("PlayerHud");
        bool playerHudVisible = playerHud.Visible;
        _ui.Visible = false;
        playerHud.Visible = false;
        camera.GlobalPosition = focus + (close ? new Vector3(-1.8f, .7f, 2.1f) : new Vector3(4, 2.5f, 5));
        camera.LookAt(focus);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, name + ".png"));
        _ui.Visible = uiVisible;
        playerHud.Visible = playerHudVisible;
        camera.QueueFree();
        _player!.Camera.Current = true;
    }
}
