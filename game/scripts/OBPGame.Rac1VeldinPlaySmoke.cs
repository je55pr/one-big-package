using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;

namespace OneBigPackage;

/// <summary>
/// End-to-end Veldin gate that starts from the ordinary ephemeral local-dev state
/// and drives only public player input. It deliberately contains no transform
/// staging, checkpoint/death injection, ammo mutation, or direct consequence calls.
/// </summary>
public partial class OBPGame
{
    private async Task RunRac1VeldinPlaySmokeAsync()
    {
        try
        {
            if (_world is not { Game: "rac1", LevelId: 0 } || _player is null)
                throw new InvalidOperationException(
                    "Veldin play smoke must begin in ordinary rac1:LEVEL0.");

            Engine.MaxFps = 60;
            ClearRac1VeldinPlaySmokeInput();
            await WaitForGroundedAsync(_player, 360);
            AssertRac1VeldinEphemeralOpening();

            Vector3 authoredRespawnPosition = _player.GlobalPosition;
            if (!TryGetRac1RepresentativeHostile(out var hostile, out _) ||
                hostile is null ||
                !IsInstanceValid(hostile.Root) ||
                !hostile.Root.Visible)
                throw new InvalidOperationException(
                    "Ordinary Veldin start has no supported class-749 runtime witness.");

            GD.Print(
                $"[rac1-veldin-play] opening PASS: ephemeral current=0, " +
                $"Wrench equipped, Bomb Glove ammo={_rac1Weapons.FirstRangedAmmo}");

            // Run the fall route before camera input changes the recovered control heading.
            await RunRac1NaturalVeldinFallRespawnSmokeAsync(authoredRespawnPosition);
            await RunRac1RecoveredCameraInputSmokeAsync(_player);
            await RunRac1OrdinaryBombGloveUseSmokeAsync();

            await RunRac1VisibleHostileMotionSmokeAsync(hostile);

            var naturalAttack = await Rac1SmokeProvokeClass749AttackAsync(
                hostile,
                maxFrames: 3000);
            GD.Print(
                $"[rac1-veldin-play] natural hostile attack PASS: " +
                $"Ratchet travel={naturalAttack.PlayerTravel:0.###}, " +
                $"hostile travel={naturalAttack.HostileTravel:0.###}, " +
                $"Nanotech={_rac1Nanotech.Probe().Nanotech}/4");

            await RunRac1OrdinaryHostileWrenchSmokeAsync(hostile);
            await RunRac1OrdinaryCrateWrenchSmokeAsync();

            GD.Print(
                "[rac1-veldin-play] PASS: clean opening, recovered camera input, " +
                "Bomb Glove use, natural hostile motion/attack, practical wrench contacts, " +
                "and natural fall/death/respawn all passed without smoke staging");
            ApplicationLifecycle.RequestQuit(this, "rac1-veldin-play-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[rac1-veldin-play] FAIL: {ex.Message}\n{ex.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-veldin-play-smoke-fail", 3);
        }
        finally
        {
            ClearRac1VeldinPlaySmokeInput();
        }
    }

