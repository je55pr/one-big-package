using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;

namespace OBP.Game.Diagnostics;

/// <summary>Ordinary Veldin input witnesses; no transform staging, ammo mutation or direct consequence calls.</summary>
public sealed class Rac1VeldinScenario(MovementDiagnosticContext context, Rac1CombatScenarioPorts ports) : Rac1CombatScenarioSupport(context, ports)
{
    public async Task RunAsync()
    {
        try
        {
            if (World.Read() is not { Source: ObpSourceGame.Rac1, LevelId: 0 } || Player is null)
                throw new InvalidOperationException(
                    "Veldin play smoke must begin in ordinary rac1:LEVEL0.");

            using var frameRate = World.LimitFrameRate(60);
            ClearRac1VeldinPlaySmokeInput();
            await WaitForGroundedAsync(Player, 360);
            AssertRac1VeldinEphemeralOpening();

            Vector3 authoredRespawnPosition = Player.Read().GlobalPosition;
            if (Hostiles.Count != Rac1Class749VeldinPopulation.AuthoredPlacementCount ||
                Combat.ReadPopulation().RegisteredClass749 != Rac1Class749VeldinPopulation.AuthoredPlacementCount ||
                !TryGetRac1Hostile(143, out var hostile, out _) ||
                hostile is null ||
                !Combat.IsAvailable(hostile.Key) ||
                !hostile.Read().Visible)
                throw new InvalidOperationException(
                    "Ordinary Veldin start does not expose the recovered 16-placement class-749 population.");

            await RunRac1VeldinPopulationOpeningIdleSmokeAsync();
            await Ports.Menus.CheckVeldinPauseAsync(Driver.Lifetime);
            await Ports.Menus.CheckRetailNavigationAsync("Veldin", Driver.Lifetime);

            Log(
                $"[rac1-veldin-play] opening PASS: ephemeral current=0, " +
                $"Bomb Glove equipped, ammo={Ports.Equipment.Read().BombAmmo}");

            // Run the fall route before camera input changes the recovered control heading.
            await new Rac1EnvironmentalDeathRegression(Context, Ports.Environment).RunAsync(authoredRespawnPosition);
            await RunRac1RecoveredCameraInputSmokeAsync();
            await RunRac1OrdinaryBombGloveUseSmokeAsync();

            var naturalAttack = await Rac1SmokeProvokeClass749AttackAsync(
                hostile,
                maxFrames: 3000);
            if (Ports.Environment.Read().RestartGeneration != Ports.Environment.Read().DeathGeneration ||
                naturalAttack.PlayerTravel < 5f)
                throw new InvalidOperationException(
                    $"Ordinary movement did not resume after automatic Veldin restart: " +
                    $"restart={Ports.Environment.Read().RestartGeneration}, " +
                    $"death={Ports.Environment.Read().DeathGeneration}, " +
                    $"travel={naturalAttack.PlayerTravel:R}.");
            Log(
                $"[rac1-veldin-play] natural hostile attack / post-restart control PASS: " +
                $"Ratchet travel={naturalAttack.PlayerTravel:0.###}, " +
                $"hostile travel={naturalAttack.HostileTravel:0.###}, " +
                $"Nanotech={lifecycle.ReadNanotech().Nanotech}/4");

            await RunRac1OrdinaryHostileWrenchSmokeAsync(Hostiles[naturalAttack.AttackerInstance]);
            await RunRac1OrdinaryCrateWrenchSmokeAsync();

            Log(
                "[rac1-veldin-play] PASS: clean opening, recovered camera input, " +
                "Bomb Glove use, natural hostile motion/attack, practical wrench contacts, " +
                "and natural fall/death/automatic restart all passed without smoke staging or development respawn input");

        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogError($"[rac1-veldin-play] FAIL: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
        finally
        {
            ClearRac1VeldinPlaySmokeInput();
        }
    }


    public async Task RunMovementContactAsync()
    {
        try
        {
            if (World.Read() is not { Source: ObpSourceGame.Rac1, LevelId: 0 } || Player is null)
                throw new InvalidOperationException(
                    "R&C1 movement/contact smoke must begin in ordinary rac1:LEVEL0.");

            using var frameRate = World.LimitFrameRate(60);
            ClearRac1VeldinPlaySmokeInput();
            await WaitForGroundedAsync(Player, 360);
            AssertRac1VeldinEphemeralOpening();
            Player.ResetCollisionDiagnostics();

            Vector3 authoredRespawnPosition = Player.Read().GlobalPosition;

            // First prove the ordinary recovered ground/edge path can leave authored
            // collision, hit the retail Veldin death boundary, and reacquire the
            // authored start automatically. No transform or development respawn input.
            await new Rac1EnvironmentalDeathRegression(Context, Ports.Environment).RunAsync(authoredRespawnPosition);

            Vector3 movementStart = Player.Read().GlobalPosition;
            SetAnalogueSmokeInput(0f, 0.60f);
            await PhysicsFramesAsync(48);
            float walkSpeed = HorizontalSpeed(Player);
            float walkTravel = HorizontalDistance(movementStart, Player.Read().GlobalPosition);
            double walkMagnitude = Player.Read().Rac1AnalogueMagnitude;
            double walkTarget = Player.Read().Rac1TargetPlanarStep;
            Require(
                walkMagnitude >= 0.25d && walkMagnitude < 0.83d,
                "ordinary Veldin low-stick input did not land in the recovered walk band");
            Require(
                walkTarget > 0d && walkTarget < 0.03d,
                "ordinary Veldin low-stick input did not select the recovered walk plateau");
            Require(walkTravel > 0.25f, "ordinary Veldin walk produced no useful traversal");

            Vector3 runStart = Player.Read().GlobalPosition;
            SetAnalogueSmokeInput(0f, 1f);
            await PhysicsFramesAsync(48);
            float runSpeed = HorizontalSpeed(Player);
            float runTravel = HorizontalDistance(runStart, Player.Read().GlobalPosition);
            Require(
                runSpeed > walkSpeed + 1.0f,
                "ordinary Veldin full-stick input did not progress from walk to run");
            Require(
                Player.Read().Rac1TargetPlanarStep > walkTarget,
                "ordinary Veldin full-stick target did not exceed the walk target");
            Require(runTravel > walkTravel + 1.0f, "ordinary Veldin run did not out-travel walk");

            float movingJump = await new MovementScenario(Context).MeasureJumpAsync(
                Player,
                holdFrames: 8,
                applyPartialAirControl: false,
                movingAtLaunch: true);
            Require(movingJump > 0.5f, "ordinary Veldin moving jump produced no useful apex");
            Require(Player.Read().Grounded, "ordinary Veldin jump did not reacquire floor support");
            Require(
                Player.Read().Rac1MaxObservedSlideCollisions < Player.Read().MaxSlides,
                $"ordinary Veldin movement saturated Godot MaxSlides={Player.Read().MaxSlides}; " +
                $"observed={Player.Read().Rac1MaxObservedSlideCollisions}");

            Log(
                $"[rac1-movement-contact] PASS walk={walkSpeed:0.000}/{walkTravel:0.000} " +
                $"run={runSpeed:0.000}/{runTravel:0.000} movingJump={movingJump:0.000} " +
                $"maxSlideContacts={Player.Read().Rac1MaxObservedSlideCollisions}/{Player.Read().MaxSlides}; " +
                "authored edge/death/restart and post-restart support all passed without cheats");

        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogError($"[rac1-movement-contact] FAIL: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
        finally
        {
            ClearRac1VeldinPlaySmokeInput();
        }
    }


    private void AssertRac1VeldinEphemeralOpening()
    {
        if (Combat.ReadOpening().PersistenceRequested || Combat.ReadOpening().PersistenceEnabled)
            throw new InvalidOperationException(
                "Veldin play smoke requires the ordinary ephemeral local-dev campaign session.");
        if (Combat.ReadOpening().RestoreKind != Rac1CampaignRestoreKind.DefaultedMissingState)
            throw new InvalidOperationException(
                $"Ephemeral opening restore kind was {Combat.ReadOpening().RestoreKind}.");
        if (Combat.ReadOpening().CurrentLevel != 0 ||
            Combat.ReadOpening().AdmittedDestinations != 0)
            throw new InvalidOperationException(
                "Ephemeral opening campaign state was not the clean Veldin default.");
        if (!Combat.ReadOpening().OwnsFirstRanged ||
            Ports.Equipment.Read().Selected != Rac1WeaponId.FirstRanged ||
            Ports.Equipment.Read().BombAmmo != 20)
            throw new InvalidOperationException(
                $"Opening inventory drifted: item10-owned={Combat.ReadOpening().OwnsFirstRanged}, " +
                $"equipped={Ports.Equipment.Read().Selected}, ammo={Ports.Equipment.Read().BombAmmo}; " +
                "expected Bomb Glove equipped with twenty usable rounds.");
        var life = lifecycle.ReadNanotech();
        if (life.IsDead || life.Nanotech != 4)
            throw new InvalidOperationException(
                $"Opening Nanotech was {life.Nanotech}, dead={life.IsDead}; expected 4/alive.");
        if (Combat.ReadLoot().DestroyedCrates != 0 ||
            Combat.ReadLoot().Pickups.Any(item => item.Id > 0) ||
            Combat.ReadBombs().Projectiles.Length != 0)
            throw new InvalidOperationException(
                "Opening Veldin gameplay state was not clean.");
        AssertRac1SmokeHudWeapon(
            Rac1HudProjection.BombGlovePresentationKey,
            expectedAmmo: 20);
    }


    private async Task RunRac1RecoveredCameraInputSmokeAsync()
    {
        if (!Combat.ReadPlayerCamera().ActiveRecovered ||
            Combat.ReadPlayerCamera().ControllerLabel != "rac1-native-type0" ||
            !Combat.ReadPlayerCamera().RuntimeStatePresent)
            throw new InvalidOperationException(
                "Ordinary Veldin did not start with the recovered type-0 camera.");

        Vector3 playerStart = Player.Read().GlobalPosition;
        Vector3 eyeStart = Combat.ReadPlayerCamera().Eye;
        double headingStart = Combat.ReadPlayerCamera().ControlYaw;

        SetRac1CameraSmokeInput(right: 1f, up: 0f);
        await PhysicsFramesAsync(36);
        ReleaseRac1CameraSmokeInput();

        Vector3 eyeAfterHorizontal = Combat.ReadPlayerCamera().Eye;
        double headingDelta = Math.Abs(WrapAngle(Combat.ReadPlayerCamera().ControlYaw - headingStart));
        if (headingDelta < 0.10d ||
            HorizontalDistance(eyeStart, eyeAfterHorizontal) < 0.25f)
            throw new InvalidOperationException(
                $"Recovered horizontal camera input was not visible: heading={headingDelta:R}, " +
                $"eye={HorizontalDistance(eyeStart, eyeAfterHorizontal):R}.");

        double pitchBefore = Combat.ReadPlayerCamera().ManualPitch;
        float eyeYBefore = Combat.ReadPlayerCamera().Eye.Y;
        SetRac1CameraSmokeInput(right: 0f, up: 1f);
        await PhysicsFramesAsync(36);
        ReleaseRac1CameraSmokeInput();

        double pitchAtRelease = Combat.ReadPlayerCamera().ManualPitch;
        float eyeYAtRelease = Combat.ReadPlayerCamera().Eye.Y;
        double pitchDelta = Math.Abs(pitchAtRelease - pitchBefore);
        float eyeYDelta = Math.Abs(eyeYAtRelease - eyeYBefore);
        if (pitchDelta < 0.40d || eyeYDelta < 0.25f)
            throw new InvalidOperationException(
                $"Recovered vertical camera orbit was not visible: manual={pitchDelta:R}, " +
                $"eyeY={eyeYDelta:R}.");

        await PhysicsFramesAsync(90);

        double neutralReleaseDrift =
            Math.Abs(Combat.ReadPlayerCamera().ManualPitch - pitchAtRelease);
        float neutralEyeYDrift =
            Math.Abs(Combat.ReadPlayerCamera().Eye.Y - eyeYAtRelease);
        if (Math.Abs(Combat.ReadPlayerCamera().ManualPitch) > 0.02d)
            throw new InvalidOperationException(
                $"Recovered vertical camera command did not complete its native slow release: " +
                $"manual drift={neutralReleaseDrift:R}, eyeY drift={neutralEyeYDrift:R}.");

        if (HorizontalDistance(playerStart, Player.Read().GlobalPosition) > 0.05f)
            throw new InvalidOperationException(
                "Camera smoke moved Ratchet while only camera actions were pressed.");

        Log(
            $"[rac1-veldin-play] camera PASS: heading delta={headingDelta:0.###} rad, " +
            $"vertical state delta={pitchDelta:0.###}, eyeY delta={eyeYDelta:0.###}, " +
            $"neutral drift={neutralReleaseDrift:0.###}");
    }


    private async Task RunRac1OrdinaryBombGloveUseSmokeAsync()
    {
        int ammoBefore = Ports.Equipment.Read().BombAmmo;
        if (ammoBefore != 20)
            throw new InvalidOperationException(
                $"Bomb Glove use must begin from twenty opening rounds, found {ammoBefore}.");

        await TapPhysicalKeyAsync(DiagnosticPhysicalKey.Key2);
        await Rac1SmokeWaitAsync(
            () => Ports.Equipment.Read().Selected == Rac1WeaponId.FirstRanged,
            60,
            "ordinary Bomb Glove selection");
        AssertRac1SmokeHudWeapon(
            Rac1HudProjection.BombGlovePresentationKey,
            ammoBefore);

        await Rac1EncounterInput.TapPrimaryAsync(Context, Ports.Equipment);
        await Rac1SmokeWaitAsync(
            () => Ports.Equipment.Read().BombAmmo == ammoBefore - 1,
            90,
            "ordinary Bomb Glove fire");
        AssertRac1SmokeHudWeapon(
            Rac1HudProjection.BombGlovePresentationKey,
            ammoBefore - 1);

        Log(
            $"[rac1-veldin-play] Bomb Glove PASS: ordinary input consumed " +
            $"{ammoBefore}->{Ports.Equipment.Read().BombAmmo}");
    }


    private async Task RunRac1VeldinPopulationOpeningIdleSmokeAsync()
    {
        var starts = Hostiles.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Read().Position);
        for (int frame = 0; frame < 30; frame++)
            await ProcessFrameAsync();

        int moved = 0;
        foreach (var pair in Hostiles)
        {
            if (pair.Value.Read().Position.DistanceTo(starts[pair.Key]) > 0.02f)
                moved++;
            var probe = Enemies.ReadToad(pair.Value.Key);
            if (probe.NativeState is Rac1Class749Hostile.TargetedNativeState or Rac1Class749Hostile.AttackNativeState)
                throw new InvalidOperationException(
                    $"Class-749 i{pair.Key} pursued at ordinary Veldin start in state {probe.NativeState}.");
        }

        if (moved != 0)
            throw new InvalidOperationException(
                $"Ordinary Veldin start moved {moved} class-749 placements before polygon admission.");

        Log(
            "[rac1-veldin-play] population opening PASS: all 16 class-749 placements idle at authored start");
    }


    private async Task RunRac1OrdinaryHostileWrenchSmokeAsync(
        Rac1DiagnosticObject hostile)
    {
        float approach = 0f;
        if (!Ports.Approach.Admits(hostile.Read().Position))
        {
            approach = await Rac1SmokeApproachWrenchTargetAsync(
                () => hostile.Read().Position,
                maxFrames: 360,
                "naturally pursuing class-749 hostile");
        }

        await Rac1EncounterInput.TapWrenchAsync(Context);
        await Rac1SmokeWaitAsync(
            () => Enemies.ReadToad(hostile.Key).Health == 0f &&
                !hostile.Read().Visible,
            120,
            "ordinary hostile wrench contact");

        Log(
            $"[rac1-veldin-play] hostile wrench PASS: ordinary approach={approach:0.###}");
    }


    private async Task RunRac1OrdinaryCrateWrenchSmokeAsync()
    {
        if (Player is null)
            throw new InvalidOperationException("Veldin player disappeared before crate smoke.");

        var (crate, waypoints) = SelectRac1PracticalWrenchCrate();
        Vector3 routeStart = Player.Read().GlobalPosition;
        float directDistance = HorizontalDistance(routeStart, crate.Read().Position);
        Log($"[rac1-veldin-play] ordinary crate route: start={routeStart}, crate=i{crate.Key.Target.InstanceIndex}, " +
            $"waypoints={string.Join(';', waypoints)}");
        await Rac1SmokeFollowWalkRouteAsync(waypoints);

        float finalApproach = await Rac1SmokeSettleForWrenchContactAsync(
            () => crate.Read().Position,
            maxFrames: 360,
            $"planned class-500 crate i{crate.Key.Target.InstanceIndex}");

        int destroyedBefore = Combat.ReadLoot().DestroyedCrates;
        int[] visibleBefore = Crates.Where(node => Combat.IsAvailable(node.Key) && node.Read().Visible)
            .Select(node => node.Key.Target.InstanceIndex)
            .ToArray();

        await Rac1EncounterInput.TapWrenchAsync(Context);
        await Rac1SmokeWaitAsync(
            () => Combat.ReadLoot().DestroyedCrates > destroyedBefore && !Combat.ReadObject(crate.Key).GameplayActive,
            120,
            "ordinary wrench crate contact");

        int[] brokenInstances = Crates
            .Where(node => visibleBefore.Contains(node.Key.Target.InstanceIndex))
            .Where(node => Combat.IsAvailable(node.Key) && !node.Read().Visible)
            .Select(node => node.Key.Target.InstanceIndex)
            .ToArray();
        Require(brokenInstances.Contains(crate.Key.Target.InstanceIndex),
            "ordinary wrench sweep missed the planned crate");

        float travelled = HorizontalDistance(routeStart, Player.Read().GlobalPosition);
        Log(
            $"[rac1-veldin-play] crate wrench PASS: planned=i{crate.Key.Target.InstanceIndex}, " +
            $"broken=[{string.Join(',', brokenInstances)}], direct={directDistance:0.###}, " +
            $"route-points={waypoints.Count}, travel={travelled:0.###}, " +
            $"final={finalApproach:0.###}");
    }


    private async Task<float> Rac1SmokeSettleForWrenchContactAsync(
        Func<Vector3> targetCenterProvider,
        int maxFrames,
        string label)
    {
        if (Player is null)
            throw new InvalidOperationException("Veldin player disappeared during wrench approach.");

        Vector3 start = Player.Read().GlobalPosition;
        Vector3 target = targetCenterProvider();
        int admittedStoppedFrames = 0;
        try
        {
            for (int frame = 0; frame < maxFrames; frame++)
            {
                target = targetCenterProvider();
                // The old planar admission cone extends beyond the recovered
                // animated hand/tip sweep. Approach the crate itself before
                // expecting a real contact; continue using ordinary input only.
                bool admitted = Ports.Approach.Admits(target) &&
                    HorizontalDistance(Player.Read().GlobalPosition, target) <= 1.45f;
                float speed = HorizontalSpeed(Player);

                if (admitted)
                {
                    ClearMovementSmokeInput();
                    if (speed <= 0.15f)
                    {
                        admittedStoppedFrames++;
                        if (admittedStoppedFrames >= 2)
                            return HorizontalDistance(start, Player.Read().GlobalPosition);
                    }
                    else
                    {
                        admittedStoppedFrames = 0;
                    }
                }
                else
                {
                    admittedStoppedFrames = 0;
                    // Use the already-proven low-stick walk witness so the smoke
                    // approaches the host contact window without full-run overshoot.
                    Rac1SmokeDriveToward(target, inputScale: 0.60f);
                }

                await PhysicsFramesAsync(1);
            }
        }
        finally
        {
            ClearMovementSmokeInput();
        }

        throw new TimeoutException(
            $"Timed out settling ordinary movement inside wrench host policy for {label}: " +
            $"player={Player.Read().GlobalPosition}, target={target}, speed={HorizontalSpeed(Player):R}.");
    }


    private (
        Rac1DiagnosticObject Crate,
        IReadOnlyList<Vector3> Waypoints) SelectRac1PracticalWrenchCrate()
    {
        if (Player is null)
            throw new InvalidOperationException("Veldin player disappeared.");

        var candidates = Crates.Where(node => Combat.IsAvailable(node.Key) && node.Read().Visible)
            .OrderBy(node => HorizontalDistance(Player.Read().GlobalPosition, node.Read().Position))
            .ThenBy(node => node.Key.Target.InstanceIndex)
            .ToArray();

        foreach (var candidate in candidates)
        {
            float distance = HorizontalDistance(
                Player.Read().GlobalPosition,
                candidate.Read().Position);
            if (distance < 1f || distance > 70f)
                continue;
            if (TryPlanRac1SmokeWalkRoute(
                    candidate.Read().Position,
                    out var waypoints))
                return (candidate, waypoints);
        }

        throw new InvalidOperationException(
            $"No visible centre-10 class-500 crate had a collision-backed ordinary walk route " +
            $"from {Player.Read().GlobalPosition}; candidates={candidates.Length}.");
    }


    private readonly record struct Rac1SmokeGridNode(int X, int Z);


    private bool TryPlanRac1SmokeWalkRoute(
        Vector3 target,
        out IReadOnlyList<Vector3> waypoints)
    {
        waypoints = Array.Empty<Vector3>();
        if (Player is null)
            return false;

        const float step = 2.5f;
        const float goalRadius = 2.0f;
        Vector3 origin = Player.Read().GlobalPosition;
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
            var hit = Actors.Ray(sample + Vector3.UnitY * 24f, sample - Vector3.UnitY * 64f, [], true);
            if (hit is null || hit.Normal.Y < 0.52f)
            {
                floorCache[node] = null;
                return null;
            }

            Vector3 floor = hit.Position;
            floorCache[node] = floor;
            return floor;
        }

        bool Traversable(Vector3 a, Vector3 b)
        {
            if (Math.Abs(a.Y - b.Y) > 1.35f)
                return false;

            var hit = Actors.Ray(a + Vector3.UnitY * 1.0f, b + Vector3.UnitY * 1.0f, [], true);
            if (hit is null)
                return true;

            Vector3 point = hit.Position;
            return point.DistanceTo(b + Vector3.UnitY * 1.0f) < 0.35f;
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
                    Math.Abs(Vector3.Dot(a.Normalized(), b.Normalized())) > 0.999f)
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
        if (Player is null)
            throw new InvalidOperationException("Veldin player disappeared during route.");

        try
        {
            foreach (Vector3 waypoint in waypoints)
            {
                bool reached = false;
                float bestDistance =
                    HorizontalDistance(Player.Read().GlobalPosition, waypoint);
                int stagnantFrames = 0;
                int jumpHeldFrames = 0;

                for (int frame = 0; frame < 360; frame++)
                {
                    float distance =
                        HorizontalDistance(Player.Read().GlobalPosition, waypoint);
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
                        Player.Read().Grounded)
                    {
                        Driver.SetAction(MovementDiagnosticDriver.Jump, true);
                        jumpHeldFrames = 8;
                        stagnantFrames = 0;
                    }

                    Rac1SmokeDriveToward(waypoint);
                    await PhysicsFramesAsync(1);

                    if (jumpHeldFrames > 0 && --jumpHeldFrames == 0)
                        Driver.SetAction(MovementDiagnosticDriver.Jump, false);
                }

                Driver.SetAction(MovementDiagnosticDriver.Jump, false);
                if (!reached)
                    throw new TimeoutException(
                        $"Ordinary crate route stalled before waypoint {waypoint}; " +
                        $"player={Player.Read().GlobalPosition}, velocity={Player.Read().Velocity}, " +
                        $"phase={Player.Read().Rac1MovementPhase}, floor={Player.Read().Grounded}, " +
                        $"support={Player.Read().Rac1RecoveredSupportGrounded}, best={bestDistance}, stagnant={stagnantFrames}; " +
                        $"contacts={string.Join(';', Combat.ReadPlayerContactNormals())}.");
            }
        }
        finally
        {
            Driver.SetAction(MovementDiagnosticDriver.Jump, false);
            ClearMovementSmokeInput();
        }
    }

    private void SetRac1CameraSmokeInput(float right, float up)
    {
        ReleaseRac1CameraSmokeInput();
        if (right > 0) Driver.SetAction("obp_camera_right", true, right); else if (right < 0) Driver.SetAction("obp_camera_left", true, -right);
        if (up > 0) Driver.SetAction("obp_camera_up", true, up); else if (up < 0) Driver.SetAction("obp_camera_down", true, -up);
    }
    private void ReleaseRac1CameraSmokeInput()
    { foreach (string action in new[] { "obp_camera_left", "obp_camera_right", "obp_camera_up", "obp_camera_down" }) Driver.SetAction(action, false); }
    private void ClearRac1VeldinPlaySmokeInput() { ClearMovementSmokeInput(); ReleaseRac1CameraSmokeInput(); }

}
