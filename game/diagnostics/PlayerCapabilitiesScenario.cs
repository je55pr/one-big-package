using System.Text.Json;
using OBP.Core;
using OBP.Fusion;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game.Diagnostics;

/// <summary>Explicit common-contact fixture across real leases. Its artificial deck is not a native-world parity claim.</summary>
public sealed class PlayerCapabilitiesScenario(DiagnosticScenarioDefinition definition, IDiagnosticPersistentPlayerFixture player,
    IDiagnosticPlayerInputFixture input, IDiagnosticNavigationActions navigation, IDiagnosticMenuFixture menus,
    IDiagnosticPlayerEquipmentFixture equipment, IDiagnosticPlayerInterfaceFixture ui, IDiagnosticSourceAttachmentFixture sources,
    IDiagnosticCapabilitySamples samples, IDiagnosticCommonContactFixture contact, IDiagnosticRetiredActorProbe actors,
    IDiagnosticCapabilityHealthFixture health, Func<string?> configurationIdentity, string? output, Action<string> log) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = definition;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "S1244", Justification =
        "The diagnostic requires the exact native tick endpoint: 240 / 60 = 4 seconds. A tolerance could hide a wrong tick count.")]
    public async Task RunAsync(DiagnosticRunContext run)
    {
        var entry = player.Read(); Require(entry.Ordinary && entry.NativePresent, "ordinary session required");
        var restore = player.CaptureRestorePoint(); var configuration = restore.State.Configuration;
        string? identity = configurationIdentity(); MovementReproPlayer? canonical = null;
        var baselines = new Dictionary<string, DiagnosticCapabilitySample[]>();
        foreach (string id in new[] { "rac1:LEVEL0", "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab", "rac1:LEVEL0" })
        {
            restore.Restore(); Require((await navigation.NavigateAsync(id, run.CancellationToken)).Accepted, "entry failed " + id);
            await run.WaitAsync(DiagnosticFramePhase.Physics, 3); await run.WaitAsync(DiagnosticFramePhase.Process);
            Require(actors.TryDamageRetiredActor() is null or PlayerIntakeStatus.StaleLease, "old scene's actor callback was rebound to the new health lease");
            actors.RememberCurrentActor();
            var observation = player.Read(); var surface = ui.Read();
            Require(observation.NativeProfile && observation.RecoveredCamera, "profile/camera changed with world");
            Require(observation.AvatarPresented && observation.PresentedAvatar == configuration.Avatar, "genuine selected avatar missing");
            Require(surface.Health is not null && surface.Bolts is not null && surface.HudVisible, "shared HUD hidden");
            Require(configurationIdentity() == identity, "world changed catalog configuration identity");
            Require(observation.PlayerChildren == 1 && observation.WorldChildren == 1, "duplicate world/player after re-entry");
            using var deck = contact.CreateDeck();
            await run.WaitAsync(DiagnosticFramePhase.Physics, 2);
            using var driver = input.Acquire(); driver.SetFilter(_ => new());
            contact.ResetPlayerOnDeck(); await run.WaitAsync(DiagnosticFramePhase.Physics, 40);
            canonical ??= contact.CaptureMovement();
            foreach (var weapon in new[] { FusionWeaponSession.Lancer, FusionWeaponSession.ShockCannon })
            {
                contact.RestoreMovement(canonical); player.SelectWeapon(weapon);
                Require(player.Read().State!.HandEquipment == weapon, "supported foreign weapon did not draw");
                var trace = await PlayerCapabilityTrial.CaptureAsync(run, samples, driver);
                string key = weapon.ToString();
                if (baselines.TryGetValue(key, out var baseline)) Require(JsonSerializer.Serialize(trace) == JsonSerializer.Serialize(baseline), "movement/pose/attachment/camera trace differs in " + id + " for " + key);
                else baselines.Add(key, trace);
                Require(trace.Select(sample => sample.Sequence).Distinct().Count() >= 3, "fixture did not exercise native sequence transitions");
                Require(trace[^1].Clock == 4, "240 native ticks did not take four native seconds");
                Require(player.Read().State is { } state && state.SelectedWeapon == weapon && state.HandEquipment is null, "wrench attack did not retain hand ownership");
                log($"[player-capabilities-smoke] {id} {weapon}: matching 240 ticks, movement, camera, pose, attachments and wrench ownership");
            }
            driver.SetFilter(_ => new()); await CheckMenuMask(run, driver);
        }
        Require(equipment.Assign(7, "rac2/item/30").Accepted, "foreign Quick Select assignment failed");
        Require(player.Read().State!.QuickSelect[7] == FusionWeaponSession.Lancer, "foreign assignment lost identity");
        Require(ui.OpenQuickSelect(), "assigned wheel unavailable");
        try { var wheel = ui.Read().WheelEntries; Require(wheel[7] == FusionWeaponSession.Lancer && wheel[1] is null, "wheel compacted a cleared slot or moved a foreign slot"); }
        finally { ui.CloseQuickSelect(); }
        Require(equipment.EnableGadget("rac1/item/3", true).Accepted && !equipment.HeliPackEnabled, "conflicting packs enabled");
        Require(!equipment.Select("rac2/appearance/ratchet").Accepted && !equipment.EnableGadget("rac1/item/28", true).Accepted, "unsupported skin/gadget admitted");
        await CheckSourceRecovery(run); await CheckHealth(run);
        if (output is not null) File.WriteAllText(output, JsonSerializer.Serialize(new { Version = 1, Baselines = baselines }));
        log("[player-capabilities-smoke] PASS: world-independent profiles, native clock/poses, camera, input masking, source recovery, catalog and shared health");
    }

    private async Task CheckMenuMask(DiagnosticRunContext run, IDiagnosticPlayerInputLease driver)
    {
        var selected = player.Read().State!.SelectedWeapon;
        Require(ui.OpenQuickSelect(), "Quick Select unavailable");
        try
        {
            var before = samples.Read();
            driver.SetFilter(_ => new(Move: System.Numerics.Vector2.One, JumpHeld: true, ActionJustPressed: true, ActionHeld: true, TraversalHeld: true));
            await run.WaitAsync(DiagnosticFramePhase.Physics, 8);
            var after = samples.Read();
            Require(before.ShotsFired == after.ShotsFired && before.WrenchGeneration == after.WrenchGeneration && player.Read().State!.SelectedWeapon == selected, "Quick Select leaked attack input");
        }
        finally { ui.CloseQuickSelect(); driver.SetFilter(_ => new()); }
        Require(menus.OpenPause(), "pause unavailable");
        try { long tick = samples.Read().PhysicsTick; await run.WaitAsync(DiagnosticFramePhase.Process, 2); Require(samples.Read().PhysicsTick == tick, "pause advanced native player clock"); }
        finally { menus.ClosePause(); }
    }
    private async Task CheckSourceRecovery(DiagnosticRunContext run)
    {
        Require(equipment.Select("rac2/item/30").Accepted, "Lancer selection failed");
        using (sources.Detach(ObpSourceGame.Rac2, refreshOnDetach: true))
        {
            var entry = equipment.Read().Single(entry => entry.Definition.Item == FusionWeaponSession.Lancer);
            var state = player.Read().State!;
            Require(entry.Usability == EquipmentUsability.MissingSource && state.SelectedWeapon == FusionWeaponSession.Lancer && state.HandEquipment is null,
                "source removal changed desired loadout or did not use safe wrench fallback");
        }
        Require(equipment.Select("rac2/item/30").Accepted && player.Read().State!.HandEquipment == FusionWeaponSession.Lancer, "source recovery could not re-equip remembered weapon");
        await run.WaitAsync(DiagnosticFramePhase.Physics);
    }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "S1244", Justification =
        "Health is a quantized native counter. These checks require exact counter values and an identical HUD copy, not approximate computed geometry.")]
    private async Task CheckHealth(DiagnosticRunContext run)
    {
        Require((await navigation.NavigateAsync("rac2:LEVEL0", run.CancellationToken)).Accepted, "Aranos source combat entry failed");
        await run.WaitAsync(DiagnosticFramePhase.Physics, 3);
        Require(health.StageFirstRac2HostileContact(), "no admitted GC attack controller");
        using var idle = input.Acquire(); idle.SetFilter(_ => new());
        for (int i = 0; i < 600 && health.Read().Current == 4; i++) await run.WaitAsync(DiagnosticFramePhase.Physics);
        var hit = health.Read();
        Require(hit.Current < 4 && hit.SourceRac2Nanotech == 4, "native GC hostile did not damage the selected shared victim");
        Require(hit.HudCurrent == hit.Current, "GC hit did not project into the shared HUD");
        log("[player-capabilities-smoke] source GC MSR I attack reached the shared R&C1-derived victim/HUD");
        Require((await navigation.NavigateAsync("obp:fusion-lab", run.CancellationToken)).Accepted, "health lifecycle Lab entry failed");
        var before = player.Read().State!;
        health.DefeatFromWorldHazard(); await run.WaitAsync(DiagnosticFramePhase.Physics, 2);
        var dead = health.Read(); Require(!dead.Alive && !dead.GameplayAlive && dead.HudLife == HudLifeState.Dead, "death not shared");
        string? liveWorld = player.Read().WorldInstance;
        using (sources.Detach(ObpSourceGame.Rac1, refreshOnDetach: false))
            Require(!health.Respawn() && !health.Read().Alive && player.Read().WorldInstance == liveWorld, "failed respawn consumed the retry or destroyed the live scene");
        Require(health.Respawn(), "OBP respawn failed"); await run.WaitAsync(DiagnosticFramePhase.Physics, 2);
        var alive = health.Read(); Require(alive.Alive && alive.Current == 4, "respawn did not restore selected capacity");
        Require(player.Read().State!.SelectedWeapon == before.SelectedWeapon && alive.Bolts == before.Currencies["obp/bolts"], "respawn reset equipment/currency");
    }
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
}
