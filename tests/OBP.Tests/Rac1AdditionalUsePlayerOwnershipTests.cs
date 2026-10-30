using Microsoft.CodeAnalysis;

namespace OBP.Tests;

/// <summary>
/// #367/#368: player-owned R&C1 additional-weapon requests and animation
/// use counters are separate from world-owned shot/beam/flame presentation.
/// Native cadence and ammo remain in the single source session.
/// </summary>
public sealed class Rac1AdditionalUsePlayerOwnershipTests
{
    private static string Script(string name) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", name));

    private static void AssertIncreasing(string text, params string[] needles)
    {
        int prior = -1;
        foreach (string needle in needles)
        {
            int next = text.IndexOf(needle, StringComparison.Ordinal);
            Assert.True(next > prior, "Expected order, but could not find the next step: " + needle);
            prior = next;
        }
    }

    [Fact]
    public void NativeUseSessionAndPlayerTimingHaveOnePersistentOwner()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string member in new[]
        {
            "_rac1AdditionalWeapons", "_rac1AdditionalWeaponsInventory",
            "_rac1AdditionalFireRequested", "_rac1PyroEmitting",
            "_rac1EquipmentUseWeapon", "_rac1EquipmentUseTicks",
            "_rac1EquipmentUseAge", "_rac1WeaponShotsFired", "_rac1WeaponGenerations",
        })
            Assert.Empty(root.GetMembers(member));

        var player = compilation.GetTypeByMetadataName("OBP.Game.PlayerSessionHost")!;
        Assert.Equal("Rac1AdditionalUseHost",
            Assert.Single(player.GetMembers("Rac1Use").OfType<IPropertySymbol>()).Type.Name);
        string host = Script("Rac1AdditionalUseHost.cs");
        string playerHost = Script("PlayerSessionHost.cs");

        Assert.Contains("internal Rac1AdditionalUseHost Rac1Use { get; } = new()", playerHost, StringComparison.Ordinal);
        Assert.Contains("internal Rac1AdditionalWeaponSession? Session { get; private set; }", host, StringComparison.Ordinal);
        Assert.Contains("Session = new(inventory)", host, StringComparison.Ordinal);
        Assert.Contains("BoundInventory = inventory", host, StringComparison.Ordinal);
        Assert.DoesNotContain("using Godot;", host, StringComparison.Ordinal);
        Assert.DoesNotContain("Node3D", host, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1World.", host, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1WeaponInventory(", host, StringComparison.Ordinal);
        Assert.DoesNotContain("_weaponGenerations", host, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/Rac1AdditionalUseHost.cs.uid")));
    }

    [Fact]
    public void PlayerAdmissionAndUseClocksRunBeforeNativeStepAndPyroTransition()
    {
        string host = Script("Rac1AdditionalUseHost.cs");
        int begin = host.IndexOf("internal Rac1AdditionalUseFrame BeginFrame(", StringComparison.Ordinal);
        int end = host.IndexOf("internal void RecordShot(", StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin);
        AssertIncreasing(host[begin..end],
            "bool held = eligible && FireRequested && !detectorEquipped()",
            "FireRequested = false;",
            "setArsenalHeld(held)",
            "if (_useWeapon != inventory.Equipped)",
            "if (UseTicks > 0) { UseTicks--; UseAge++; }",
            "var shot = session.Step(held, admitArsenalUse())",
            "bool pyro = held && inventory.Equipped == Rac1WeaponId.Pyrocitor",
            "bool changed = pyro != PyroEmitting",
            "PyroEmitting = pyro",
            "return new(held, pyro, changed, shot)");

        Assert.Contains("inventory.GetAmmo(16) > 0 || shot is not null", host, StringComparison.Ordinal);
        Assert.Contains("if (UseTicks == 0) UseAge = 0", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponId.Blaster => 6", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponId.Devastator => 35", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponId.Ryno => Rac1ArsenalRules.FireTicks(shot.Weapon)", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponId.Walloper => Rac1ArsenalRules.WalloperDurationTicks", host, StringComparison.Ordinal);
        Assert.Contains("ShotsFired++", host, StringComparison.Ordinal);
        Assert.Contains("inventory.GetAmmo(19) > 0", host, StringComparison.Ordinal);
        Assert.Contains("inventory.Equipped == Rac1WeaponId.SuckCannon && Session?.Vacuuming == true", host, StringComparison.Ordinal);
        Assert.Contains("if (continuous) UseTicks = Math.Max(UseTicks, 2)", host, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldPresentationFollowsSameShotAndAudioDispatchOrder()
    {
        string root = Script("OBPGame.Rac1Weapons.cs");
        int start = root.IndexOf("private void StepRac1AdditionalWeapons()", StringComparison.Ordinal);
        int end = root.IndexOf("private long _rac1WeaponPoseGeneration", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        string step = root[start..end];

        AssertIncreasing(step,
            "MovementReproduction.IsLoading",
            "var frame = use.BeginFrame(",
            "if (frame.PyroChanged",
            "pyroView.Play(frame.Pyro ? 3 : 5",
            "if (frame.Shot is { } shot)",
            "use.RecordShot(shot)",
            "view.Play(Rac1ArsenalRules.HandFireSequence(shot.Weapon)",
            "Rac1World.Weapons.Projectiles.Throw(shot)",
            "Rac1World.Weapons.Projectiles.FireBlaster(shot)",
            "Rac1World.Weapons.SuckCannon.Fire(shot)",
            "Rac1World.Weapons.Arsenal.Fire(shot.Weapon)",
            "PersistRac1CampaignState(",
            "RefreshRac1HudState()",
            "use.CompleteFrame(frame, _rac1Weapons)",
            "StepRac1WeaponAudio()");

        Assert.Contains("Rac1World.Weapons.Arsenal.SetHeld", step, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Arsenal.AdmitUse", step, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1AdditionalWeaponSession(", step, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1WeaponInventory(", step, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.Rac1Use.UseTicks > 0 ? 4 : 1", root, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.Rac1Use.Session?.Vacuuming == true", root, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaySelectionPreviewAndDiagnosticsBorrowTheSamePlayerState()
    {
        string host = Script("Rac1AdditionalUseHost.cs");
        string root = Script("OBPGame.cs");
        string combat = Script("OBPGame.Rac1Combat.cs");
        string weapons = Script("OBPGame.Rac1Weapons.cs");
        string worldEntry = Script("OBPGame.WorldEntry.cs");

        Assert.Contains("_persistentPlayer.Rac1Use.RequestFire()", combat, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.Rac1Use.CancelFire()", root, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.Rac1Use.ClearUseTicks()", root, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.Rac1Use.StopPyro()", root, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.Rac1Use.UseTicks, _persistentPlayer.Rac1Use.ShotsFired", root, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.Rac1Use.PyroEmitting", root, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.Rac1Use.UseTicks > 0", worldEntry, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.Rac1Use.UseAge / 60d", worldEntry, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.Rac1Use.PyroEmitting", Script("OBPGame.Rac1WeaponWorld.cs"), StringComparison.Ordinal);

        AssertIncreasing(weapons,
            "ResetRac1Arsenal()",
            "Rac1World.Weapons.Projectiles.RetireShotsAgentsMotes()",
            "Rac1World.Weapons.Flames.ClearCooldowns()",
            "Rac1World.Weapons.Flames.ClearTrackedFlames()",
            "_persistentPlayer.Rac1Use.ResetSession(_rac1Weapons)",
            "Rac1World.Weapons.Projectiles.ResetFlashes()",
            "_persistentPlayer.Rac1Use.ResetInputState()");
        Assert.Contains("_persistentPlayer.Rac1Use.BoundInventory != _rac1Weapons", weapons, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.Rac1Use.BindInventory(_rac1Weapons)", weapons, StringComparison.Ordinal);
        Assert.Contains("internal void ResetInputState()", host, StringComparison.Ordinal);
        Assert.Contains("UseTicks = UseAge = 0", host, StringComparison.Ordinal);
        Assert.Contains("ShotsFired = 0", host, StringComparison.Ordinal);
    }
}
