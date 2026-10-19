using Godot;
using OBP.Godot;
using OBP.RAC1.Player;

namespace OneBigPackage;

public partial class PlayerHost
{
    internal string Rac1SmallStepDecision { get; private set; } = "none";
    private bool RejectRac1SmallStep(string reason)
    {
        Rac1SmallStepDecision = reason;
        return false;
    }
    // Godot adapter policy, NOT a recovered retail step-height field. Use the
    // lower native query height and ordinary support angle to bound a rounded
    // contact's clearance (.411 units). Merely subtracting radius (.25) excluded
    // ordinary small lips which retail Ratchet can cross, including the saved
    // ship-side repro. Actual top support and full-body sweeps remain required.
    private static readonly float Rac1HostMaximumStepRise =
        Rac1CollisionProfile.OrdinaryLowerContactHeight - Rac1CollisionProfile.OrdinaryHorizontalRadius *
        MathF.Cos((float)Rac1OrdinaryGroundContactMotion.OrdinarySupportMaxAngleRadians);
    private static readonly float Rac1HostMinimumStepCornerUp =
        (Rac1CollisionProfile.OrdinaryHorizontalRadius - Rac1HostMaximumStepRise) /
        Rac1CollisionProfile.OrdinaryHorizontalRadius;

    private void MoveRac1Contact(float delta)
    {
        Transform3D from = GlobalTransform;
        Vector3 request = Velocity;
        long profileStart = DevelopmentTimestamp();
        MoveAndSlide();
        long profileSlide = DevelopmentTimestamp();
        long profileStep = profileSlide;
        Rac1SmallStepDecision = _rac1SmallStepEligible ? "clear" : "not-ordinary-ground";
        if (_rac1SmallStepEligible)
        {
            Vector3 direction = new Vector3(request.X, 0f, request.Z).Normalized();
            Vector3 inward = Vector3.Zero;
            float opposition = -.01f;
            // Use the real slide contacts: a horizontal-only predictive sweep
            // misses some corners encountered by the native downward request.
            // An opposed rounded contact can also block a slow walk before
            // it reaches the top. Only the separate top query owns slope admission.
            for (int i = 0; i < GetSlideCollisionCount(); i++)
            {
                var collision = GetSlideCollision(i);
                for (int j = 0; j < collision.GetCollisionCount(); j++)
                {
                    Vector3 normal = collision.GetNormal(j);
                    float dot = normal.Dot(direction);
                    if (dot < opposition)
                    {
                        opposition = dot;
                        inward = new Vector3(-normal.X, 0f, -normal.Z).Normalized();
                    }
                }
            }
            if (inward != Vector3.Zero)
                TryMoveRac1SmallStep(from, request, inward, delta);
            profileStep = DevelopmentTimestamp();
            SettleRac1GroundCrest(request.Y * delta, delta);
        }
        if (DevelopmentProfileEnabled)
            DevelopmentContactProfile = (DevelopmentElapsed(profileStart, profileSlide),
                DevelopmentElapsed(profileSlide, profileStep), DevelopmentElapsed(profileStep, DevelopmentTimestamp()));
    }

