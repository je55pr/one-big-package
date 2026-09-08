using System.Numerics;
using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2.Animation;
using OBP.RAC2.Geometry;
using OBP.RAC2.Level;

namespace OBP.Tests;

/// <summary>
/// Retail-backed validation of Going Commando <see cref="GcMoby.MobySequence"/>
/// decode + engine-independent <see cref="MobyAnimation"/> pose evaluation.
/// Retail-gated on <c>OBP_GC_ISO</c>. See <c>research/GC_MOBY.md</c>.
/// </summary>
public class MobyAnimationTests
{
    private static Dictionary<int, GcMoby.MobyClass> Classes(int level)
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile($"/G/LEVEL{level}.WAD")!;
        var header = GcLevelWad.ReadHeader(wad);
        return GcMoby.ReadClasses(GcLevelCore.Open(GcLevelWad.RequireLump(wad, header, 0)));
    }

    [SkippableFact]
    public void Oc1134_SequenceStructureMatchesRetail()
    {
        var cl = Classes(1)[1134];

        Assert.Single(cl.Joints);
        Assert.Equal(0, cl.Joints[0].Parent);
        Assert.Equal(2, cl.Sequences.Count);
        Assert.Single(cl.Sequences[0].Frames);
        Assert.Equal(170, cl.Sequences[1].Frames.Count);

        // Every stored joint rotation is a unit quaternion (s16/32768, 4 per joint).
        foreach (var seq in cl.Sequences)
        {
            foreach (var frame in seq.Frames)
            {
                Assert.Equal(cl.Joints.Count, frame.JointRotations.Length);
                foreach (var q in frame.JointRotations)
                {
                    float len = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
                    Assert.InRange(len, 0.999f, 1.001f);
                }
            }
        }
    }

    [SkippableFact]
    public void IdentityFrameReproducesTheRestPose()
    {
        // For every single-joint animated class in Oozla, an all-identity frame
        // must return the decoder's baked rest pose (the skinning invariant).
        var classes = Classes(1);
        int checkedClasses = 0;
        foreach (var cl in classes.Values)
        {
            if (cl.Joints.Count != 1 || cl.Sequences.All(s => s.Frames.Count < 2) || cl.Mesh.Positions.Length == 0)
            {
                continue;
            }

            var identity = new GcMoby.MobyFrame(0f, [Quaternion.Identity]);
            var posed = MobyAnimation.Pose(cl.Mesh, cl.Joints, identity);

            Assert.Equal(cl.Mesh.Positions.Length, posed.Length);
            double maxDev = 0;
            for (int i = 0; i < posed.Length; i++)
            {
                maxDev = Math.Max(maxDev, Math.Abs(posed[i] - cl.Mesh.Positions[i]));
            }

            Assert.True(maxDev < 1e-3, $"oClass {cl.OClass} identity-frame deviation {maxDev:E3}");
            checkedClasses++;
        }

        Assert.True(checkedClasses > 0, "expected at least one single-joint animated class in Oozla");
    }

    [SkippableFact]
    public void PosedFramesAreFiniteBoundedAndDeterministic()
    {
        var cl = Classes(1)[1134];
        var seq = cl.Sequences[1];

        double restExtent = Extent(cl.Mesh.Positions);

        for (int f = 0; f < seq.Frames.Count; f++)
        {
            var a = MobyAnimation.Pose(cl.Mesh, cl.Joints, seq.Frames[f]);
            var b = MobyAnimation.Pose(cl.Mesh, cl.Joints, seq.Frames[f]);
            Assert.Equal(a, b); // deterministic

            foreach (double c in a)
            {
                Assert.True(double.IsFinite(c));
            }

            // A rigid rotation about the joint can't grow the mesh's extent.
            Assert.True(Extent(a) < restExtent * 1.5 + 1e-3, $"frame {f} extent {Extent(a):F3} vs rest {restExtent:F3}");
        }
    }

    [SkippableFact]
    public void SkinMatricesAreRigid()
    {
        // Each per-joint skin matrix is a rotation + translation (no scale/shear):
        // its linear part must be orthonormal.
        var cl = Classes(1)[1134];
        var mid = cl.Sequences[1].Frames[85];
        var skin = MobyAnimation.SkinMatrices(cl.Joints, mid, cl.Mesh.Scale / 1024f);

        foreach (var m in skin)
        {
            var x = new Vector3(m.M11, m.M12, m.M13);
            var y = new Vector3(m.M21, m.M22, m.M23);
            var z = new Vector3(m.M31, m.M32, m.M33);
            Assert.InRange(x.Length(), 0.999f, 1.001f);
            Assert.InRange(y.Length(), 0.999f, 1.001f);
            Assert.InRange(z.Length(), 0.999f, 1.001f);
            Assert.InRange(Vector3.Dot(x, y), -1e-3f, 1e-3f);
            Assert.InRange(Vector3.Dot(x, z), -1e-3f, 1e-3f);
        }
    }

    private static double Extent(double[] p)
    {
        if (p.Length == 0)
        {
            return 0;
        }

        double mn = double.PositiveInfinity, mx = double.NegativeInfinity;
        foreach (double c in p)
        {
            mn = Math.Min(mn, c);
            mx = Math.Max(mx, c);
        }

        return mx - mn;
    }
}
