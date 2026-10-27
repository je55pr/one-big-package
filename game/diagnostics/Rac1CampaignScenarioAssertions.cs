using OBP.Core;
using OBP.RAC1.Progression;

namespace OBP.Game.Diagnostics;

public static class Rac1CampaignScenarioAssertions
{
    public static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static void IsolatedSave(IRac1CampaignDiagnosticSave save)
    {
        var provenance = save.ReadProvenance();
        Require(provenance.ExplicitPath && !provenance.SharedProfileActive && provenance.Source == ObpSourceGame.Rac1,
            "Campaign smoke requires --rac1-campaign-save so it cannot modify the normal host save.");
    }
    public static void Persisted(IRac1CampaignDiagnosticSave save, int current, int[] admissions)
    {
        var restored = save.ReadPersisted();
        Require(restored.Kind == Rac1CampaignRestoreKind.RestoredCurrentSchema, $"Persisted restore kind was {restored.Kind}.");
        Require(restored.CurrentLevel == current, $"Persisted CurrentLevel {restored.CurrentLevel}, expected {current}.");
        Require(restored.Admissions.SequenceEqual(admissions),
            $"Persisted unlock order [{string.Join(',', restored.Admissions)}], expected [{string.Join(',', admissions)}].");
    }
    public static void FreshCheckpoint(IRac1CampaignCheckpointFixture fixture, int level)
    {
        var checkpoint = fixture.Read();
        Require(checkpoint.WorldAuthoredClass0 is not null, "R&C1 smoke world has no authored class-0 start.");
        Require(checkpoint.NativeLevel == level, $"Checkpoint session level {checkpoint.NativeLevel}, expected {level}.");
        Require(!checkpoint.Active, $"Level {level} unexpectedly carried an active checkpoint across full entry.");
        Require(checkpoint.AuthoredClass0 == checkpoint.WorldAuthoredClass0, $"Level {level} checkpoint baseline does not match RuntimeWorld.PlayerStart.");
        Require(checkpoint.RestartKind == Rac1RestartPlacementKind.AuthoredClass0,
            $"Level {level} fresh checkpoint session did not resolve to authored class 0.");
    }
}