    private bool TryMoveRac1SmallStep(Transform3D from, Vector3 request, Vector3 inward, float delta)
    {
        Vector3 planar = new Vector3(request.X, 0f, request.Z) * delta;
        if (planar.LengthSquared() < .00000001f)
            return RejectRac1SmallStep("no-planar-request");

        using var parameters = new PhysicsTestMotionParameters3D
        {
            From = from,
            Motion = planar,
            Margin = SafeMargin,
            MaxCollisions = 4,
        };
        using var result = new PhysicsTestMotionResult3D();
        float walkableUp = MathF.Cos((float)Rac1OrdinaryGroundContactMotion.OrdinarySupportMaxAngleRadians);

        // Look across the lower capsule's leading hemisphere for a real top.
        // This point is only a support witness; it never adds planar travel.
        Vector3 support = from.Origin + planar + inward * Rac1CollisionProfile.OrdinaryHorizontalRadius;
        using var ray = PhysicsRayQueryParameters3D.Create(
            support + Vector3.Up * (Rac1HostMaximumStepRise + SafeMargin),
            support + Vector3.Down * SafeMargin);
        ray.Exclude = new global::Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (hit.Count == 0 ||
            hit["collider"].As<Node>() is not RuntimeWorldScene.RuntimeCollisionBody3D body ||
            ((Vector3)hit["normal"]).Y < walkableUp)
            return RejectRac1SmallStep("no-walkable-static-top");

        int? material = body.MaterialIdForFace((int)hit["face_index"]);
        if (material is not (>= byte.MinValue and <= byte.MaxValue) ||
            Rac1SurfaceActionRouting.Select(Rac1PlayerContactResult.StaticWorld(true, rawFaceType: material)) is not null)
            return RejectRac1SmallStep("special-or-unknown-surface");

        float supportRise = ((Vector3)hit["position"]).Y - from.Origin.Y;
        if (supportRise <= SafeMargin || supportRise > Rac1HostMaximumStepRise)
            return RejectRac1SmallStep(FormattableString.Invariant($"support-rise:{supportRise:F6}"));

        // Measure the lip from terrain beneath the feet as well. The rounded
        // capsule can already sit part way up a wall corner; using its raised
        // origin alone would gradually admit a taller wall as a small step.
        ray.From = from.Origin + Vector3.Up * SafeMargin;
        ray.To = from.Origin + Vector3.Down * (Rac1HostMaximumStepRise + SafeMargin);
        var priorSupport = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (priorSupport.Count == 0 ||
            priorSupport["collider"].As<Node>() is not RuntimeWorldScene.RuntimeCollisionBody3D ||
            ((Vector3)hit["position"]).Y - ((Vector3)priorSupport["position"]).Y > Rac1HostMaximumStepRise)
            return RejectRac1SmallStep("missing-base-or-lip-over-limit");

        float lift = supportRise + SafeMargin;
        parameters.Motion = Vector3.Up * lift;
        if (PhysicsServer3D.BodyTestMotion(GetRid(), parameters, result))
            return RejectRac1SmallStep("upward-clearance-blocked");

        Transform3D raised = from;
        raised.Origin += Vector3.Up * lift;
        parameters.From = raised;
        parameters.Motion = planar;
        if (PhysicsServer3D.BodyTestMotion(GetRid(), parameters, result))
            return RejectRac1SmallStep("raised-forward-blocked");

        raised.Origin += planar;
        parameters.From = raised;
        parameters.Motion = Vector3.Down * (lift + SafeMargin +
            (float)Rac1OrdinaryGroundContactMotion.GroundDownwardRequestPerTick);
        parameters.RecoveryAsCollision = true;
        if (!PhysicsServer3D.BodyTestMotion(GetRid(), parameters, result) ||
            result.GetCollisionCount() == 0 || result.GetCollisionNormal(0).Y < Rac1HostMinimumStepCornerUp ||
            result.GetCollider(0) is not RuntimeWorldScene.RuntimeCollisionBody3D)
            return RejectRac1SmallStep("no-valid-landing-contact");

        Vector3 landing = raised.Origin + result.GetTravel();
        float rise = landing.Y - from.Origin.Y;
        Vector3 direction = planar.Normalized();
        if (rise < -SafeMargin || rise > Rac1HostMaximumStepRise ||
            (landing - GlobalPosition).Dot(direction) <= .00001f)
            return RejectRac1SmallStep("landing-outside-envelope-or-no-progress");

        // All three shape sweeps succeeded. The top's support normal passed
        // the ordinary 50-degree gate; the enclosing capsule's rounded corner
        // can have a steeper radial normal. Classify that bounded corner as a
        // floor for this settling pass only, keeping those two normals separate.
        // The original source request still owns feedback, speed and animation;
        // this placement correction must not become extra crouch momentum.
        GlobalPosition = landing;
        // A margin measured along the corner normal needs more vertical reach
        // than the same margin on a flat floor, especially at walking speed.
        float settleReach = (float)Rac1OrdinaryGroundContactMotion.GroundDownwardRequestPerTick +
            SafeMargin / Rac1HostMinimumStepCornerUp;
        Velocity = Vector3.Down * (settleReach / delta);
        float savedFloorAngle = FloorMaxAngle;
        try
        {
            FloorMaxAngle = Math.Max(savedFloorAngle,
                MathF.Acos(Rac1HostMinimumStepCornerUp) + .001f);
            MoveAndSlide();
        }
        finally
        {
            FloorMaxAngle = savedFloorAngle;
            Velocity = request;
        }
        Rac1SmallStepDecision = "stepped";
        return true;
    }

    private void SettleRac1GroundCrest(float requestedRise, float delta)
    {
        if (!_rac1SmallStepEligible || IsOnFloor() || requestedRise <= 0f)
            return;

        // A prior uphill tangent can overshoot the flat top for one update.
        // Native support correction is separate from radial collision; the
        // foot capsule alone would turn that harmless crest into a small fall.
        // Undo at most this update's ascent plus the existing contact skin.
        using var parameters = new PhysicsTestMotionParameters3D
        {
            From = GlobalTransform,
            Motion = Vector3.Down * (requestedRise + SafeMargin),
            Margin = SafeMargin,
            MaxCollisions = 4,
            RecoveryAsCollision = true,
        };
        using var result = new PhysicsTestMotionResult3D();
        if (!PhysicsServer3D.BodyTestMotion(GetRid(), parameters, result) ||
            result.GetCollisionCount() == 0 ||
            result.GetCollisionNormal(0).Y < MathF.Cos((float)Rac1OrdinaryGroundContactMotion.OrdinarySupportMaxAngleRadians) ||
            result.GetCollider(0) is not RuntimeWorldScene.RuntimeCollisionBody3D)
            return;

        Vector3 request = Velocity;
        Velocity = parameters.Motion / delta;
        MoveAndSlide();
        Velocity = request;
    }
}
