using System.Buffers.Binary;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Source-backed admission and select attack transitions for native Aridia
/// class 612 (LEVEL2:0x2d7748). This is deliberately not a translation of
/// every pathfinding, physics, emitter and damage branch of its 18-slot table.
/// </summary>
public static class Rac1AridiaConstructorbotNative
{
    public const int NativeClassId = Rac1AridiaEnemies.Constructorbot;
    public const int InitialModePvarOffset = 0x194;
    public const int EngagementRadiusPvarOffset = 0x180;
    public const int ActivationVolumePvarOffset = 0x18c;
    public const int WaypointRecordPvarOffset = 0x188;

    /// <summary>
    /// LEVEL2:0x2d7c98 and 0x2d7fac index source 0x80-byte native records,
    /// loaded by level_load.cpp from gameplay header +0x60 into 0x1600ec.
    /// Each record's +0x30 translation is already Rac1ClipVolume.Centre.
    /// It is NOT the path-list structure at gameplay header +0x70.
    /// </summary>
    public static int ReadWaypointRecordIndex(ReadOnlySpan<byte> pvar, int recordCount)
    {
        if (recordCount <= 0 || pvar.Length < WaypointRecordPvarOffset + sizeof(int))
            throw new InvalidDataException("Constructorbot waypoint records unavailable.");
        int index = BinaryPrimitives.ReadInt32LittleEndian(
            pvar.Slice(WaypointRecordPvarOffset, sizeof(int)));
        if (index < 0 || index >= recordCount)
            throw new InvalidDataException("Constructorbot waypoint index is out of native record range.");
        return index;
    }

    /// <summary>
    /// Source state8/9 mode -1 compares target's full XYZ distance to the
    /// native record +0x30 centre with PVar +0x184. Never substitute a
    /// spawn-based distance or the oriented point_in_box test.
    /// </summary>
    public static bool NativeWaypointActivation(
        System.Numerics.Vector3 targetNative, System.Numerics.Vector3 waypointNative,
        float triggerRadius)
    {
        if (!float.IsFinite(triggerRadius) || triggerRadius < 0 ||
            !float.IsFinite(targetNative.X) || !float.IsFinite(targetNative.Y) ||
            !float.IsFinite(targetNative.Z) || !float.IsFinite(waypointNative.X) ||
            !float.IsFinite(waypointNative.Y) || !float.IsFinite(waypointNative.Z))
            throw new ArgumentOutOfRangeException(nameof(triggerRadius));
        return System.Numerics.Vector3.Distance(targetNative, waypointNative) < triggerRadius;
    }

    public const int ActivationDistancePvarOffset = 0x184;

    // LEVEL2:0x25b710 original wrapper and walker_record_step (0x258b50):
    // bit4 = current actor inside walker record's +0x2c stop radius;
    // bit0x10 = error at least record +0x28 tolerance. Bits 1/2/8/0x20
    // are collision/floor-probe outputs and MUST be host-provided.
    public const int WalkerNearTargetFlag = 0x4;
    public const int WalkerHeadingOutsideToleranceFlag = 0x10;
    public const int WalkerBlockedFlag = 0x2;
    public const int WalkerTouchedFlag = 0x1;
    public const int WalkerLedgeFlag = 0x8;
    public const int WalkerHeadingOrContactMask = 0x6;
    public const float WalkerStopDistance = .1f;
    // Retail class612 PVar+0x108 (record+0x38) is zero for all 15 bots.
    // This is distinct from the initializer's +0 radius word 0x280.
    public const int WalkerInitialRecordFlags = 0;
    public const float WalkerProbeUp = .5f;
    public const float WalkerProbeDown = .5f;
    public const float WalkerSlopeLookaheadThreshold = .5f;
    public const float WalkerLookaheadSpeedThreshold = .1f;

    public const float WalkerLookaheadDistance = .57f;
    public const float WalkerLedgeHeightMargin = .04f;