    private void AssertRac1VeldinEphemeralOpening()
    {
        if (_args.Rac1CampaignPersistenceRequested || _rac1CampaignPersistence.Enabled)
            throw new InvalidOperationException(
                "Veldin play smoke requires the ordinary ephemeral local-dev campaign session.");
        if (_rac1CampaignRestoreKind != Rac1CampaignRestoreKind.DefaultedMissingState)
            throw new InvalidOperationException(
                $"Ephemeral opening restore kind was {_rac1CampaignRestoreKind}.");
        if (_rac1CampaignSession.Campaign.CurrentLevel != 0 ||
            _rac1CampaignSession.Campaign.AdmittedDestinationCount != 0)
            throw new InvalidOperationException(
                "Ephemeral opening campaign state was not the clean Veldin default.");
        if (!_rac1Weapons.OwnsFirstRanged ||
            _rac1Weapons.Equipped != Rac1WeaponId.Wrench ||
            _rac1Weapons.FirstRangedAmmo != 6)
            throw new InvalidOperationException(
                $"Opening inventory drifted: item10-owned={_rac1Weapons.OwnsFirstRanged}, " +
                $"equipped={_rac1Weapons.Equipped}, ammo={_rac1Weapons.FirstRangedAmmo}; " +
                "expected Wrench plus six usable Bomb Glove rounds.");
        var life = _rac1Nanotech.Probe();
        if (life.IsDead || life.Nanotech != 4)
            throw new InvalidOperationException(
                $"Opening Nanotech was {life.Nanotech}, dead={life.IsDead}; expected 4/alive.");
        if (_rac1BoltCrates.DestroyedCrateCount != 0 ||
            _rac1PickupNodes.Count != 0 ||
            _rac1Projectiles.Count != 0)
            throw new InvalidOperationException(
                "Opening Veldin gameplay state was not clean.");
        AssertRac1SmokeHudWeapon(
            Rac1HudProjection.WrenchPresentationKey,
            expectedAmmo: null);
    }

    private async Task RunRac1RecoveredCameraInputSmokeAsync(DebugPlayer player)
    {
        if (!player.HasActiveRecoveredCamera ||
            player.CameraControllerLabel != "rac1-native-type0" ||
            player.Rac1RuntimeCameraState is null)
            throw new InvalidOperationException(
                "Ordinary Veldin did not start with the recovered type-0 camera.");

        Vector3 playerStart = player.GlobalPosition;
        Vector3 eyeStart = player.Camera.GlobalPosition;
        double headingStart = player.Rac1ControlYaw;

        SetRac1CameraSmokeInput(right: 1f, up: 0f);
        await PhysicsFramesAsync(36);
        ReleaseRac1CameraSmokeInput();

        Vector3 eyeAfterHorizontal = player.Camera.GlobalPosition;
        double headingDelta = Math.Abs(WrapAngle(player.Rac1ControlYaw - headingStart));
        if (headingDelta < 0.10d ||
            HorizontalDistance(eyeStart, eyeAfterHorizontal) < 0.25f)
            throw new InvalidOperationException(
                $"Recovered horizontal camera input was not visible: heading={headingDelta:R}, " +
                $"eye={HorizontalDistance(eyeStart, eyeAfterHorizontal):R}.");

        double pitchBefore = player.Rac1CameraManualPitch;
        float eyeYBefore = player.Camera.GlobalPosition.Y;
        SetRac1CameraSmokeInput(right: 0f, up: 1f);
        await PhysicsFramesAsync(36);
        ReleaseRac1CameraSmokeInput();

        double pitchAtRelease = player.Rac1CameraManualPitch;
        float eyeYAtRelease = player.Camera.GlobalPosition.Y;
        double pitchDelta = Math.Abs(pitchAtRelease - pitchBefore);
        float eyeYDelta = Math.Abs(eyeYAtRelease - eyeYBefore);
        if (pitchDelta < 0.40d || eyeYDelta < 0.25f)
            throw new InvalidOperationException(
                $"Recovered vertical camera orbit was not visible: manual={pitchDelta:R}, " +
                $"eyeY={eyeYDelta:R}.");

        await PhysicsFramesAsync(90);

        double neutralReleaseDrift =
            Math.Abs(player.Rac1CameraManualPitch - pitchAtRelease);
        float neutralEyeYDrift =
            Math.Abs(player.Camera.GlobalPosition.Y - eyeYAtRelease);
        if (neutralReleaseDrift > 0.02d || neutralEyeYDrift > 0.20f)
            throw new InvalidOperationException(
                $"Recovered vertical camera orbit did not hold after neutral release: " +
                $"manual drift={neutralReleaseDrift:R}, eyeY drift={neutralEyeYDrift:R}.");

        if (HorizontalDistance(playerStart, player.GlobalPosition) > 0.05f)
            throw new InvalidOperationException(
                "Camera smoke moved Ratchet while only camera actions were pressed.");

        GD.Print(
            $"[rac1-veldin-play] camera PASS: heading delta={headingDelta:0.###} rad, " +
            $"vertical state delta={pitchDelta:0.###}, eyeY delta={eyeYDelta:0.###}, " +
            $"neutral drift={neutralReleaseDrift:0.###}");
    }

