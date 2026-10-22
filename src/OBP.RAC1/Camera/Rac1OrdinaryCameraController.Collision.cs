using OBP.Core.Math;

namespace OBP.RAC1.Camera;

public sealed partial class Rac1OrdinaryCameraController
{
    public const double CollisionBaseRadius = .25d;
    public const int CollisionProbeCount = 8;
    public const double CollisionRadiusStep = .10000000149011612d;
    public const double CollisionEyeRadius = CollisionBaseRadius + CollisionRadiusStep * (CollisionProbeCount - 1);

    // 0x002e5830: keep the height anchor between the floor and ceiling.
    private Vec3 ResolveHeightAnchor(IRac1CameraGeometry geometry, Vec3 player, out double targetHeight)
    {
        targetHeight = OrdinaryEyeHeight;
        Vec3 from = player + new Vec3(0, 0, .5d);
        Vec3 top = geometry.Segment(from, from + new Vec3(0, 0, 20d)) ?? from + new Vec3(0, 0, 20d);
        Vec3 bottom = geometry.Segment(from, from - new Vec3(0, 0, 20d)) ?? from - new Vec3(0, 0, 20d);
        Vec3 resolved;
        if (Length(top - bottom) < CollisionEyeRadius * 2d)
            resolved = (top + bottom) * .5d;
        else if (Length(top - player) <= _preferredEyeHeight + CollisionEyeRadius)
            resolved = top - new Vec3(0, 0, CollisionEyeRadius);
        else
        {
            if (bottom.Z < player.Z) bottom = player;
            return bottom + new Vec3(0, 0, _preferredEyeHeight);
        }
        targetHeight = resolved.Z - player.Z;
        return resolved;
    }

    // 0x002e7a30 / 0x002e7318 / 0x002e7d20, ordinary upright camera.
    // Authored camera exclusion volumes are a separate native owner.
    private void ResolveObstruction(IRac1CameraGeometry geometry, Vec3 anchor, double rightX, double rightY)
    {
        bool blocked = false;
        Vec3 direction = WithLength(_desiredOffset, 1d);
        Vec3 point = anchor;
        double travelled = 0d, radius = CollisionBaseRadius;
        for (int i = 0; i < CollisionProbeCount - 1; i++)
        {
            if (i > 0 && geometry.Sphere(point, radius) is { } hit &&
                !(i == 1 && Dot(hit.Point - point, _desiredOffset) < 0d))
            {
                Vec3 start = point;
                if (TrySlide(geometry, anchor, ref point, hit, travelled, radius))
                {
                    blocked = true;
                    if (Math.Abs(rightX) > .05000000074505806d && Math.Abs(WithLength(hit.Point - start, 1d).Z) < .75d)
                    {
                        _obstructionCorrection = Math.Min(_obstructionCorrection + Rac1CameraRecurrence.ObstructionPullInStep,
                            OrdinaryPreferredRadius - Rac1CameraRecurrence.ObstructionInnerRadiusFloor);
                        _obstructionReleaseTicks = Rac1CameraRecurrence.ObstructionContactTimerTicks;
                    }
                    _desiredOffset = WithLength(point - anchor, OrdinaryPreferredRadius - _obstructionCorrection);
                    direction = WithLength(_desiredOffset, 1d);
                }
            }
            radius += CollisionRadiusStep;
            travelled += radius;
            point += direction * (radius * (OrdinaryPreferredRadius - _obstructionCorrection) / OrdinaryPreferredRadius);
        }

        double before = Length(_desiredOffset);
        point = anchor + _desiredOffset;
        bool hitWall = false;
        _lateralSide = 0;
        for (int i = 0; i < 3; i++)
        {
            if (geometry.Sphere(point, CollisionEyeRadius) is not { } hit) break;
            hitWall = true;
            if (i == 0) _lateralSide = Rac1CameraRecurrence.ClassifyLateralSide(WithLength(hit.Point - point, 1d).Z);
            point = hit.ResolvedCenter;
        }
        _desiredOffset = point - anchor;
        if (_lateralSide != 0)
        {
            // 0x002e7138: a predominantly vertical contact changes the direction
            // while retaining the current radius; walls may shorten it below.
            _desiredOffset = WithLength(_desiredOffset, OrdinaryPreferredRadius - _obstructionCorrection);
        }
        else
        {
            double length = Length(_desiredOffset);
            if (Math.Abs(rightX) > .05000000074505806d && hitWall)
            {
                length -= Rac1CameraRecurrence.ObstructionPullInStep;
                _obstructionReleaseTicks = Rac1CameraRecurrence.ObstructionContactTimerTicks;
            }
            if (length > before) length = before;
            length = Math.Max(length, Rac1CameraRecurrence.ObstructionInnerRadiusFloor);
            _desiredOffset = WithLength(_desiredOffset, length);
            _obstructionCorrection = Math.Max(0d, OrdinaryPreferredRadius - length);
        }
        blocked |= hitWall;

        var guard = Rac1CameraRecurrence.EnforceFinalEffectiveRadiusFloor(
            OrdinaryPreferredRadius, _obstructionCorrection, _obstructionReleaseTicks);
        _obstructionCorrection = guard.RadialCorrection;
        _obstructionReleaseTicks = guard.ReleaseTimerTicks;
        if (!blocked && (_obstructionReleaseTicks is > 0 and < 2000 || rightX < .3d && rightY < .3d))
        {
            var release = Rac1CameraRecurrence.AdvanceClearLineRecovery(_obstructionCorrection, _obstructionReleaseTicks);
            _obstructionCorrection = release.RadialCorrection;
            _obstructionReleaseTicks = release.ReleaseTimerTicks;
        }
        else if (_obstructionReleaseTicks > Rac1CameraRecurrence.ObstructionMinimumRadiusTimerTicks)
            --_obstructionReleaseTicks;
        else
            _obstructionReleaseTicks = Rac1CameraRecurrence.ObstructionMinimumRadiusTimerTicks;
    }

    // 0x002e7618: at most three contact corrections, measured relative to the
    // nearest point on the anchor-to-eye segment rather than an arbitrary yaw nudge.
    private bool TrySlide(IRac1CameraGeometry geometry, Vec3 anchor, ref Vec3 position,
        Rac1CameraContact first, double back, double radius)
    {
        Vec3 start = position, surface = first.Point, push = first.ResolvedCenter;
        Vec3 end = anchor + _desiredOffset;
        Vec3 accumulated = Vec3.Zero;
        bool slid = false;
        for (int i = 0; i < 3; i++)
        {
            Vec3 segment = end - anchor;
            double t = Math.Clamp(Dot(surface - anchor, segment) / Math.Max(Dot(segment, segment), 1e-12), 0d, 1d);
            Vec3 near = anchor + segment * t;
            double distance = Length(surface - near);
            double fraction = Length(near - anchor) / Math.Max(OrdinaryPreferredRadius - _obstructionCorrection, 1e-12);
            double clearance = (CollisionBaseRadius + (CollisionEyeRadius - CollisionBaseRadius) * fraction) * .75d;
            if (distance >= clearance) break;
            slid = true;
            near += WithLength(near - surface, clearance - distance);
            accumulated += anchor + WithLength(near - anchor, back) - start;
            if (i == 2 || geometry.Sphere(push, radius) is not { } next) break;
            surface = next.Point + start - push;
            push = next.ResolvedCenter;
        }
        position = start + accumulated;
        return slid;
    }

    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