    /// <summary>
    /// Original walker_record_step L2:0x258b50 performs a second
    /// walker_probe at a fixed 0.57 native-unit planar lookahead when the
    /// first hit is sufficiently upright and horizontal speed exceeds 0.1.
    /// It blocks with 0x22 when that ahead probe misses, or when its
    /// supported surface remains at least 0.5 radians upright. Record
    /// bit8 permits ledge code8 only when both relevant hit elevations
    /// are below the original Moby Z + 0.04. Original class612 walker
    /// behavior flags +0x38 are zero, so no ledge-mode exception.
    /// The caller MUST provide real scene probe results; an unknown
    /// second probe is not evidence of successful travel.
    /// </summary>
    public static int WalkerLookaheadFlags(
        float firstNormalUp, float planarSpeed, bool? aheadSupported,
        float aheadNormalUp, float firstFloor, float aheadFloor,
        float actorHeight, int recordFlags)
    {
        if (!float.IsFinite(firstFloor) || !float.IsFinite(aheadFloor) ||
            !float.IsFinite(actorHeight) || !float.IsFinite(aheadNormalUp) ||
            aheadNormalUp is < -1 or > 1 ||
            recordFlags < 0 || (recordFlags & ~0x2a9) != 0)
            throw new ArgumentOutOfRangeException(nameof(recordFlags));
        if (!WalkerNeedsLookahead(firstNormalUp, planarSpeed))
            return 0;
        if (aheadSupported is null)
            throw new InvalidDataException("Native walker lookahead has no host probe observation.");

        bool ledge = (recordFlags & WalkerLedgeFlag) != 0;
        float top = actorHeight + WalkerLedgeHeightMargin;
        if (aheadSupported == false)
            return ledge && firstFloor < top ? WalkerLedgeFlag : 0x22;

        // The second source branch uses >=0.5, whereas admission to
        // lookahead uses >0.5. This difference matters on the boundary.
        if (MathF.Asin(aheadNormalUp) < WalkerSlopeLookaheadThreshold)
            return 0;
        return ledge && firstFloor < top && aheadFloor < top
            ? WalkerLedgeFlag : 0x22;
    }


    /// <summary>
    /// walker_probe (L2:0x2583f0) emits a vertical source-native line segment
    /// from candidateZ+up to max(0, candidateZ-down); a missing collision
    /// reports no support. Caller supplies original collision metadata.
    /// </summary>
    public static (float StartZ, float EndZ) WalkerProbeHeights(
        float candidateZ, float up, float down)
    {
        if (!float.IsFinite(candidateZ) || !float.IsFinite(up) ||
            !float.IsFinite(down) || up < 0 || down < 0)
            throw new ArgumentOutOfRangeException(nameof(candidateZ));
        return (candidateZ + up, MathF.Max(0, candidateZ - down));
    }

    /// <summary>
    /// walker_record_step L2:0x258b50: first probe with no valid source
    /// collision stops the walker with 0x22, or ledge code 8 when record
    /// bit 8 is set. A valid source probe contributes no flags by itself.
    /// </summary>
    public static int WalkerInitialProbeFlags(bool hasValidFloorHit, int recordFlags)
    {
        if ((recordFlags & ~0x2a9) != 0 || recordFlags < 0)
            throw new ArgumentOutOfRangeException(nameof(recordFlags));
        return hasValidFloorHit ? 0 : (recordFlags & WalkerLedgeFlag) != 0
            ? WalkerLedgeFlag : 0x22;
    }

    /// <summary>
    /// The native post-move contact response rolls back movement if source
    /// floor height differs from the previous Moby Z by more than record
    /// +8 downward reach or +0xc upward reach. Boundaries are inclusive.
    /// </summary>
    public static bool WalkerFloorHeightRejectsStep(
        float previousZ, float hitFloorZ, float maximumDown, float maximumUp)
    {
        if (!float.IsFinite(previousZ) || !float.IsFinite(hitFloorZ) ||
            !float.IsFinite(maximumDown) || !float.IsFinite(maximumUp) ||
            maximumDown < 0 || maximumUp < 0)
            throw new ArgumentOutOfRangeException(nameof(hitFloorZ));
        return previousZ - hitFloorZ > maximumDown ||
            hitFloorZ - previousZ > maximumUp;
    }

