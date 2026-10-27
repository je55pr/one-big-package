using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OBP.Core;
using OBP.RAC1;

namespace OBP.Game.Diagnostics;

public sealed class TrilogyGameplayScenario(CaptureDiagnosticContext context, ITrilogyPlayerDiagnosticFixture player,
    ITrilogyActorDiagnosticFixture actors, IDiagnosticNavigationActions navigation, ICaptureEquipmentActions equipment) : CaptureScenarioSupport(context)
{
    public async Task RunAsync()
    {
        Require(player.Read() is { Ordinary: true, NativePresent: true }, "ordinary selected player required");
        var initial = player.CaptureSession(); ITrilogyPoseWitness? canonical = null; string[]? audioBaseline = null;
        try
        {
            foreach (string destination in new[] { "rac1:LEVEL0", "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab" })
            {
                initial.Restore(); Require((await navigation.NavigateAsync(destination, Lifetime)).Accepted, "entry failed: " + destination);
                using var visit = player.BeginVisit(); player.StageDeck(); await Physics(2);
                player.StageEncounterDeck();
                await Physics(40); Require(player.Read() is { AssetsReady: true, AudioReady: true }, "selected source bank missing in " + destination);
                canonical ??= player.CapturePose(); canonical.Restore(); player.ResetAudio(); await player.CaptureTrialAsync(Lifetime);
                var audio = player.ReadAudio(); var cues = audio.Select(cue => $"{cue.Source}/{cue.Build}/{cue.Level}/{cue.Class}/{cue.Sound}/{cue.LevelSound}/{cue.Loop}@{cue.Tick}").ToArray();
                Require(audio.Any(cue => cue.Class == 0) && cues.Length > 0, "movement/jump/wrench audio is silent in " + destination);
                Require(player.Read().WrenchHeld, "wrench attack did not retain hand ownership in " + destination);
                if (audioBaseline is null) audioBaseline = cues;
                else Require(audioBaseline.SequenceEqual(cues), "same physics-clock player actions changed audio intents in " + destination);
                await VerifyPauseAsync(); await VerifyWeaponsAsync(destination);
                audio = player.ReadAudio(); Require(audio.Length > 0, "selected Ratchet/equipment went silent in " + destination);
                Require(audio.All(cue => cue.Source == "rac1" && cue.Build == Rac1Authority.Primary.BuildId && cue.Level == 0), "player voice/surface bank followed destination");
                Log($"[trilogy-gameplay-smoke] {destination}: universal menu, held models, native firing/ammo and {cues.Length} matching player audio intents PASS");
            }
            await VerifyMixedActorsAsync();
            await VerifyCacheAndInvalidationAsync();
        }
        finally { player.SetPaused(false); player.HoldEquipped(false); }
    }
    private async Task VerifyPauseAsync()
    {
        Require(player.OpenPause() && player.Read() is { UniversalPause: true, RetailPause: false }, "ordinary pause is not universal");
        var before = player.Read(); player.ShowPauseSection("Quick Select");
        Require(equipment.AssignQuickSelect(7, "rac3/item/43"), "mixed slot assignment failed");
        player.ShowPauseSection("Gadgets"); player.ShowPauseSection("Appearance"); await Frames(); var after = player.Read();
        Require(after.PhysicsTick == before.PhysicsTick && after.Shots == before.Shots, "pause advanced weapon/player clock"); player.ClosePause();
    }
    private async Task VerifyWeaponsAsync(string destination)
    {
        foreach (string id in new[] { "rac1/item/10", "rac1/item/16", "rac2/item/30", "rac3/item/43" })
        {
            var entry = player.ReadEquipment().Single(entry => entry.Id == id);
            Require(entry.Selectable && equipment.Select(id), "weapon unavailable in " + destination + ": " + entry.Reason);
            int ammo = player.ReadEquipment().Single(entry => entry.Id == id).Ammo;
            player.HoldEquipped(true); await Physics(id == "rac3/item/43" ? 90 : 18);
            if (id == "rac1/item/16") Require(player.Read() is { PyroEmitting: true, LiveType12: true, RenderedParticles: > 0 }, "Pyrocitor has no renderable flame in " + destination);
            player.HoldEquipped(false); await Physics(65);
            Require(player.ReadEquipment().Single(entry => entry.Id == id).Ammo < ammo, "weapon did not consume native ammo in " + destination + ": " + id);
            if (id == "rac1/item/16") Require(player.Read() is { PyroEmitting: false, LiveType12: false }, "Pyrocitor flame survived release in " + destination);
        }
    }
    private async Task PrepareActorsAsync()
    {
        ulong deadline = Context.Time.Milliseconds + 180_000;
        while (actors.Read().Placed + actors.Read().Problems.Length < actors.Read().Expected && Context.Time.Milliseconds < deadline)
            await actors.PumpAsync(Lifetime);
    }
    private async Task VerifyMixedActorsAsync()
    {
        player.SetPaused(true); await PrepareActorsAsync(); var population = actors.Read();
        Require(population.Problems.Length == 0, "mixed actor preparation: " + string.Join("; ", population.Problems));
        Require(population.Placed == 3 && population.Registered == 3, "three native source actors did not coexist");
        Require(await actors.RejectUnknownContextsAsync() && actors.Read() is { Registered: 3, Bodies: 3 }, "unrecovered native context was admitted or changed actor registrations");
        foreach (var actor in actors.ReadActors()) Require(actors.Reset(actor.Id), "initial encounter reset failed");
        actors.ResetEncounter(); player.SetPaused(false); player.StageEncounter();
        var starts = actors.ReadActors().ToDictionary(actor => actor.Key.Target, actor => actor.Facts.Position);
        double healthBeforeContact = player.Read().Health; var trace = new List<string>();
        for (int sample = 0; sample < 6; sample++)
        {
            await Physics(30);
            foreach (var actor in actors.ReadActors().OrderBy(actor => actor.Id, StringComparer.Ordinal))
            {
                var facts = actor.Facts;
                trace.Add(FormattableString.Invariant($"{sample}/{actor.Id}/{actor.Key.Target.InstanceIndex}/{facts.Position.X:R}/{facts.Position.Y:R}/{facts.Position.Z:R}/{facts.NativeState}/{facts.NativeSequence}/{facts.Health:R}/{facts.Alive}"));
            }
        }
        Log("[trilogy-gameplay-smoke] native encounter trace " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", trace)))));
        foreach (var actor in actors.ReadActors().Where(actor => actor.Key.Target.SourceGame != ObpSourceGame.Rac3))
            Require(System.Numerics.Vector3.Distance(starts[actor.Key.Target], actor.Facts.Position) > .1f, "hostile has no live native motion: " + actor.Key.Target.SourceGame);
        Require(player.Read().Health < healthBeforeContact, $"native actors did not damage the shared player: before={healthBeforeContact}, after={player.Read().Health}, player={player.Read().Position}; " + string.Join("; ", actors.ReadActors()));
        Require(actors.Read().Rac2AnimationPresented, "GC native animation was not presented");
        await FireAtActorAsync("arena-rac1-toad", "rac3/item/43", 100);
        Require(!Actor("arena-rac1-toad").Facts.Alive, "ordinary charged Cannon did not defeat the native toad");
        Require(actors.Reset("arena-gc-msr1"), "GC combat reset failed");
        await FireAtActorAsync("arena-gc-msr1", "rac2/item/30", 160);
        Require(!Actor("arena-gc-msr1").Facts.Alive, "ordinary Lancer contacts did not exhaust native GC health");
        await FireAtActorAsync("arena-uya-target", "rac1/item/10", 18);
        Require(!Actor("arena-uya-target").Facts.Alive, "ordinary Bomb Glove did not deactivate the UYA target");
        foreach (string id in actors.ReadActors().Select(actor => actor.Id).ToArray())
        {
            var stale = Actor(id).Key; var others = actors.ReadActors().Where(actor => actor.Id != id).ToDictionary(actor => actor.Id, actor => actor.Key);
            Require(actors.Reset(id), "independent actor reset failed: " + id);
            Require(Actor(id).Key != stale && !actors.HasRuntimeActor(stale), "reset reused an old actor identity");
            Require(!actors.HasRewardOrAudio(stale), "reset retained native reward/audio ownership");
            Require(others.All(pair => Actor(pair.Key).Key == pair.Value), "reset changed a different actor");
            await Physics(2); Require(actors.Read().Bodies == 3, "actor reset leaked a collision body");
        }
        Log("[trilogy-gameplay-smoke] native motion/animation, shared player damage, ordinary Lancer/Cannon/Bomb contacts and independent resets PASS");
    }
    private TrilogyActorDiagnostic Actor(string id) => actors.ReadActors().Single(actor => actor.Id == id);
    private async Task FireAtActorAsync(string id, string weapon, int ticks)
    {
        player.StageAttack(Actor(id).Facts.Position); Require(equipment.Select(weapon), "combat weapon could not be selected: " + weapon);
        player.HoldEquipped(true); await Physics(ticks); player.HoldEquipped(false); await Physics(100);
        var target = Actor(id); var state = player.Read();
        Log($"[trilogy-gameplay-smoke] ordinary {weapon} -> {id}: alive={target.Facts.Alive}, health={target.Facts.Health}, player={state.Health}, yaw={state.Yaw}, hits={state.AcceptedHits}");
    }
    private async Task VerifyCacheAndInvalidationAsync()
    {
        var cache = actors.CaptureCache(); Require(cache.Reused, "current source version was not reused");
        Require(await actors.VerifyConstructionRollbackAsync(), "failed factory leaked native registrations, scene or physics");
        Log("[trilogy-gameplay-smoke] partial actor construction rollback PASS");
        player.SetPaused(true);
        using (var invalidated = actors.Invalidate())
        {
            Require(actors.Read() is { Registered: 0, Bodies: 0, Placed: 0, Libraries: 0, Assets: 0 } && invalidated.PendingRejected && invalidated.Retired.DamageTargetsRemoved,
                "source invalidation retained live actors or player attack registrations");
            actors.Readmit(); await PrepareActorsAsync();
            Require(actors.Read() is { Placed: 3, Bodies: 3 } current && current.Problems.Length == 0 && invalidated.Retired.ReadmittedWithNewIdentities,
                "source re-admission failed or revived retired target identities");
        }
        cache = actors.CaptureCache(); player.SetPaused(false); Log("[trilogy-gameplay-smoke] live source invalidation and re-admission PASS");
        var retired = actors.CaptureRetirement(); int ammo = player.ReadEquipment().Single(item => item.Id == "rac2/item/30").Ammo;
        Require((await navigation.NavigateAsync("rac2:LEVEL1", Lifetime)).Accepted, "cleanup destination entry failed"); await Physics(2);
        Require(retired.OldActorsRetired && retired.OldRootsFreed && actors.Read().Bodies == 0, "world transition retained actors or their physics");
        Require(player.ReadEquipment().Single(item => item.Id == "rac2/item/30").Ammo == ammo && player.Read() is { Projectiles: 0, AudioPlayers: 0 },
            "world transition leaked effects/audio or reset native ammunition");
        Require(cache.Reused && actors.Read().Assets == 2, "world transition discarded reusable attached-source actor data");
        actors.ResetSharedAssets(); Require(actors.Read() is { Libraries: 0, Assets: 0 }, "source invalidation retained decoded actor caches");
        Log("[trilogy-gameplay-smoke] current source-cache reuse and source invalidation PASS");
        Log("[trilogy-gameplay-smoke] lease cleanup and persistent ammunition PASS");
    }
}
