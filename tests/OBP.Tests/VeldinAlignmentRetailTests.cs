using OBP.Composition;
using OBP.Core.Math;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC3.Level;

namespace OBP.Tests;

/// <summary>Cross-game retail evidence for the R&C1/UYA Veldin composition experiment.</summary>
public sealed class VeldinAlignmentRetailTests
{
    [SkippableFact]
    public void CandidateCorrespondencesDoNotEstablishSingleRigidFit()
    {
        string? rac1Iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        string? uyaIso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(rac1Iso), "OBP_RAC1_ISO not set");
        Skip.If(string.IsNullOrEmpty(uyaIso), "OBP_UYA_ISO not set");

        using var rac1Reader = new FileRandomAccessReader(rac1Iso!);
        var rac1Row = Rac1DiscIndex.Read(rac1Reader).Levels.Single(x => x.LevelId == 0);
        var rac1 = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(rac1Reader, rac1Row));

        using var uyaReader = new FileRandomAccessReader(uyaIso!);
        var uya = UyaGameplay.Read(UyaLevelCore.Open(uyaReader, 1).GameplayReader);

        Vec3 ratchet1 = Moby(Assert.Single(rac1.MobyInstances, x => x.OClass == 0).Position);
        Vec3 ratchet3 = Moby(Assert.Single(uya.MobyInstances, x => x.OClass == 0).Position);

        Vec3 s14 = Static(Assert.Single(rac1.ShrubInstances, x => x.OClass == 467 && x.Index == 14).Matrix);
        Vec3 t254 = Static(Assert.Single(uya.ShrubInstances, x => x.OClass == 3364 && x.Index == 254).Matrix);
        Vec3 s17 = Static(Assert.Single(rac1.ShrubInstances, x => x.OClass == 467 && x.Index == 17).Matrix);
        Vec3 t253 = Static(Assert.Single(uya.ShrubInstances, x => x.OClass == 3364 && x.Index == 253).Matrix);
        Vec3 s13 = Static(Assert.Single(rac1.ShrubInstances, x => x.OClass == 467 && x.Index == 13).Matrix);
        Vec3 t245 = Static(Assert.Single(uya.ShrubInstances, x => x.OClass == 3364 && x.Index == 245).Matrix);

        // These are exploratory candidate correspondences, not admitted semantic landmarks.
        var local = PlanarAlignmentSolver.Solve(new[] { (ratchet3, ratchet1), (t254, s14) });
        Assert.Equal(-8.0577, local.RotationYDegrees, 3);
        Assert.Equal(0.0145, local.RmsError, 3);
        Assert.InRange(Residual(local.Transform, t253, s17), 1.69, 1.71);

        var candidates = new[] { (ratchet3, ratchet1), (t254, s14), (t253, s17) };
        var rigid = PlanarAlignmentSolver.Solve(candidates);
        Assert.Equal(AlignmentQuality.Solved, rigid.Quality);
        Assert.Equal(-18.1084, rigid.RotationYDegrees, 3);
        Assert.Equal(0.6403, rigid.RmsError, 3);
        Assert.InRange(Residual(rigid.Transform, t245, s13), 5.38, 5.39);

        var scaled = PlanarAlignmentSolver.Solve(candidates, allowScale: true);
        Assert.Equal(0.975826, scaled.FittedScale, 5);
        Assert.InRange(Residual(scaled.Transform, t245, s13), 5.14, 5.15);
    }

    private static Vec3 Moby((float X, float Y, float Z) p) => new(p.X, p.Z, p.Y);

    private static Vec3 Static(float[] matrix)
    {
        Assert.Equal(16, matrix.Length);
        return new Vec3(matrix[12], matrix[14], matrix[13]);
    }

    private static double Residual(CompositionTransform transform, Vec3 target, Vec3 source)
    {
        Vec3 predicted = transform.Apply(source);
        Vec3 delta = predicted - target;
        return Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
    }
}