    /// <summary>
    /// The original walker checks atan2(surfaceNormalZ, normalXYLength) &gt; .5
    /// and horizontal speed &gt; .1 before performing its ahead-of-actor
    /// slope/ledge probe. Godot surfaces use Y as vertical instead.
    /// </summary>
    public static bool WalkerNeedsLookahead(float verticalSurfaceNormal, float planarSpeed)
    {
        if (!float.IsFinite(verticalSurfaceNormal) ||
            !float.IsFinite(planarSpeed) || verticalSurfaceNormal is < -1 or > 1 ||
            planarSpeed < 0)
            throw new ArgumentOutOfRangeException(nameof(verticalSurfaceNormal));
        return MathF.Asin(verticalSurfaceNormal) > WalkerSlopeLookaheadThreshold &&
            planarSpeed > WalkerLookaheadSpeedThreshold;
    }

    // walker_move_v return bit2 indicates floor contact. The same bit
    // has a different meaning (blocked) in walker_record_step's flags.
    // Original class612 source calls L2:0x25b1c0 on its PVar+0xd0
    // walker record. Shared retail initializer stores fixed-point 0x280
    // at record +0, consumed by walker_move_fx as radius/1024, while
    // behavior flags are a DIFFERENT word at record +0x38.
    public const int WalkerCollisionRadiusFixed = 0x280;
    public const float WalkerCollisionRadius = WalkerCollisionRadiusFixed / 1024f;
    public const float WalkerCollisionUp = 1f;
    public const int WalkerRecordBehaviorFlags = 0;

    /// <summary>
    /// Source walker_move_v L2:0x258830 only sweeps when the full 3D
    /// requested displacement is STRICTLY longer than the collision
    /// radius. The original segment goes from old+up to
    /// (old+displacement)+(displacement normalised to radius)+up.
    /// This is NOT equivalent to a ray that ends at the requested point.
    /// </summary>
    public readonly record struct WalkerSweepSegment(
        System.Numerics.Vector3 Start,
        System.Numerics.Vector3 End,
        System.Numerics.Vector3 RadiusAdvance);

