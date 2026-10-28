using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using OBP.Core;
using OBP.Fusion;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

// Copied observations only. No controller, scene, source path or application-root access crosses this port.
internal sealed record CaptureCheckpointSnapshot(
    string? Destination, bool Paused, int Width, int Height, bool Pause, bool QuickSelect, bool PlanetMap, bool Settings,
    string? Focus, SourceWeaponKey? QuickSelected, CaptureCampaignCheckpoint Campaign, CaptureNativePlayerCheckpoint NativePlayer,
    CaptureFusionCheckpoint? Fusion, CapturePlayerCheckpoint? Player, CaptureMobyCheckpoint[] Mobies,
    CaptureObjectCheckpoint[] Objects, CaptureEnemyCheckpoint[] Enemies, double[] TransientCounts,
    CaptureBombCheckpoint[] Bombs, CaptureShotCheckpoint[] Shots, CaptureBodyCheckpoint[] Doom,
    CapturePositionCheckpoint[] Pickups, CaptureCrankCheckpoint[] Cranks);
internal sealed record CaptureCampaignCheckpoint(int Current, double[] Admissions, double[] Map, double[] Levels);
internal sealed record CaptureNativePlayerCheckpoint(int Nanotech, bool Dead, long Bolts, int CurrentItem, int HeldItem,
    string HeldEquipment, JsonElement Inventory, JsonElement CameraOptions);
internal sealed record CapturePlayerCheckpoint(Vector3 Position, Vector3 Velocity, double Yaw, double ControlYaw,
    bool OnFloor, bool RecoveredSupport, string Phase, string Animation, double AvatarSequence, double AvatarFrame,
    double AvatarFps, string Step, JsonElement Action, JsonElement Wrench, Vector3 CameraPosition, Vector3 CameraRotation);
internal sealed record CaptureFusionCheckpoint(string Selected, JsonElement Configuration, CaptureWeaponCheckpoint[] Weapons,
    double[] Counts, JsonElement LastDamage, JsonElement Cannon, CaptureProjectileCheckpoint[] Projectiles);
internal sealed record CaptureWeaponCheckpoint(string Key, JsonElement Status);
internal sealed record CaptureProjectileCheckpoint(long Generation, long Sequence, Vector3 Position);
internal sealed record CaptureMobyCheckpoint(int Class, int Instance, int State, bool Active, bool Terminal);
internal sealed record CaptureObjectCheckpoint(int Class, int Instance, Vector3 Position, Vector3 Rotation, bool Visible);
internal sealed record CaptureEnemyCheckpoint(int Id, Vector3 Velocity, double Sequence, double AnimationPosition,
    double DeathTicks, bool Reward, bool Buried, double? ClassHealth, double? HostileHealth);
internal sealed record CaptureBombCheckpoint(long Id, Vector3 Position, Vector3 Velocity, double State, double BlastAge);
internal sealed record CaptureShotCheckpoint(long Id, Vector3 Position, Vector3 Step, int Weapon, double Age, bool Grounded);
internal sealed record CaptureBodyCheckpoint(long Id, Vector3 Position, Vector3 Velocity);
internal sealed record CapturePositionCheckpoint(long Id, Vector3 Position);
internal sealed record CaptureCrankCheckpoint(int Id, double Progress);
internal sealed record CaptureActorCheckpoint(WeaponTargetKey Target, Vector3 Position, bool Alive, bool Retired,
    double Health, double State, double Sequence);
internal sealed record CaptureSharedCheckpoint(ProfilePlayer Player, IReadOnlyList<ProfileSource> Sources,
    CaptureActorCheckpoint[] Actors, long ActorTick);