    private async Task RunRac1OrdinaryBombGloveUseSmokeAsync()
    {
        int ammoBefore = _rac1Weapons.FirstRangedAmmo;
        if (ammoBefore != 6)
            throw new InvalidOperationException(
                $"Bomb Glove use must begin from six opening rounds, found {ammoBefore}.");

        await TapPhysicalKeyAsync(Key.Key2);
        await Rac1SmokeWaitAsync(
            () => _rac1Weapons.Equipped == Rac1WeaponId.FirstRanged,
            60,
            "ordinary Bomb Glove selection");
        AssertRac1SmokeHudWeapon(
            Rac1HudProjection.BombGlovePresentationKey,
            ammoBefore);

        await TapRac1PrimaryActionAsync();
        await Rac1SmokeWaitAsync(
            () => _rac1Weapons.FirstRangedAmmo == ammoBefore - 1,
            90,
            "ordinary Bomb Glove fire");
        AssertRac1SmokeHudWeapon(
            Rac1HudProjection.BombGlovePresentationKey,
            ammoBefore - 1);

        await TapPhysicalKeyAsync(Key.Key1);
        await Rac1SmokeWaitAsync(
            () => _rac1Weapons.Equipped == Rac1WeaponId.Wrench,
            60,
            "ordinary Wrench reselection");
        AssertRac1SmokeHudWeapon(
            Rac1HudProjection.WrenchPresentationKey,
            expectedAmmo: null);

        GD.Print(
            $"[rac1-veldin-play] Bomb Glove PASS: ordinary input consumed " +
            $"{ammoBefore}->{_rac1Weapons.FirstRangedAmmo}");
    }

    private async Task RunRac1VisibleHostileMotionSmokeAsync(
        RuntimeWorldScene.DynamicObjectNode hostile)
    {
        Vector3 startPosition = hostile.Root.GlobalPosition;
        await Rac1SmokeWaitAsync(
            () =>
                IsInstanceValid(hostile.Root) &&
                hostile.Root.Visible &&
                hostile.Root.GlobalPosition.DistanceTo(startPosition) > 0.25f,
            240,
            "supported class-749 visible motion");

        float moved = hostile.Root.GlobalPosition.DistanceTo(startPosition);
        GD.Print(
            $"[rac1-veldin-play] hostile motion PASS: class-749 moved={moved:0.###}");
    }

    private async Task RunRac1OrdinaryHostileWrenchSmokeAsync(
        RuntimeWorldScene.DynamicObjectNode hostile)
    {
        await TapPhysicalKeyAsync(Key.Key1);
        await Rac1SmokeWaitAsync(
            () => _rac1Weapons.Equipped == Rac1WeaponId.Wrench,
            60,
            "ordinary Wrench selection before hostile contact");

        float approach = 0f;
        if (!Rac1SmokeCurrentWrenchPolicyAdmits(hostile.Root.GlobalPosition))
        {
            approach = await Rac1SmokeApproachWrenchTargetAsync(
                () => hostile.Root.GlobalPosition,
                maxFrames: 360,
                "naturally pursuing class-749 hostile");
        }

        await TapRac1PrimaryActionAsync();
        await Rac1SmokeWaitAsync(
            () => _rac1HostileProbes.TryGetValue(
                    Rac1WitnessHostileInstance,
                    out var probe) &&
                probe.Health == 0f &&
                !hostile.Root.Visible,
            120,
            "ordinary hostile wrench contact");

        GD.Print(
            $"[rac1-veldin-play] hostile wrench PASS: ordinary approach={approach:0.###}");
    }