    public static WalkerSweepSegment? BuildWalkerSweep(
        System.Numerics.Vector3 oldPosition, System.Numerics.Vector3 requestedMove,
        float radius, float up)
    {
        if (!Finite(oldPosition) || !Finite(requestedMove) ||
            !float.IsFinite(radius) || radius < 0 ||
            !float.IsFinite(up) || up < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        float length = requestedMove.Length();
        if (length <= radius || length <= 0)
            return null;
        var advance = requestedMove * (radius / length);
        var raised = System.Numerics.Vector3.UnitZ * up;
        return new(oldPosition + raised,
            oldPosition + requestedMove + advance + raised, advance);
    }

    /// <summary>
    /// Upon source segment hit, original Moby position becomes recorded
    /// collision hit - radiusAdvance - vertical up offset. The following
    /// six sphere-pushouts / partial floor-lift are separate native work.
    /// </summary>
    public static System.Numerics.Vector3 WalkerSweptHitPosition(
        System.Numerics.Vector3 hit, WalkerSweepSegment sweep, float up)
    {
        if (!Finite(hit) || !Finite(sweep.Start) || !Finite(sweep.End) ||
            !Finite(sweep.RadiusAdvance) || !float.IsFinite(up) || up < 0)
            throw new ArgumentOutOfRangeException(nameof(up));
        return hit - sweep.RadiusAdvance -
            System.Numerics.Vector3.UnitZ * up;
    }

    private static bool Finite(System.Numerics.Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    public const int WalkerMoveFloorContactFlag = 0x2;
    public const float WalkerCollisionMinimumForwardFraction = .2f;
    public const float WalkerMoveFloorLiftFraction = .3f;
    public const float WalkerMoveGroundProbeExtension = .05f;
    public const int WalkerSpherePushoutAttempts = 6;

    /// <summary>
    /// Source walker_move_v at L2:0x258830. A source collision (return
    /// bit1) is distinct from its floor-contact bit2. Only bit1 makes
    /// walker_record_step set result bit1; bit2 resets its vertical
    /// velocity, but is NOT itself a returned state-transition flag.
    /// The observed travel projected onto the requested planar direction
    /// must be strictly below 0.2 of that requested length for bit2.
    /// A floor_probe_v miss after touching is rejected by the host because
    /// it cannot support the original distance test without a hit height.
    /// A floor delta outside the source record's up/down limits adds 0x22
    /// and requires restoring the old position.
    /// </summary>
    public readonly record struct WalkerContactFrame(int Flags, bool RestorePreviousPosition);

    public static WalkerContactFrame WalkerPostMoveContact(
        System.Numerics.Vector3 requestedNative,
        System.Numerics.Vector3 actualNative, int hostMoveFlags,
        float previousZ, float? floorZ)
    {
        if (!float.IsFinite(requestedNative.X) || !float.IsFinite(requestedNative.Y) ||
            !float.IsFinite(requestedNative.Z) || !float.IsFinite(actualNative.X) ||
            !float.IsFinite(actualNative.Y) || !float.IsFinite(actualNative.Z) ||
            !float.IsFinite(previousZ) ||
            floorZ is { } observed && !float.IsFinite(observed) ||
            hostMoveFlags < 0 || (hostMoveFlags & ~3) != 0)
            throw new ArgumentOutOfRangeException(nameof(hostMoveFlags));

        if ((hostMoveFlags & WalkerTouchedFlag) == 0)
            return new(0, false);

        int flags = WalkerTouchedFlag;
        float planarLength = MathF.Sqrt(
            requestedNative.X * requestedNative.X +
            requestedNative.Y * requestedNative.Y);
        if (planarLength > 0)
        {
            float along = (actualNative.X * requestedNative.X +
                actualNative.Y * requestedNative.Y) / planarLength;
            if (along < planarLength * WalkerCollisionMinimumForwardFraction)
                flags |= WalkerBlockedFlag;
        }

        if (!floorZ.HasValue ||
            WalkerFloorHeightRejectsStep(previousZ, floorZ.Value,
                WalkerProbeDown, WalkerProbeUp))
            return new(flags | 0x22, true);

        return new(flags, false);
    }

    /// <summary>
    /// Source walker_move_v's support correction: after a successful
    /// segment test to below (posZ-drop-0.05), if recorded floor is higher
    /// than posZ-drop then lift by 30% of the difference. It never lowers
    /// the Moby during this support correction.
    /// </summary>
    public static float WalkerGroundLift(float positionZ, float hitFloorZ, float drop)
    {
        if (!float.IsFinite(positionZ) || !float.IsFinite(hitFloorZ) ||
            !float.IsFinite(drop) || drop < 0)
            throw new ArgumentOutOfRangeException(nameof(drop));
        float bottom = positionZ - drop;
        return hitFloorZ > bottom
            ? (hitFloorZ - bottom) * WalkerMoveFloorLiftFraction : 0f;
    }

    public const float WalkerYawTolerance = 1.57f;
    public const float WalkerAcceleration = .3f;
    public const float WalkerMaxSpeed = 2.7f;
    public const float WalkerDrag = .3f;

    /// <summary>
    /// Source 0x25b710 + walker_record_step. The collision bits are supplied
    /// by the caller, never inferred from target distance. This flag builder
    /// is deliberately independent of Godot physics.
    /// </summary>
    public static int WalkerFlags(float distanceToTarget, float yawErrorRadians, int collisionFlags)
    {
        if (!float.IsFinite(distanceToTarget) || distanceToTarget < 0 ||
            !float.IsFinite(yawErrorRadians) ||
            (collisionFlags & ~0x2b) != 0)
            throw new ArgumentOutOfRangeException(nameof(collisionFlags));
        int flags = collisionFlags;
        if (distanceToTarget < WalkerStopDistance) flags |= WalkerNearTargetFlag;
        float error = MathF.Abs(MathF.Atan2(MathF.Sin(yawErrorRadians), MathF.Cos(yawErrorRadians)));
        if (error >= WalkerYawTolerance) flags |= WalkerHeadingOutsideToleranceFlag;
        return flags;
    }

    /// <summary>
    /// Walker's source record recurrence at 0x258b50 before collision:
    /// speed += min(accel * planarDistance, accel), reduced by record drag
    /// if within speed*10, then clamped to ±vmax. Source 0x25b710 uses
    /// (tolerance-angle)/tolerance as its walker scale while a short
    /// stateful turn timer is live. This method only computes speed,
    /// not collisions or inferred source timer validity.
    /// </summary>
    public static float WalkerSpeed(float previousSpeed, float planarDistance, float movementScale)
    {
        if (!float.IsFinite(previousSpeed) || !float.IsFinite(planarDistance) ||
            !float.IsFinite(movementScale) || planarDistance < 0 ||
            movementScale < 0 || movementScale > 1)
            throw new ArgumentOutOfRangeException(nameof(movementScale));
        float acceleration = WalkerAcceleration * movementScale;
        float speed = previousSpeed + MathF.Min(acceleration * planarDistance, acceleration);
        if (planarDistance < speed * 10) speed -= WalkerDrag * speed;
        float limit = WalkerMaxSpeed * movementScale;
        return Math.Clamp(speed, -limit, limit);
    }

    /// <summary>
    /// Native state5 tests entire solver return == 4 (not &amp;4);
    /// if true enters state7/animation0. State6 tests
    /// (return &amp; 0x6)==0 for state7, otherwise enters state5/clip6.
    /// Unknown contact bits must not be silently treated as zero.
    /// </summary>
    public static Frame StepWalkerState(int nativeState, int solverFlags, bool resultKnown)
    {
        if (nativeState is not (5 or 6))
            throw new ArgumentOutOfRangeException(nameof(nativeState));
        if (!resultKnown) return nativeState == 5 ? new(5, 6) : new(6, 6);
        if (solverFlags < 0 || (solverFlags & ~0x3f) != 0)
            throw new ArgumentOutOfRangeException(nameof(solverFlags));
        if (nativeState == 5)
            return solverFlags == WalkerNearTargetFlag ? new(RestState, 0) : new(5, 6);
        return (solverFlags & WalkerHeadingOrContactMask) == 0
            ? new(RestState, 0) : new(5, 6);
    }


    public readonly record struct StateThreeVerticalFrame(
        float Height, float VerticalStep, bool ReachedFloor);

    /// <summary>
    /// Native LEVEL2:0x2d7aac..0x2d7aec first calls approach_f on PVar+0x48
    /// toward 10*frameScale by its acceleration argument, then calls it
    /// on Moby Z toward floor_probe_v(0.5, position, 0), using that PVar
    /// step. Source approach_f_v at 0x25b868 clamps the distance exactly.
    /// A missing collision floor holds position and never admits state4.
    /// </summary>
    public static StateThreeVerticalFrame StepStateThreeVertical(
        float currentHeight, float currentVerticalStep, float? floorHeight,
        float sourceFrameScale, float sourceAcceleration)
    {
        if (!float.IsFinite(currentHeight) || !float.IsFinite(currentVerticalStep) ||
            currentVerticalStep < 0 || !float.IsFinite(sourceFrameScale) ||
            sourceFrameScale < 0 || sourceFrameScale > 1 ||
            !float.IsFinite(sourceAcceleration) || sourceAcceleration < 0 ||
            (floorHeight.HasValue && !float.IsFinite(floorHeight.Value)))
            throw new ArgumentOutOfRangeException(nameof(sourceAcceleration));

        // approach_f(pvar+0x48, 10*frameScale, sourceAcceleration)
        float step = ApproachNativeScalar(currentVerticalStep,
            10f * sourceFrameScale, sourceAcceleration);
        if (floorHeight is not { } floor)
            return new(currentHeight, step, false);
        // approach_f(moby+0x18, floor_probe, pvar+0x48)
        float height = ApproachNativeScalar(currentHeight, floor, step);
        return new(height, step, height == floor);
    }

    public static float ApproachNativeScalar(float value, float target, float step)
    {
        if (!float.IsFinite(value) || !float.IsFinite(target) ||
            !float.IsFinite(step) || step < 0)
            throw new ArgumentOutOfRangeException(nameof(step));
        float distance = target - value;
        if (MathF.Abs(distance) <= step)
            return target;
        return value + Math.Clamp(distance, -step, step);
    }

    /// <summary>
    /// LEVEL2:0x2d7a44..0x2d7a98: state 3 subtracts the live Moby position
    /// from the PVar+0x188 record's centre, ZEROES the resulting native Z,
    /// clamps the horizontal displacement to (source frame scale * 6), then
    /// adds it to Moby+0x10. Floor projection and vertical approach happen
    /// separately after this calculation and are host responsibilities.
    /// Source frame scale (global 0x15ed6c) is supplied, never fabricated
    /// by this engine-neutral method.
    /// </summary>
    public static System.Numerics.Vector3 StepStateThreeHorizontal(
        System.Numerics.Vector3 currentNative,
        System.Numerics.Vector3 waypointNative, float sourceFrameScale)
    {
        if (!float.IsFinite(currentNative.X) || !float.IsFinite(currentNative.Y) ||
            !float.IsFinite(currentNative.Z) || !float.IsFinite(waypointNative.X) ||
            !float.IsFinite(waypointNative.Y) || !float.IsFinite(waypointNative.Z) ||
            !float.IsFinite(sourceFrameScale) || sourceFrameScale < 0 || sourceFrameScale > 1)
            throw new ArgumentOutOfRangeException(nameof(sourceFrameScale));
        var horizontal = new System.Numerics.Vector2(
            waypointNative.X - currentNative.X, waypointNative.Y - currentNative.Y);
        float distance = horizontal.Length();
        float maximumStep = 6f * sourceFrameScale;
        if (distance > maximumStep && distance > 0)
            horizontal *= maximumStep / distance;
        return new System.Numerics.Vector3(horizontal.X, horizontal.Y, 0);
    }

    /// <summary>
    /// Bounded host angular adapter for source state3's yaw_turn_damped:
    /// the source turn target is Ratchet, not the navigation waypoint.
    /// This uses the native frame-scale * PI/2 cap from L2:0x2d7a04,
    /// but NOT the complete native damping recurrence.
    /// angleError is computed against the actual Godot actor heading.
    /// </summary>
    public static float StepStateThreeYaw(float currentYaw, float angleErrorRadians, float sourceFrameScale)
    {
        if (!float.IsFinite(currentYaw) || !float.IsFinite(angleErrorRadians) ||
            !float.IsFinite(sourceFrameScale) || sourceFrameScale < 0 || sourceFrameScale > 1)
            throw new ArgumentOutOfRangeException(nameof(sourceFrameScale));
        float error = MathF.Atan2(MathF.Sin(angleErrorRadians), MathF.Cos(angleErrorRadians));
        float cap = sourceFrameScale * MathF.PI / 2f;
        return currentYaw + Math.Clamp(error, -cap, cap);
    }

    /// <summary>
    /// L2:0x2d7aec..0x2d7b2c: after source floor probing, state3 becomes
    /// state4/clip5 only when Moby Z has reached the probed floor height.
    /// An unresolved host floor probe must not be treated as success.
    /// </summary>
    public static Frame StepStateThreeLanding(bool? sourceFloorHeightReached) =>
        sourceFloorHeightReached == true ? new(4, 5) : new(3, 4);

    /// <summary>
    /// L2:0x2d7b34..0x2d7b5c: native state4 waits for source animation
    /// completion before entering state5/clip6.
    /// </summary>
    public static Frame StepStateFour(bool animationCompleted) =>
        animationCompleted ? new(5, 6) : new(4, 5);
    public const float FollowupEngagementMargin = 1f;
    public const float FollowupHeightTolerance = 3f;

    public static int ReadActivationVolume(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length < ActivationVolumePvarOffset + sizeof(int))
            throw new InvalidDataException("Constructorbot has no native activation volume selector.");
        int index = BinaryPrimitives.ReadInt32LittleEndian(
            pvar.Slice(ActivationVolumePvarOffset, sizeof(int)));
        if (index is < -1 or > 65535)
            throw new InvalidDataException("Constructorbot activation volume selector is invalid.");
        return index;
    }

    public static float ReadActivationDistance(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length < ActivationDistancePvarOffset + sizeof(float))
            throw new InvalidDataException("Constructorbot has no native activation distance.");
        float value = BinaryPrimitives.ReadSingleLittleEndian(
            pvar.Slice(ActivationDistancePvarOffset, sizeof(float)));
        if (!float.IsFinite(value) || value < 0)
            throw new InvalidDataException("Constructorbot activation distance is invalid.");
        return value;
    }

