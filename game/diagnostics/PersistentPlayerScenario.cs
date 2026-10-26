using System.Numerics;
using OBP.Core;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

/// <summary>Staged persistent-owner/lifecycle test. Not original-world parity or a cross-build replay claim.</summary>
public sealed class PersistentPlayerScenario(DiagnosticScenarioDefinition definition, IDiagnosticPersistentPlayerFixture player,
    IDiagnosticPlayerObservations movement, IDiagnosticPlayerInputFixture input, IDiagnosticNavigationActions navigation,
    IDiagnosticMenuFixture menus, IDiagnosticSourceAttachmentFixture sources, IDiagnosticWorldLifecycleFixture lifecycle,
    IDiagnosticSessionEngineChecks engineChecks, Action<string> log) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = definition;
    public async Task RunAsync(DiagnosticRunContext run)
    {
        using var camera = player.PreserveCameraSettings();
        var entered = player.Read();
        Require(entered.Ordinary && entered.NativePresent && entered.State is not null, "ordinary player owner required");
        var configuration = entered.State!.Configuration;
        player.SelectWeapon(FusionWeaponSession.Lancer);
        player.HoldWrench();
        player.ApplyProvisionalContactDamage();
        player.SetPreference("camera/horizontal", camera.Horizontal == "Normal" ? "Inverted" : "Normal");
        var restore = player.CaptureRestorePoint();
        var initial = restore.State;
        foreach (string id in new[] { "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab", "rac1:LEVEL0", "obp:fusion-lab", "rac1:LEVEL0" })
        {
            Require((await navigation.NavigateAsync(id, run.CancellationToken)).Accepted, "world entry " + id);
            await run.WaitAsync(DiagnosticFramePhase.Process, 2);
            var current = player.Read();
            Require(current.SessionOwner == entered.SessionOwner && current.LiveController == entered.NativeController, "player/controller owner replaced");
            var state = current.State ?? throw new DiagnosticUnavailableException("The persistent player disappeared.");
            Require(state.Configuration == configuration && state.Generation == initial.Generation, "player profile/generation changed");
            Require(state.SelectedWeapon == initial.SelectedWeapon && state.HandEquipment is null, "selection or wrench hand ownership changed");
            Require(state.Health == initial.Health, "world entry reset player health");
            Require(state.Preferences.Count == initial.Preferences.Count && initial.Preferences.All(pair => state.Preferences.GetValueOrDefault(pair.Key) == pair.Value),
                "world entry reset player input/UI preferences");
            Require(current.AvatarPresented && current.PresentedAvatar == configuration.Avatar, "selected real avatar missing");
            Require(current.PlayerChildren == 1 && current.WorldChildren == 1, "old scene/player survived teardown");
            Require(current.Projectiles == 0 && current.AudioPlayers == 0 && current.Effects == 0, "old player effects/audio survived");
            Require(id != "obp:fusion-lab" || !current.RetailDestinationActive && current.Destination == id, "Lab impersonates a retail destination");
            if (id == "obp:fusion-lab") await CheckLabControls(run);
            log($"[player-session-smoke] {id}: same player/profile/selection/health; one player/scene; no old effects");
        }
        await CheckDeferredEntry(run);
        await engineChecks.RunAsync(run.CancellationToken);
        var live = player.Read();
        using (sources.Detach(ObpSourceGame.Rac1, refreshOnDetach: false))
        {
            Require(!(await navigation.NavigateAsync("obp:fusion-lab", run.CancellationToken)).Accepted, "missing required player source was accepted");
            var current = player.Read();
            Require(live.WorldInstance == current.WorldInstance && live.PlayerNode == current.PlayerNode, "missing avatar source destroyed the live player/world");
            Require(current.State?.SelectedWeapon == initial.SelectedWeapon, "source removal reset desired equipment");
        }
        Require(!lifecycle.TryFailedPreparation(), "invalid source was admitted");
        var afterFailure = player.Read();
        Require(live.WorldInstance == afterFailure.WorldInstance && live.PlayerNode == afterFailure.PlayerNode && entered.SessionOwner == afterFailure.SessionOwner,
            "failed preparation destroyed the live world/player");
        bool ownedBlaster = afterFailure.NativeBlasterOwned;
        player.SetEquipmentPreview(disable: false);
        Require(player.Read().NativeBlasterOwned, "diagnostic preview did not bind to the player owner");
        player.SetEquipmentPreview(disable: true);
        Require(player.Read().NativeBlasterOwned == ownedBlaster, "preview granted permanent ownership");
        player.SetPreference("camera/horizontal", camera.Horizontal);
        restore.Restore();
        Require(player.ReadCameraHorizontal() == initial.Preferences["camera/horizontal"], "restore did not apply host preferences");
        log("[player-session-smoke] PASS: persistent owner, native/custom round-trip, retained selections/health and failed-stage isolation");
    }

    private async Task CheckDeferredEntry(DiagnosticRunContext run)
    {
        var original = player.Read();
        await using (var deferred = lifecycle.ScheduleLabAfterNextFrame(run.CancellationToken))
        {
            menus.ShowWorldSelect();
            await deferred.Completion;
            await run.WaitAsync(DiagnosticFramePhase.Process);
            var current = player.Read();
            Require(!deferred.FailureReported, "canceled deferred entry reported failure");
            Require(!deferred.EntryRan && current.WorldInstance is null && menus.ReadNavigation().SelectorActive, "deferred entry replaced newer navigation");
            Require(original.SessionOwner == current.SessionOwner, "canceling navigation replaced the player owner");
        }
        Require((await navigation.NavigateAsync("rac1:LEVEL0", run.CancellationToken)).Accepted, "entry after cancellation failed");
        log("[player-session-smoke] PASS: canceled deferred entry cannot replace newer navigation");
    }
    private async Task CheckLabControls(DiagnosticRunContext run)
    {
        var original = player.Read().State!;
        foreach (var weapon in new[] { new SourceWeaponKey(ObpSourceGame.Rac1, 8), new(ObpSourceGame.Rac1, 10), FusionWeaponSession.Lancer, FusionWeaponSession.ShockCannon })
        {
            player.SelectWeapon(weapon);
            var hand = player.Read().State!.HandEquipment;
            Require((await navigation.NavigateAsync("obp:fusion-lab", run.CancellationToken)).Accepted, "Lab control trial entry failed");
            Require(player.Read().State is { } state && state.SelectedWeapon == weapon && state.HandEquipment == hand, "Lab control trial reset equipment");
            using var driver = input.Acquire();
            driver.SetFilter(_ => new());
            await run.WaitAsync(DiagnosticFramePhase.Physics, 30);
            var start = movement.ReadPlayer().Position;
            driver.SetFilter(_ => new(Move: new(0, -1)));
            await run.WaitAsync(DiagnosticFramePhase.Physics, 90);
            var end = movement.ReadPlayer().Position;
            float distance = new Vector2(end.X - start.X, end.Z - start.Z).Length();
            Require(distance > 1, "Lab entry left the persistent player unable to move");
            driver.SetFilter(_ => new());
            await run.WaitAsync(DiagnosticFramePhase.Physics, 20);
            float floor = movement.ReadPlayer().Position.Y, apex = floor;
            driver.SetFilter(_ => new(JumpHeld: true));
            for (int i = 0; i < 30; i++) { await run.WaitAsync(DiagnosticFramePhase.Physics); apex = Math.Max(apex, movement.ReadPlayer().Position.Y); }
            Require(apex > floor + .1f, "Lab entry left the persistent player unable to jump");
            log($"[player-session-smoke] Lab controls {weapon}: moved {distance:0.00}, jumped {apex - floor:0.00}");
        }
        player.SelectWeapon(original.SelectedWeapon!.Value);
        if (original.HandEquipment is null) player.HoldWrench();
    }
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
}
