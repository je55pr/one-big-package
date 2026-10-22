using OBP.Core.Math;

namespace OBP.RAC1.Camera;

/// <summary>Native camera contact: surface point and a sphere centre resolved clear of it.</summary>
public readonly record struct Rac1CameraContact(Vec3 Point, Vec3 ResolvedCenter);

/// <summary>
/// Geometry boundary for the camera's segment/sphere queries. The source owns
/// probe radii, order, slide, pull-in and release; the host supplies intersections.
/// </summary>
public interface IRac1CameraGeometry
{
    Rac1CameraContact? Sphere(Vec3 centerNativeZUp, double radius);
    Vec3? Segment(Vec3 fromNativeZUp, Vec3 toNativeZUp);
}