    /// <summary>
    /// LEVEL2:0x2d7f8c..0x2d8080: authored modes 8/9 share the activation
    /// decision. If +0x18c is nonnegative it is a native clip-volume index;
    /// otherwise target distance from the native 0x80-byte clip record +0x30
    /// is compared against PVar +0x184. Null means that a required
    /// source check is unresolved: never treat it as a true activation.
    /// Mode8 uses source sequence4 and mode9 sequence16 on admission.
    /// </summary>
    public static Frame StepActivation(int nativeState, bool? triggerSatisfied)
    {
        if (nativeState is not (ParticleModeState or ParticleAlternateState))
            throw new ArgumentOutOfRangeException(nameof(nativeState));
        if (triggerSatisfied != true)
            return new(nativeState, nativeState == ParticleModeState ? 2 : 15);
        return new(2, nativeState == ParticleModeState ? 4 : 16);
    }

    /// <summary>
    /// LEVEL2:0x2d79a4: state2 retains its entry clip (4 from mode8,
    /// 16 from mode9) until source animation completion, then enters
    /// state3 and selects sequence4. No host time-based substitute.
    /// </summary>
    public static Frame StepActivatedAnimation(
        bool animationCompleted, int entrySequence)
    {
        if (entrySequence is not (4 or 16))
            throw new ArgumentOutOfRangeException(nameof(entrySequence));
        return animationCompleted ? new(3, 4) : new(2, entrySequence);
    }

