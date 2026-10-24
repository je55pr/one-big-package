using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1MissionProgressionTests
{
    [Theory]
    [InlineData("Veldin-actor-to-Novalis")]
    [InlineData("Infobot-active-pickup-and-admission")]
    [InlineData("Plumber-price-gate-and-Aridia")]
    public void ActorTalkAndAdmissionTransitions_MatchIndependentOriginalMipsExecution(string name)
    {
        using var doc = Witness();
        var trial = doc.RootElement.GetProperty("trials").EnumerateArray().Single(t => t.GetProperty("name").GetString() == name);
        int level = trial.GetProperty("level").GetInt32(), cls = trial.GetProperty("nativeClassId").GetInt32();
        var campaign = new Rac1CampaignState();
        if (level != 0) { campaign.AdmitDestination(level); campaign.BeginTravel(level); }
        var wallet = new Rac1BoltWallet();
        var missions = new Rac1MissionSession(level, campaign, wallet);
        var runtime = new Rac1CampaignRuntimeSession(campaign, Rac1WeaponInventory.CreateOpeningLoadout());
        var actor = Actor(cls);
        if (cls == 750) missions.RegisterInfobot(actor);
        else missions.RegisterTalkActor(actor, Program(level), postOpeningIntro: cls == 834);
        var inputs = trial.GetProperty("inputs").EnumerateArray().Select(s => s.GetString()!).ToArray();
        var samples = trial.GetProperty("samples").EnumerateArray().ToArray();
        int admissionEvents = 0;
        for (int index = 0; index < inputs.Length; ++index)
        {
            string[] row = inputs[index].Split(' ');
            float F(int at) => float.Parse(row[at], CultureInfo.InvariantCulture);
            int I(int at) => int.Parse(row[at], CultureInfo.InvariantCulture);
            if (I(8) >= 0)
            {
                int delta = I(8) - wallet.Balance;
                if (delta > 0) wallet.Credit(delta);
                else if (delta < 0) Assert.True(wallet.TrySpend(-delta));
            }
            if (row[0] == "complete") Assert.True(missions.CompleteScene(missions.PendingScene!));
            else if (row[0] == "entry")
            {
                if (runtime.Travel.TransitionActive)
                {
                    var kind = runtime.CommitLoadedLevel(runtime.Travel.LoaderDestination!.Value);
                    runtime.FinishLoadedLevel(kind);
                }
                else Assert.False(campaign.AdmitDestination(campaign.CurrentLevel));
            }
            else
            {
                var tick = missions.Step(new(new(F(1), F(2), F(3)), F(4), I(5), I(6), I(7) != 0));
                admissionEvents += tick.Admissions.Count;
                if (tick.LevelExit is { } exit) Assert.Equal(Rac1PlanetTravelStartResult.Started, runtime.BeginMissionLevelExit(exit));
            }
            var actual = Assert.Single(missions.Probe());
            var expected = samples[index];
            Assert.True(expected.GetProperty("state").GetInt32() == actual.NativeState, $"{name}/{index}: native actor state");
            Assert.Equal(expected.GetProperty("admittedCount").GetInt32(), campaign.AdmittedDestinationCount);
            Assert.Equal(expected.GetProperty("bolts").GetInt32(), wallet.Balance);
            Assert.Equal(expected.GetProperty("sceneKind").GetInt32(), missions.PendingScene is { } scene ? (int)scene.Kind : -1);
            Assert.Equal(expected.GetProperty("sceneId").GetInt32(), missions.PendingScene?.NativeSceneId ?? -1);
            if (cls == 750) continue;
            Assert.Equal(expected.GetProperty("talkRecord").GetInt32(), actual.TalkRecord);
            Assert.Equal(expected.GetProperty("lastPlayed").GetInt32(), actual.LastPlayedRecord);
        }
        Assert.Equal(cls == 834 ? 0 : 1, admissionEvents);
    }

    [Fact]
    public void InfobotAdmission_IsIdempotentAcrossDuplicateActorsDeathRevisitAndSaveRestore()
    {
        var campaign = new Rac1CampaignState();
        var wallet = new Rac1BoltWallet();
        var session = new Rac1MissionSession(0, campaign, wallet);
        session.RegisterInfobot(Actor(750, index: 10));
        session.RegisterInfobot(Actor(750, index: 11));
        int discoveries = 0;
        for (int tick = 0; tick < 30; ++tick) discoveries += session.Step(new(Vector3.Zero, 0, 4)).Admissions.Count;
        Assert.Equal(1, discoveries);
        Assert.Equal([4], campaign.GalacticMap.Take(campaign.AdmittedDestinationCount));
        Assert.Equal(0, campaign.CurrentLevel);
        Assert.Equal(Rac1LevelVisitState.Visited, campaign.GetLevelState(0));
        Assert.Equal(Rac1LevelVisitState.Unvisited, campaign.GetLevelState(4));
        Assert.True(campaign.BeginTravel(4));
        var saved = Rac1CampaignSavePolicy.Capture(campaign, Rac1WeaponInventory.CreateOpeningLoadout());
        var restored = Rac1CampaignSavePolicy.RestoreOrDefault(saved).Campaign;
        Assert.True(restored.CanSelectDestination(4));
        Assert.Equal(1, restored.AdmittedDestinationCount);
        Assert.False(restored.AdmitDestination(4));
        var revisit = new Rac1MissionSession(0, restored, wallet);
        revisit.RegisterInfobot(Actor(750));
        Assert.Empty(revisit.Step(new(Vector3.Zero, 0, 0)).Admissions);
        Assert.Empty(revisit.Step(new(Vector3.Zero, 0, 4)).Admissions);
        Assert.True(Assert.Single(revisit.Probe()).Retired);
        Assert.Equal([4], restored.GalacticMap.Take(restored.AdmittedDestinationCount));
    }

    [Fact]
    public void DormantAndActorPathLinkedInfobots_CannotDiscoverThroughInventedProximity()
    {
        var campaign = new Rac1CampaignState();
        var session = new Rac1MissionSession(0, campaign, new());
        var dormant = Actor(750, 1);
        BinaryPrimitives.WriteInt32LittleEndian(dormant.PVar!, 0);
        session.RegisterInfobot(dormant);
        var linked = Actor(750, 2);
        BinaryPrimitives.WriteInt32LittleEndian(linked.PVar!.AsSpan(0x50), 57);
        session.RegisterInfobot(linked);
        for (int i = 0; i < 60; ++i) Assert.Empty(session.Step(new(Vector3.Zero, 0, 4)).Admissions);
        Assert.Equal(0, campaign.AdmittedDestinationCount);
        Assert.Contains(session.Probe(), p => p.NativeState == 1 && !p.Visible);
        Assert.Contains(session.Probe(), p => p.NativeState == 5 && p.UnsupportedMotion);
    }

    [Fact]
    public void SceneCompletion_RejectsDuplicateForeignAndRevisitedRequestsWithoutChargingTwice()
    {
        var wallet = new Rac1BoltWallet(500);
        var key = new Rac1MissionActorKey(1, 774, 901);
        var first = new Rac1TalkSession(key, Program(1), wallet, talkedBefore: true);
        var facts = new Rac1MissionPlayerFacts(new(4, 0, 0), Math.PI, 4, InteractPressed: true);
        Assert.True(first.TryStart(facts, Vector3.Zero, 0, 5));
        Assert.Equal(0, wallet.Balance);
        var request = first.PendingScene!;
        Assert.False(first.TryStart(facts, Vector3.Zero, 0, 5));
        Assert.False(first.CompleteScene(request with { }));
        Assert.True(first.CompleteScene(request));
        Assert.False(first.CompleteScene(request));
        Assert.Equal(1, first.PendingScene!.Serial - request.Serial);
        var revisit = new Rac1TalkSession(key, Program(1), new(500), talkedBefore: true);
        Assert.True(revisit.TryStart(facts, Vector3.Zero, 0, 5));
        Assert.False(revisit.CompleteScene(request));
        Assert.Equal(2, revisit.RecordIndex);
    }

    [Fact]
    public void MissionLevelExit_WaitsForLoaderCommitAndDoesNotFabricateMapAdmissionEarly()
    {
        var campaign = new Rac1CampaignState();
        var runtime = new Rac1CampaignRuntimeSession(campaign, Rac1WeaponInventory.CreateOpeningLoadout());
        Assert.Equal(Rac1PlanetTravelStartResult.DestinationUnavailable, runtime.BeginTravel(1));
        Assert.Equal(Rac1PlanetTravelStartResult.Started,
            runtime.BeginMissionLevelExit(new(new(0, 834, 165), 1)));
        Assert.Equal(0, campaign.CurrentLevel);
        Assert.False(campaign.CanSelectDestination(1));
        Assert.Equal(1, runtime.Travel.LoaderDestination);
        var kind = runtime.CommitLoadedLevel(1);
        Assert.Equal(1, campaign.CurrentLevel);
        Assert.True(campaign.CanSelectDestination(1));
        Assert.Equal(Rac1LevelVisitState.Completed, campaign.GetLevelState(0));
        Assert.Equal([1], campaign.GalacticMap.Take(campaign.AdmittedDestinationCount));
        runtime.FinishLoadedLevel(kind);
        Assert.False(runtime.Travel.TransitionActive);
        Assert.False(campaign.AdmitDestination(1));
    }

    [Fact]
    public void FailedMissionLoad_CanBeAbandonedWithoutDiscoveryOrCompletion()
    {
        var campaign = new Rac1CampaignState();
        var runtime = new Rac1CampaignRuntimeSession(campaign, Rac1WeaponInventory.CreateOpeningLoadout());
        runtime.BeginMissionLevelExit(new(new(0, 834, 165), 1));
        Assert.Throws<InvalidOperationException>(() => runtime.CommitLoadedLevel(2));
        runtime.AbandonUncommittedHostLoad();
        Assert.Equal(0, campaign.CurrentLevel);
        Assert.Equal(0, campaign.AdmittedDestinationCount);
        Assert.Equal(Rac1LevelVisitState.Visited, campaign.GetLevelState(0));
        Assert.False(runtime.Travel.TransitionActive);
    }

    [Fact]
    public void MissionExitToCurrentOpeningLevel_ReloadsWithoutInventingDestinationZeroAdmission()
    {
        var runtime = new Rac1CampaignRuntimeSession(new(), Rac1WeaponInventory.CreateOpeningLoadout());
        Assert.Equal(Rac1PlanetTravelStartResult.Started, runtime.BeginMissionLevelExit(new(new(0, 834, 165), 0)));
        var entry = runtime.CommitLoadedLevel(0);
        runtime.FinishLoadedLevel(entry);
        Assert.Equal(0, runtime.Campaign.CurrentLevel);
        Assert.Equal(0, runtime.Campaign.AdmittedDestinationCount);
        Assert.Equal(Rac1LevelVisitState.Completed, runtime.Campaign.GetLevelState(0));
    }

    [Theory]
    [InlineData(7, 8, false)]
    [InlineData(7, 8, true)]
    [InlineData(14, 15, false)]
    [InlineData(14, 15, true)]
    public void MissionExitCompletion_RespectsIndependentNativeLevelGates(int source, int gate, bool admitted)
    {
        var campaign = new Rac1CampaignState();
        campaign.AdmitDestination(source);
        campaign.BeginTravel(source);
        if (admitted) campaign.AdmitDestination(gate);
        var runtime = new Rac1CampaignRuntimeSession(campaign, Rac1WeaponInventory.CreateOpeningLoadout());
        runtime.BeginMissionLevelExit(new(new(source, 750, 0), 18));
        runtime.CommitLoadedLevel(18);
        Assert.Equal(admitted ? Rac1LevelVisitState.Completed : Rac1LevelVisitState.Visited, campaign.GetLevelState(source));
        Assert.True(campaign.CanSelectDestination(18));
        Assert.Equal(18, campaign.CurrentLevel);
    }

    internal static Rac1TalkProgram Program(int level)
    {
        using var doc = Witness();
        var p = doc.RootElement.GetProperty("programs").EnumerateArray().Single(p => p.GetProperty("level").GetInt32() == level);
        var records = p.GetProperty("records").EnumerateArray().Select(r => new Rac1TalkRecord(
            r.GetProperty("index").GetInt32(), r.GetProperty("textId").GetInt32(), r.GetProperty("scene").GetInt16(),
            r.GetProperty("next").GetInt16(), r.GetProperty("condition").GetInt16(), r.GetProperty("argument").GetInt16(),
            r.GetProperty("whenTrue").GetInt16(), r.GetProperty("whenFalse").GetInt16(), r.GetProperty("flags").GetUInt16())).ToDictionary(r => r.Index);
        var prices = p.GetProperty("prices").EnumerateObject().ToDictionary(v => int.Parse(v.Name, CultureInfo.InvariantCulture), v => v.Value.GetInt32());
        return new(level, p.GetProperty("globalTalkerIndex").GetInt32(), records, prices);
    }

    private static Rac1Instances.MobyInstance Actor(int cls, int index = 0)
    {
        var pvar = new byte[cls == 750 ? 0xd0 : cls == 834 ? 160 : 416];
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0xc), 5);
        if (cls == 750)
        {
            void I(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(at), value);
            I(0, 1); I(4, 4); I(8, -1); pvar[12] = pvar[13] = pvar[14] = 0xff;
            I(0x50, -1); I(0x54, -1); I(0x58, -1);
            for (int i = 0; i < 8; ++i) I(0x70 + i * 4, -1);
        }
        return new(index, cls, 1, (0, 0, 0), (0, 0, 0), null, 0, new byte[0x78], pvar);
    }

    internal static JsonDocument Witness() => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "research/generated/rac1-mission-progression.json")));
}