    private async Task RunRac1OrdinaryCrateWrenchSmokeAsync()
    {
        if (_player is null)
            throw new InvalidOperationException("Veldin player disappeared before crate smoke.");

        var (crate, waypoints) = SelectRac1PracticalWrenchCrate();
        Vector3 routeStart = _player.GlobalPosition;
        float directDistance = HorizontalDistance(routeStart, crate.Root.GlobalPosition);
        await Rac1SmokeFollowWalkRouteAsync(waypoints);

        float finalApproach = 0f;
        if (!Rac1SmokeCurrentWrenchPolicyAdmits(crate.Root.GlobalPosition))
        {
            finalApproach = await Rac1SmokeApproachWrenchTargetAsync(
                () => crate.Root.GlobalPosition,
                maxFrames: 240,
                $"planned class-500 crate i{crate.Source.InstanceIndex}");
        }

        int destroyedBefore = _rac1BoltCrates.DestroyedCrateCount;
        int[] visibleBefore = _rac1CrateNodes
            .Where(node => IsInstanceValid(node.Root) && node.Root.Visible)
            .Select(node => node.Source.InstanceIndex)
            .ToArray();

        await TapRac1PrimaryActionAsync();
        await Rac1SmokeWaitAsync(
            () => _rac1BoltCrates.DestroyedCrateCount == destroyedBefore + 1,
            120,
            "ordinary wrench crate contact");

        int brokenInstance = _rac1CrateNodes
            .Where(node => visibleBefore.Contains(node.Source.InstanceIndex))
            .Where(node => IsInstanceValid(node.Root) && !node.Root.Visible)
            .Select(node => node.Source.InstanceIndex)
            .DefaultIfEmpty(-1)
            .First();

        float travelled = HorizontalDistance(routeStart, _player.GlobalPosition);
        GD.Print(
            $"[rac1-veldin-play] crate wrench PASS: planned=i{crate.Source.InstanceIndex}, " +
            $"broken=i{brokenInstance}, direct={directDistance:0.###}, " +
            $"route-points={waypoints.Count}, travel={travelled:0.###}, " +
            $"final={finalApproach:0.###}");
    }

    private (
        RuntimeWorldScene.DynamicObjectNode Crate,
        IReadOnlyList<Vector3> Waypoints) SelectRac1PracticalWrenchCrate()
    {
        if (_player is null)
            throw new InvalidOperationException("Veldin player disappeared.");

        var candidates = _rac1CrateNodes
            .Where(node => IsInstanceValid(node.Root) && node.Root.Visible)
            .OrderBy(node => HorizontalDistance(_player.GlobalPosition, node.Root.GlobalPosition))
            .ThenBy(node => node.Source.InstanceIndex)
            .ToArray();

        foreach (var candidate in candidates)
        {
            float distance = HorizontalDistance(
                _player.GlobalPosition,
                candidate.Root.GlobalPosition);
            if (distance < 1f || distance > 70f)
                continue;
            if (TryPlanRac1SmokeWalkRoute(
                    candidate.Root.GlobalPosition,
                    out var waypoints))
                return (candidate, waypoints);
        }

        throw new InvalidOperationException(
            $"No visible centre-10 class-500 crate had a collision-backed ordinary walk route " +
            $"from {_player.GlobalPosition}; candidates={candidates.Length}.");
    }

    private readonly record struct Rac1SmokeGridNode(int X, int Z);