internal static class CaptureCheckpointBuilder
{
    internal static SortedDictionary<string, PlaytestValue> BuildValues(CaptureCheckpointSnapshot snapshot)
    {
        var values = new SortedDictionary<string, PlaytestValue>(StringComparer.Ordinal);
        void Text(string key, string? value) => values.Add(key, new(Text: value ?? ""));
        void Numbers(string key, params double[] data) => values.Add(key, new(data));
        void Vector(string key, Vector3 value) => values.Add(key, new([value.X, value.Y, value.Z], Tolerance: .002));
        Text("world.destination", snapshot.Destination);
        Numbers("world.paused", snapshot.Paused ? 1 : 0);
        Numbers("world.viewport", snapshot.Width, snapshot.Height);
        Numbers("world.ui", snapshot.Pause ? 1 : 0, snapshot.QuickSelect ? 1 : 0, snapshot.PlanetMap ? 1 : 0, snapshot.Settings ? 1 : 0);
        Text("ui.focus", snapshot.Focus);
        Numbers("ui.quickSelect", snapshot.QuickSelected?.NativeItemId ?? -1);
        if (snapshot.Fusion is not null) Text("ui.quickSelect.source", snapshot.QuickSelected is { } selected ? SourceIdentityWire.FormatSource(selected.SourceGame) : null);
        var campaign = snapshot.Campaign;
        Numbers("campaign.current", campaign.Current);
        Numbers("campaign.admissions", campaign.Admissions);
        Numbers("campaign.map", campaign.Map);
        Numbers("campaign.levels", campaign.Levels);
        var native = snapshot.NativePlayer;
        Numbers("player.nanotech", native.Nanotech, native.Dead ? 1 : 0);
        Numbers("player.bolts", native.Bolts);
        Numbers("player.equipment", native.CurrentItem, native.HeldItem);
        Text("player.heldEquipment", native.HeldEquipment);
        Text("player.inventory", native.Inventory.GetRawText());
        Text("camera.options", native.CameraOptions.GetRawText());
        if (snapshot.Fusion is { } fusion)
        {
            Text("fusion.selected", fusion.Selected);
            Text("fusion.sources", fusion.Configuration.GetRawText());
            foreach (var weapon in fusion.Weapons) Text("fusion.status." + weapon.Key, weapon.Status.GetRawText());
            Numbers("fusion.counts", fusion.Counts);
            Text("fusion.lastDamage", fusion.LastDamage.GetRawText());
            Text("fusion.cannon", fusion.Cannon.GetRawText());
            foreach (var projectile in fusion.Projectiles.OrderBy(p => p.Generation).ThenBy(p => p.Sequence))
                Vector($"fusion.projectile.{projectile.Generation}.{projectile.Sequence}", projectile.Position);
        }
        if (snapshot.Player is { } player)
        {
            Vector("player.position", player.Position);
            Vector("player.velocity", player.Velocity);
            values["player.facing"] = new([player.Yaw, player.ControlYaw], Tolerance: .0001);
            Numbers("player.support", player.OnFloor ? 1 : 0, player.RecoveredSupport ? 1 : 0);
            Text("player.phase", player.Phase);
            Text("player.animation", player.Animation);
            Numbers("player.avatar", player.AvatarSequence, player.AvatarFrame, player.AvatarFps);
            Text("player.step", player.Step);
            Text("player.action", player.Action.GetRawText());
            Text("player.wrench", player.Wrench.GetRawText());
            Vector("camera.position", player.CameraPosition);
            Vector("camera.rotation", player.CameraRotation);
        }
        foreach (var moby in snapshot.Mobies.OrderBy(m => m.Class).ThenBy(m => m.Instance))
            Numbers($"moby.{moby.Class}.{moby.Instance}.state", moby.State, moby.Active ? 1 : 0, moby.Terminal ? 1 : 0);
        foreach (var node in snapshot.Objects)
        {
            string key = $"object.{node.Class}.{node.Instance}";
            Vector(key + ".position", node.Position); Vector(key + ".rotation", node.Rotation);
            Numbers(key + ".visible", node.Visible ? 1 : 0);
        }
        foreach (var enemy in snapshot.Enemies.OrderBy(e => e.Id))
        {
            Vector($"enemy.{enemy.Id}.velocity", enemy.Velocity);
            values[$"enemy.{enemy.Id}.animation"] = new([enemy.Sequence, enemy.AnimationPosition], Tolerance: .0001);
            Numbers($"enemy.{enemy.Id}.lifetime", enemy.DeathTicks, enemy.Reward ? 1 : 0, enemy.Buried ? 1 : 0);
            if (enemy.ClassHealth is { } classHealth) Numbers($"enemy.{enemy.Id}.health", classHealth);
            if (enemy.HostileHealth is { } hostileHealth) Numbers($"enemy.{enemy.Id}.health", hostileHealth);
        }
        Numbers("transient.counts", snapshot.TransientCounts);
        foreach (var bomb in snapshot.Bombs.OrderBy(b => b.Id))
        {
            Vector($"bomb.{bomb.Id}.position", bomb.Position); Vector($"bomb.{bomb.Id}.velocity", bomb.Velocity);
            Numbers($"bomb.{bomb.Id}.state", bomb.State, bomb.BlastAge);
        }
        foreach (var shot in snapshot.Shots.OrderBy(s => s.Id))
        {
            Vector($"shot.{shot.Id}.position", shot.Position); Vector($"shot.{shot.Id}.step", shot.Step);
            Numbers($"shot.{shot.Id}.state", shot.Weapon, shot.Age, shot.Grounded ? 1 : 0);
        }
        foreach (var body in snapshot.Doom.OrderBy(b => b.Id))
        { Vector($"doom.{body.Id}.position", body.Position); Vector($"doom.{body.Id}.velocity", body.Velocity); }
        foreach (var pickup in snapshot.Pickups.OrderBy(p => p.Id)) Vector($"pickup.{pickup.Id}.position", pickup.Position);
        foreach (var crank in snapshot.Cranks.OrderBy(c => c.Id)) Numbers($"crank.{crank.Id}.progress", crank.Progress);
        return values;
    }

    internal static void AddShared(SortedDictionary<string, PlaytestValue> values, CaptureSharedCheckpoint snapshot, IReadOnlyList<PlaytestAdapter> adapters)
    {
        byte[] player = JsonSerializer.SerializeToUtf8Bytes(snapshot.Player with { Generation = 0 }, ObpProfile.Options);
        values.Add("obp.player", new(Text: Convert.ToHexString(SHA256.HashData(player))));
        values.Add("obp.adapters", new(Text: Digest(adapters)));
        values.Add("obp.sources", new(Text: Digest(snapshot.Sources)));
        foreach (var actor in snapshot.Actors.OrderBy(a => a.Target.SourceGame).ThenBy(a => a.Target.NativeClassId).ThenBy(a => a.Target.InstanceIndex))
        {
            var key = actor.Target;
            values.Add($"actor.{SourceIdentityWire.FormatSource(key.SourceGame)}.{key.NativeClassId}.{key.InstanceIndex}", new([actor.Position.X, actor.Position.Y, actor.Position.Z,
                actor.Alive ? 1 : 0, actor.Retired ? 1 : 0, actor.Health, actor.State, actor.Sequence], Tolerance: .0001));
        }
        values.Add("obp.actor-clock", new([snapshot.ActorTick]));
    }

    private static string Digest<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
