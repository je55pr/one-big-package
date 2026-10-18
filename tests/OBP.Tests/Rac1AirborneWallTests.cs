using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1AirborneWallTests
{
    private static Rac1AirborneWallProbeInput Input => new(
        new(0, 0, 10), new(0.01, 0, 0), 0.1, 0, 2, 29.7f * (1f / 3600f),
        7, 4, 2, 2, false, false, false, false, 0);

    private static Rac1TraversalRayHit Hit(Rac1TraversalRay ray, double distance = 0.7, int material = 0) =>
        new(new(ray.From.X + Math.Min(distance, ray.To.X - ray.From.X), ray.From.Y, ray.From.Z),
            new(-1, 0, 0), material);

    [Fact]
    public void CandidateUsesBothSourceRaysAndPredictedLedgeVetoBeforeRefreshingSixUpdateLease()
    {
        var rays = new List<Rac1TraversalRay>();
        Rac1NativeVector3 prediction = default;
        var found = Rac1AirborneWallProbe.Probe(Input, ray =>
        {
            rays.Add(ray);
            return Hit(ray);
        }, value => { prediction = value; return false; });
        Assert.Equal(Rac1AirborneWallProbeOutcome.Refreshed, found.Outcome);
        Assert.Equal(6, found.CandidateLeaseUpdates);
        Assert.Equal(Math.PI, found.NormalYaw);
        Assert.Equal(2, rays.Count);
        Assert.Equal(0.45f - 0.02f, rays[0].From.X, 7);
        Assert.Equal(1, rays[0].To.X);
        Assert.Equal(10 + (double)0.3f, rays[0].From.Z);
        Assert.Equal(13, rays[1].From.Z);
        Assert.Equal((double)0.45f + 1.4f, rays[1].To.X);
        Assert.All(rays, ray => Assert.Equal(Rac1TraversalRayMask.Support, ray.Mask));
        Assert.Equal(0.2, prediction.X);
        Assert.Equal(12 - Input.GravityPerUpdate * 200, prediction.Z);

        var veto = Rac1AirborneWallProbe.Probe(Input, _ => throw new InvalidOperationException(), _ => true);
        Assert.Equal(Rac1AirborneWallProbeOutcome.ClearedForLedge, veto.Outcome);
        Assert.Equal(22, veto.LedgeCooldownUpdates);
    }

    [Fact]
    public void WallMaterialDistanceSlopeFacingAndDomainGatesEachRejectIndependently()
    {
        static Rac1AirborneWallProbeResult Probe(Rac1AirborneWallProbeInput input,
            Func<Rac1TraversalRay, Rac1TraversalRayHit?> raycast) =>
            Rac1AirborneWallProbe.Probe(input, raycast, _ => false);
        foreach (int material in new[] { 10, 12 })
            Assert.Equal(Rac1AirborneWallProbeOutcome.Unchanged,
                Probe(Input, ray => Hit(ray, material: material)).Outcome);
        Assert.Equal(Rac1AirborneWallProbeOutcome.Unchanged,
            Probe(Input, ray => Hit(ray, 0.73)).Outcome);
        Assert.Equal(Rac1AirborneWallProbeOutcome.Unchanged,
            Probe(Input, ray => Hit(ray) with { Normal = new(-0.5, 0, 0.866) }).Outcome);
        Assert.Equal(Rac1AirborneWallProbeOutcome.Unchanged,
            Probe(Input, ray => Hit(ray) with { Normal = new(0, 1, 0) }).Outcome);
        Assert.Equal(Rac1AirborneWallProbeOutcome.Unchanged,
            Probe(Input with { CurrentDomain = 2 }, ray => Hit(ray)).Outcome);
        Assert.Equal(Rac1AirborneWallProbeOutcome.Refreshed,
            Probe(Input with { CurrentDomain = 2, SecondPreviousDomain = 4 }, ray => Hit(ray)).Outcome);
        Assert.Equal(Rac1AirborneWallProbeOutcome.Unchanged,
            Probe(Input with { GroundClearance = 0.39 }, _ => throw new InvalidOperationException()).Outcome);
        Assert.Equal(Rac1AirborneWallProbeOutcome.Unchanged,
            Probe(Input with { RequestedVertical = -0.34 }, _ => throw new InvalidOperationException()).Outcome);
        Assert.Equal(Rac1AirborneWallProbeOutcome.Refreshed,
            Probe(Input with
            {
                GroundClearance = Rac1AirborneWallProbe.MinimumGroundClearance,
                RequestedVertical = Rac1AirborneWallProbe.MinimumRequestedVertical
            }, ray => Hit(ray)).Outcome);
        Assert.Equal(Rac1AirborneWallProbeOutcome.Unchanged, Probe(Input, _ => null).Outcome);
    }

    [Fact]
    public void RepeatedWallJumpsRequireOpposedNormalsAndAllowLargerReachAfterTwentyFiveUpdates()
    {
        var input = Input with { CurrentAction = 0x11, HasWallJumped = true, PreviousWallNormalYaw = 0 };
        var rays = new List<Rac1TraversalRay>();
        var result = Rac1AirborneWallProbe.Probe(input, ray =>
        {
            rays.Add(ray);
            return Hit(ray, 0.94);
        }, _ => false);
        Assert.Equal(Rac1AirborneWallProbeOutcome.Refreshed, result.Outcome);
        Assert.Equal(7, result.CandidateLeaseUpdates);
        Assert.Equal(10 + (double)1.7f, rays[1].From.Z);
        Assert.Equal(Rac1AirborneWallProbeOutcome.Unchanged,
            Rac1AirborneWallProbe.Probe(input with { PreviousWallNormalYaw = Math.PI },
                ray => Hit(ray), _ => false).Outcome);

        var admission = new Rac1AirborneWallProbe.AdmissionInput(0x11, 26, 7, false, true, 0);
        Assert.True(Rac1AirborneWallProbe.CanEnter(admission));
        Assert.False(Rac1AirborneWallProbe.CanEnter(admission with { NativeActionAge = 25 }));
        Assert.False(Rac1AirborneWallProbe.CanEnter(admission with { CurrentAction = 8 }));
        Assert.False(Rac1AirborneWallProbe.CanEnter(admission with { LandingStarted = true }));
        Assert.False(Rac1AirborneWallProbe.CanEnter(admission with { CandidateLeaseUpdates = 0 }));
        Assert.False(Rac1AirborneWallProbe.CanEnter(admission with { CurrentAction = 7, NativeActionAge = 5, BufferedCrossPressAge = 5 }));
    }

    [Fact]
    public void WallJumpRetainsAnticipationMotionThenUsesOutwardNormalAndRecoveredGravity()
    {
        var inherited = new Rac1AirbornePlanarMotion.PlanarStep(0.1f, 0.02f);
        Assert.Equal(inherited, Rac1AirborneWallJump.Step(8, 0, inherited));
        Assert.Equal(new Rac1AirbornePlanarMotion.PlanarStep(Rac1AirborneWallJump.OutwardStep, 0),
            Rac1AirborneWallJump.Step(9, 0, inherited));
        var profile = Rac1AirborneJumpProfile.Create(Rac1AirborneJumpKind.WallJump);
        Assert.Equal(29f * (1f / 3600f), profile.GravityPerUpdate);
        Assert.Equal(35, profile.EntrySequence);
        Assert.Equal(7, profile.EntryBlendTicks);
        var session = new Rac1AirborneMotionSession(profile, 0);
        Assert.False(session.Step(new(8, true, 0)).HasLaunched);
        Assert.True(session.Step(new(9, true, 0)).HasLaunched);
    }
}