    private bool TryPlanRac1SmokeWalkRoute(
        Vector3 target,
        out IReadOnlyList<Vector3> waypoints)
    {
        waypoints = Array.Empty<Vector3>();
        if (_player is null)
            return false;

        const float step = 2.5f;
        const float goalRadius = 2.0f;
        Vector3 origin = _player.GlobalPosition;
        float directDistance = HorizontalDistance(origin, target);
        int radius = Math.Clamp(
            (int)Math.Ceiling((directDistance + 24f) / step),
            8,
            38);

        var floorCache = new Dictionary<Rac1SmokeGridNode, Vector3?>();
        Vector3? Floor(Rac1SmokeGridNode node)
        {
            if (floorCache.TryGetValue(node, out var cached))
                return cached;

            Vector3 sample = new(
                origin.X + node.X * step,
                origin.Y,
                origin.Z + node.Z * step);
            var query = PhysicsRayQueryParameters3D.Create(
                sample + Vector3.Up * 24f,
                sample + Vector3.Down * 64f);
            query.Exclude =
                new global::Godot.Collections.Array<Rid> { _player.GetRid() };
            var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count == 0 || ((Vector3)hit["normal"]).Y < 0.52f)
            {
                floorCache[node] = null;
                return null;
            }

            Vector3 floor = (Vector3)hit["position"];
            floorCache[node] = floor;
            return floor;
        }

        bool Traversable(Vector3 a, Vector3 b)
        {
            if (Math.Abs(a.Y - b.Y) > 1.35f)
                return false;

            var query = PhysicsRayQueryParameters3D.Create(
                a + Vector3.Up * 1.0f,
                b + Vector3.Up * 1.0f);
            query.Exclude =
                new global::Godot.Collections.Array<Rid> { _player.GetRid() };
            var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count == 0)
                return true;