    /// <summary>
    /// LEVEL2:0x2d82a0..0x2d83a8: source state12 turns toward target,
    /// waits for animation completion, then chooses state7/sequence0 if
    /// target planar distance is at least engagement radius + 1 OR if its
    /// vertical difference is at least 3. Otherwise stays in state12/seq9.
    /// </summary>
    public static Frame StepFollowup(bool animationCompleted, float planarDistance,
        float verticalDelta, float engagementRadius)
    {
        if (!animationCompleted)
            return new(FollowupState, 9);
        if (!float.IsFinite(planarDistance) || !float.IsFinite(verticalDelta) ||
            !float.IsFinite(engagementRadius) || planarDistance < 0 || engagementRadius <= 0)
            throw new ArgumentOutOfRangeException(nameof(planarDistance));
        return planarDistance >= engagementRadius + FollowupEngagementMargin ||
            MathF.Abs(verticalDelta) >= FollowupHeightTolerance
            ? new(RestState, 0)
            : new(FollowupState, 9);
    }

    /// <summary>
    /// LEVEL2:0x2d838c: source state13 calls enemy_physics and returns
    /// to state7/animation0 only when the native result has flag 0x40.
    /// The caller must supply an actual physics contact signal, not a timer.
    /// </summary>
    public static Frame StepRecovery(bool nativePhysicsContactFlag40, int currentSequence) =>
        nativePhysicsContactFlag40 ? new(RestState, 0) : new(ContactRecoveryState, currentSequence);

