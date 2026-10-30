using Microsoft.CodeAnalysis;

namespace OBP.Tests;

/// <summary>
/// #367/#368: world-owned equipment projectiles, Doom agents and transient
/// visuals replace root-held R&C1 lists, without taking player inventory,
/// recovered damage policy or the native Pyrocitor particle pool.
/// </summary>
public sealed class Rac1EquipmentProjectileWorldOwnershipTests
{
    private static string Script(string name) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", name));

    [Fact]
    public void AllNonPyrocitorProjectileAndTransientCollectionsLeaveRoot()
    {
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (var member in new[]
        {
            "_rac1EquipmentShots", "_rac1DoomAgents", "_rac1WeaponMotes",
            "_rac1WeaponFlashes", "FireRac1Blaster", "SpawnRac1ThrownEquipment",
            "StepRac1DoomAgents", "StepRac1WeaponMotes", "SpawnRac1DoomAgent",
            "ExplodeEquipment", "ResolveEquipmentBlast", "RetireEquipmentShot",
        })
            Assert.Empty(root.GetMembers(member));

        var host = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.Rac1WeaponHost")!;
        Assert.Equal("Rac1EquipmentProjectileHost",
            Assert.Single(host.GetMembers("Projectiles").OfType<IPropertySymbol>()).Type.Name);

        string composition = Script("OBPGame.cs");
        string world = Script("Rac1WorldModule.cs");
        string weapons = Script("Rac1WeaponHost.cs");
        string projectile = Script("Rac1EquipmentProjectileHost.cs");

        Assert.Contains("Projectiles = new(Contacts, projectiles)", weapons, StringComparison.Ordinal);
        Assert.Contains("Projectiles.Reset()", weapons, StringComparison.Ordinal);
        Assert.Contains("Projectiles.Dispose()", weapons, StringComparison.Ordinal);
        Assert.Contains("beamPresentation, arsenal, projectiles)", world, StringComparison.Ordinal);
        Assert.Contains("new Rac1EquipmentProjectilePorts(", composition, StringComparison.Ordinal);
        Assert.Contains("internal IReadOnlyList<Rac1HostedEquipmentShot> Shots", projectile, StringComparison.Ordinal);
        Assert.Contains("internal IReadOnlyList<Rac1HostedDoomAgent> DoomAgents", projectile, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", projectile, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/Rac1EquipmentProjectileHost.cs.uid")));
    }

    [Fact]
    public void EquipmentFrameUpdatesPreserveOriginalOrderAndPyrocitorAuthority()
    {
        string frame = Script("OBPGame.Rac1WeaponWorld.cs");
        string projectile = Script("Rac1EquipmentProjectileHost.cs");
        string[] ordered =
        [
            "StepRac1ArsenalWorld()",
            "if (_rac1PyroEmitting && EnsureRac1Particles()",
            "Rac1World.Weapons.Projectiles.StepMotes()",
            "StepRac1NativeFlameContacts()",
            "Rac1World.Weapons.SuckCannon.Step()",
            "Rac1World.Weapons.Projectiles.StepShots()",
            "Rac1World.Weapons.Projectiles.StepDoomAgents()",
            "Rac1World.Weapons.Projectiles.StepFlashes()",
        ];
        int previous = -1;
        foreach (var member in ordered)
        {
            int next = frame.IndexOf(member, StringComparison.Ordinal);
            Assert.True(next > previous, "World frame order changed: " + member);
            previous = next;
        }

        Assert.Contains("_rac1NativeFlames.Add(new(created[2]))", frame, StringComparison.Ordinal);
        Assert.Contains("if ((_rac1WeaponWorldTick & 3) == 0 && created.Count > 2)", frame, StringComparison.Ordinal);
        Assert.Contains("flame.Victims.Add(key); _rac1FlameHitTicks[key]", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("Pyrocitor(", projectile, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1ParticleSession(", projectile, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeCarrierMineAndDoomLifetimesRetainRecoveredSourceRules()
    {
        string host = Script("Rac1EquipmentProjectileHost.cs");
        string weapons = Script("OBPGame.Rac1Weapons.cs");

        Assert.Contains("motion.Step(shot.AimPoint, 1.5f * MathF.PI)", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponProfiles.BlasterCarrierTicks", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponReconstructionPolicy.SuckProjectileLifetimeTicks", host, StringComparison.Ordinal);
        Assert.Contains("_contacts.Contact(shot.Position, next, shot.Weapon == Rac1WeaponId.Blaster ? .15f : .25f)", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponProfiles.BlasterDamage : Rac1WeaponReconstructionPolicy.SuckDamage", host, StringComparison.Ordinal);
        Assert.Contains("Rac1BombProjectileSession.LaunchOrigin(body, yaw)", host, StringComparison.Ordinal);
        Assert.Contains("Rac1BombProjectileSession.LaunchVelocity(origin, target, yaw)", host, StringComparison.Ordinal);
        Assert.Contains("Rac1BombProjectileSession.GravityStep", host, StringComparison.Ordinal);
        Assert.Contains("canister.Step(_doomAgents.Count)", host, StringComparison.Ordinal);
        Assert.Contains("if (mines.Length > 8) ExplodeEquipment(mines[0]", host, StringComparison.Ordinal);
        Assert.Contains("if (shot.Age > Rac1WeaponReconstructionPolicy.PlantedLifetimeTicks || shot.Position.Y < -100)", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponProfiles.ChildDamage(weapon, nativeClass, id,", host, StringComparison.Ordinal);
        Assert.Contains("bot.Motion.Step(Rac1LootPresentation.ToEffectNative(origin), targetPoint)", host, StringComparison.Ordinal);
        Assert.Contains("bot.Motion.SourceSequence(direction.LengthSquared() > .01f)", host, StringComparison.Ordinal);
        Assert.Contains("new Rac1TaunterMine(shot.Position, () => shot.Taunted = true)", Script("OBPGame.cs"), StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Projectiles.FireBlaster(shot)", weapons, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Projectiles.Throw(shot)", weapons, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1AdditionalWeaponSession(", host, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1WeaponInventory(", host, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneResetAndDiagnosticsUseSameOwnedLifetime()
    {
        string root = Script("OBPGame.Rac1Weapons.cs");
        string host = Script("Rac1EquipmentProjectileHost.cs");
        string composition = Script("OBPGame.cs");

        int arsenal = root.IndexOf("ResetRac1Arsenal()", StringComparison.Ordinal);
        int retire = root.IndexOf("Rac1World.Weapons.Projectiles.RetireShotsAgentsMotes()", StringComparison.Ordinal);
        int suck = root.IndexOf("Rac1World.Weapons.SuckCannon.Reset()", StringComparison.Ordinal);
        int particles = root.IndexOf("_rac1NativeFlames.Clear()", StringComparison.Ordinal);
        int flashes = root.IndexOf("Rac1World.Weapons.Projectiles.ResetFlashes()", StringComparison.Ordinal);
        Assert.True(arsenal >= 0 && arsenal < retire && retire < suck && suck < particles && particles < flashes);

        Assert.Contains("if (GodotObject.IsInstanceValid(node)) node.QueueFree()", host, StringComparison.Ordinal);
        Assert.Contains("_shots.Clear(); _doomAgents.Clear(); _motes.Clear()", host, StringComparison.Ordinal);
        Assert.Contains("_flashes.Clear()", host, StringComparison.Ordinal);
        Assert.Contains("if (_disposed) return;", host, StringComparison.Ordinal);
        Assert.Contains("Reset();", host, StringComparison.Ordinal);
        Assert.Contains("owner.Weapons.Projectiles.MinePosition(id)", composition, StringComparison.Ordinal);
        Assert.Contains("owner.Weapons.Projectiles.DoomPosition(id)", composition, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Projectiles.Shots.Select(", composition, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Projectiles.DoomAgents.Select(", composition, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Projectiles.AddMote(", composition, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Projectiles.AddSuckCarrier(", composition, StringComparison.Ordinal);
    }
}