            Vector3 point = (Vector3)hit["position"];
            return point.DistanceTo(b + Vector3.Up * 1.0f) < 0.35f;
        }

        var start = new Rac1SmokeGridNode(0, 0);
        if (Floor(start) is null)
            return false;

        var frontier = new PriorityQueue<Rac1SmokeGridNode, float>();
        var cost = new Dictionary<Rac1SmokeGridNode, float> { [start] = 0f };
        var previous = new Dictionary<Rac1SmokeGridNode, Rac1SmokeGridNode>();
        frontier.Enqueue(start, 0f);

        Rac1SmokeGridNode? goal = null;
        ReadOnlySpan<(int X, int Z)> offsets =
        [
            (1, 0), (-1, 0), (0, 1), (0, -1),
            (1, 1), (1, -1), (-1, 1), (-1, -1),
        ];

        int expanded = 0;
        while (frontier.Count > 0 && expanded++ < 5000)
        {
            Rac1SmokeGridNode current = frontier.Dequeue();
            Vector3 currentFloor = Floor(current)!.Value;
            if (HorizontalDistance(currentFloor, target) <= goalRadius)
            {
                goal = current;
                break;
            }

            foreach (var (dx, dz) in offsets)
            {
                var next = new Rac1SmokeGridNode(current.X + dx, current.Z + dz);
                if (Math.Abs(next.X) > radius || Math.Abs(next.Z) > radius)
                    continue;
                Vector3? nextFloorMaybe = Floor(next);
                if (nextFloorMaybe is not { } nextFloor ||
                    !Traversable(currentFloor, nextFloor))
                    continue;

                float segment = HorizontalDistance(currentFloor, nextFloor);
                float nextCost = cost[current] + segment;
                if (cost.TryGetValue(next, out float known) && nextCost >= known)
                    continue;

                cost[next] = nextCost;
                previous[next] = current;
                float heuristic = HorizontalDistance(nextFloor, target);
                frontier.Enqueue(next, nextCost + heuristic);
            }
        }

        if (goal is null)
            return false;

        var reverse = new List<Vector3>();
        Rac1SmokeGridNode cursor = goal.Value;
        while (cursor != start)
        {
            reverse.Add(Floor(cursor)!.Value);
            cursor = previous[cursor];
        }
        reverse.Reverse();

        // Keep grid turns but remove redundant collinear nodes. This remains a
        // test-side input route; it never moves Ratchet except through InputMap.
        var compact = new List<Vector3>();
        for (int i = 0; i < reverse.Count; i++)
        {
            if (i > 0 && i + 1 < reverse.Count)
            {
                Vector3 a = reverse[i] - reverse[i - 1];
                Vector3 b = reverse[i + 1] - reverse[i];
                a.Y = b.Y = 0f;
                if (a.LengthSquared() > 0.01f &&
                    b.LengthSquared() > 0.01f &&
                    Math.Abs(a.Normalized().Dot(b.Normalized())) > 0.999f)
                    continue;
            }
            compact.Add(reverse[i]);
        }

        waypoints = compact;
        return compact.Count > 0;
    }

    private async Task Rac1SmokeFollowWalkRouteAsync(
        IReadOnlyList<Vector3> waypoints)
    {
        if (_player is null)
            throw new InvalidOperationException("Veldin player disappeared during route.");

        try
        {
            foreach (Vector3 waypoint in waypoints)
            {
                bool reached = false;
                float bestDistance =
                    HorizontalDistance(_player.GlobalPosition, waypoint);
                int stagnantFrames = 0;
                int jumpHeldFrames = 0;

                for (int frame = 0; frame < 360; frame++)
                {
                    float distance =
                        HorizontalDistance(_player.GlobalPosition, waypoint);
                    if (distance <= 0.85f)
                    {
                        reached = true;
                        break;
                    }

                    if (distance < bestDistance - 0.04f)
                    {
                        bestDistance = distance;
                        stagnantFrames = 0;
                    }
                    else
                    {
                        stagnantFrames++;
                    }

                    // Ordinary Veldin traversal includes jump. Use it only after
                    // sustained lack of planar progress, never to inject position.
                    if (stagnantFrames >= 45 &&
                        jumpHeldFrames == 0 &&
                        _player.IsOnFloor())
                    {
                        Input.ActionPress(RawGamepadInput.Jump);
                        jumpHeldFrames = 8;
                        stagnantFrames = 0;
                    }

                    Rac1SmokeDriveToward(waypoint);
                    await PhysicsFramesAsync(1);

                    if (jumpHeldFrames > 0 && --jumpHeldFrames == 0)
                        Input.ActionRelease(RawGamepadInput.Jump);
                }

                Input.ActionRelease(RawGamepadInput.Jump);
                if (!reached)
                    throw new TimeoutException(
                        $"Ordinary crate route stalled before waypoint {waypoint}; " +
                        $"player={_player.GlobalPosition}.");
            }
        }
        finally
        {
            Input.ActionRelease(RawGamepadInput.Jump);
            ClearMovementSmokeInput();
        }
    }

    private async Task TapRac1PrimaryActionAsync()
    {
        Input.ActionPress(RawGamepadInput.Action);
        await PhysicsFramesAsync(2);
        Input.ActionRelease(RawGamepadInput.Action);
        await PhysicsFramesAsync(2);
    }

    private static void SetRac1CameraSmokeInput(float right, float up)
    {
        ReleaseRac1CameraSmokeInput();
        if (right > 0f) Input.ActionPress(RawGamepadInput.CameraRight, right);
        else if (right < 0f) Input.ActionPress(RawGamepadInput.CameraLeft, -right);
        if (up > 0f) Input.ActionPress(RawGamepadInput.CameraUp, up);
        else if (up < 0f) Input.ActionPress(RawGamepadInput.CameraDown, -up);
    }

    private static void ReleaseRac1CameraSmokeInput()
    {
        Input.ActionRelease(RawGamepadInput.CameraLeft);
        Input.ActionRelease(RawGamepadInput.CameraRight);
        Input.ActionRelease(RawGamepadInput.CameraUp);
        Input.ActionRelease(RawGamepadInput.CameraDown);
    }

    private static void ClearRac1VeldinPlaySmokeInput()
    {
        ClearMovementSmokeInput();
        ReleaseRac1CameraSmokeInput();
    }
}