    public const float State7HeightTolerance = 4f;
    public const float State7WaypointTolerance = 1f;
    public const float State7FacingTolerance = MathF.PI / 36f; // 5 degrees

    public static float ReadEngagementRadius(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length < EngagementRadiusPvarOffset + sizeof(float))
            throw new InvalidDataException("Constructorbot has no native engagement radius.");
        float value = BinaryPrimitives.ReadSingleLittleEndian(
            pvar.Slice(EngagementRadiusPvarOffset, sizeof(float)));
        if (!float.IsFinite(value) || value is <= 0 or > 1000f)
            throw new InvalidDataException("Constructorbot engagement radius is invalid.");
        return value;
    }

    /// <summary>
    /// Source state7 branch L2:0x2d7c98..0x2d7dd4:
    /// target within native PVar+0x180 radius of waypoint, vertical delta
    /// less than 4, Moby within one unit of waypoint, facing error below
    /// about five degrees. Original waypoint is the decoded header +0x60
    /// native record at PVar +0x188, with centre at record offset +0x30.
    /// </summary>
    public static bool State7AdmitsJointContact(
        float targetToWaypointDistance, float targetVerticalDelta,
        float mobyToWaypointDistance, float facingErrorRadians, float engagementRadius)
    {
        return float.IsFinite(targetToWaypointDistance) &&
            float.IsFinite(targetVerticalDelta) &&
            float.IsFinite(mobyToWaypointDistance) &&
            float.IsFinite(facingErrorRadians) &&
            float.IsFinite(engagementRadius) &&
            targetToWaypointDistance >= 0 &&
            MathF.Abs(targetVerticalDelta) < State7HeightTolerance &&
            mobyToWaypointDistance >= 0 &&
            targetToWaypointDistance < engagementRadius &&
            mobyToWaypointDistance < State7WaypointTolerance &&
            MathF.Abs(facingErrorRadians) < State7FacingTolerance;
    }

    public const int RestState = 7;
    public const int ParticleModeState = 8;
    public const int ParticleAlternateState = 9;
    public const int AimState = 10;
    public const int JointContactState = 11;
    public const int FollowupState = 12;
    public const int ContactRecoveryState = 13;

    public readonly record struct Frame(int NativeState, int Sequence, bool JointContactQuery = false);
    public const int SourceEmitterParticleId = 15;
    public const int NativeEmitterCadenceMask = 3;

    /// <summary>
    /// LEVEL2:0x2d7ecc..0x2d7f8c. Only state 8 invokes source particle
    /// program 15, when the original global frame counter low two bits are
    /// zero and Moby+0x31 is nonzero. The emission position is semantic joint
    /// zero; native particle parameters remain in level globals and are not
    /// invented here. State 9 shares the following detection code but does
    /// NOT perform the particle spawn.
    /// </summary>
    public static bool ShouldEmitSourceParticle(
        int nativeState, uint sourceFrameCounter, bool mobyActive) =>
        nativeState == ParticleModeState && mobyActive &&
        (sourceFrameCounter & NativeEmitterCadenceMask) == 0;

    public static int ReadInitialMode(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length < InitialModePvarOffset + sizeof(int))
            throw new InvalidDataException("Aridia Constructorbot has no initial variant PVar.");
        int mode = BinaryPrimitives.ReadInt32LittleEndian(pvar.Slice(InitialModePvarOffset, sizeof(int)));
        if (mode is < 0 or > 3)
            throw new InvalidDataException("Unknown retail Constructorbot startup mode.");
        return mode;
    }

    // L2:0x2d7808..0x2d7998:
    // +0x194==0 -> state 7, sequence 0; ==1 -> state 10, sequence 0;
    // ==2 -> state 8, sequence 2; ==3 -> state 9, sequence 15.
    // This corresponds to four DIFFERENT authored populations, not a
    // universal stationary flamethrower on Aridia level load.
    public static Frame Initialize(ReadOnlySpan<byte> pvar) => ReadInitialMode(pvar) switch
    {
        0 => new(RestState, 0),
        1 => new(AimState, 0),
        2 => new(ParticleModeState, 2),
        3 => new(ParticleAlternateState, 15),
        _ => throw new InvalidDataException("Unknown Constructorbot startup mode.")
    };

    /// <summary>
    /// L2:0x2d8088 moves native state 10 into 12/sequence 9 only after
    /// the original animation-completion bit at Moby+0x70 &amp; 2.
    /// </summary>
    public static Frame StepAim(bool animationCompleted) =>
        animationCompleted ? new(FollowupState, 9) : new(AimState, 0);

    /// <summary>
    /// L2:0x2d812c (the jump table's STATE 11, not state 12) queries a
    /// transformed joint-0 contact volume EVERY update. Completion changes
    /// to state 13 and randomly selects sequence 8 or 10; the actual
    /// source damage solver and host contact result remain external.
    /// </summary>
    public static Frame StepJointContact(bool animationCompleted, bool chooseSequenceTen) =>
        animationCompleted
            ? new(ContactRecoveryState, chooseSequenceTen ? 10 : 8, JointContactQuery: true)
            : new(JointContactState, 8, JointContactQuery: true);
}
