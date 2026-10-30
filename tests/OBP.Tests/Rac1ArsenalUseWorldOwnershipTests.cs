using Microsoft.CodeAnalysis;

namespace OBP.Tests;

/// <summary>
/// #367/#368: continuous arsenal input and family stepping belong to a single
/// R&C1 world lease; native ammo, selection, launch and hit rules stay with
/// their existing player/controller and family hosts.
/// </summary>
public sealed class Rac1ArsenalUseWorldOwnershipTests
{
    private static string Script(string file) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", file));

    [Fact]
    public void RootHeldStateAndFamilyOrchestrationAreRetired()
    {
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (var member in new[]
        {
            "_rac1ArsenalHeld", "AdmitRac1ArsenalUse", "FireRac1Arsenal",
            "StepRac1ArsenalDeployables", "StepRac1ArsenalBeams",
        })
            Assert.Empty(root.GetMembers(member));

        string host = Script("Rac1ArsenalUseHost.cs");
        string parent = Script("Rac1WeaponHost.cs");
        string module = Script("Rac1WorldModule.cs");
        string rootArsenal = Script("OBPGame.Rac1Arsenal.cs");

        Assert.Contains("Arsenal = new(this, arsenal)", parent, StringComparison.Ordinal);
        Assert.Contains("internal Rac1ArsenalUseHost Arsenal { get; }", parent, StringComparison.Ordinal);
        Assert.Contains("Arsenal.ClearHeld()", parent, StringComparison.Ordinal);
        Assert.Contains("Arsenal.Dispose()", parent, StringComparison.Ordinal);
        Assert.Contains("beamPresentation, arsenal, projectiles, flames)", module, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Arsenal.Reset()", rootArsenal, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Arsenal.Tick()", rootArsenal, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", host, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/Rac1ArsenalUseHost.cs.uid")));
    }

    [Fact]
    public void FamilyTicksAndResetFollowExistingFrameOrder()
    {
        string host = Script("Rac1ArsenalUseHost.cs");
        int tickStart = host.IndexOf("internal void Tick()", StringComparison.Ordinal);
        int tickEnd = host.IndexOf("private void TickBeams()", StringComparison.Ordinal);
        Assert.True(tickStart >= 0 && tickEnd > tickStart);
        string tick = host[tickStart..tickEnd];
        string[] ticks =
        [
            "weapons.Missiles.Tick()",
            "weapons.Deployables.Tick()",
            "weapons.Morph.TickChickens()",
            "TickBeams()",
            "weapons.Walloper.Tick()",
        ];
        int index = -1;
        foreach (string call in ticks)
        {
            int next = tick.IndexOf(call, StringComparison.Ordinal);
            Assert.True(next > index, "Family tick order changed: " + call);
            index = next;
        }

        int resetStart = host.IndexOf("internal void Reset()", StringComparison.Ordinal);
        int resetEnd = host.IndexOf("internal bool AdmitUse()", StringComparison.Ordinal);
        Assert.True(resetStart >= 0 && resetEnd > resetStart);
        string reset = host[resetStart..resetEnd];
        string[] resets =
        [
            "weapons.Missiles.Reset()",
            "weapons.Deployables.Reset()",
            "weapons.Morph.Reset()",
            "weapons.Beams.Reset()",
            "weapons.Taunter.Reset()",
            "weapons.Walloper.Reset()",
            "ClearHeld()",
            "weapons.Tesla.Reset()",
        ];
        index = -1;
        foreach (string call in resets)
        {
            int next = reset.IndexOf(call, StringComparison.Ordinal);
            Assert.True(next > index, "Family reset order changed: " + call);
            index = next;
        }
    }

    [Fact]
    public void BeamAndUseAdmissionRetainNativeGatesAndBorrowedPlayerInputs()
    {
        string host = Script("Rac1ArsenalUseHost.cs");
        string root = Script("OBPGame.cs");
        string weapons = Script("OBPGame.Rac1Weapons.cs");

        Assert.Contains("Func<Rac1WeaponId> Equipped", host, StringComparison.Ordinal);
        Assert.Contains("Func<int> TeslaAmmo", host, StringComparison.Ordinal);
        Assert.Contains("Func<ulong> WorldTick", host, StringComparison.Ordinal);
        Assert.Contains("if (!_held) { weapons.Tesla.Release(); return; }", host, StringComparison.Ordinal);
        Assert.Contains("if (weapon != Rac1WeaponId.TeslaClaw) weapons.Tesla.Release()", host, StringComparison.Ordinal);
        Assert.Contains("if (weapon == Rac1WeaponId.TeslaClaw && ports.TeslaAmmo() <= 0) return", host, StringComparison.Ordinal);
        Assert.Contains("weapons.Beams.AdvanceTesla()", host, StringComparison.Ordinal);
        Assert.Contains("ports.WorldTick() % 3 == 0", host, StringComparison.Ordinal);
        Assert.Contains("weapons.Taunter.Tick(from, ports.EquipmentForward(), ports.PlayerPosition())", host, StringComparison.Ordinal);
        Assert.Contains("weapons.Tesla.Tick(from, ports.EquipmentForward())", host, StringComparison.Ordinal);
        Assert.Contains("weapons.Morph.Fire(from, ports.EquipmentForward())", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponId.VisibombGun => !weapons.Missiles.GuidedActive", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponId.DroneDevice => weapons.Deployables.CanFireDrone", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponId.Ryno => weapons.Missiles.CanBeginRyno", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponId.DecoyGlove => weapons.Deployables.CanFireDecoy", host, StringComparison.Ordinal);
        Assert.Contains("new Rac1ArsenalUsePorts(", root, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Arsenal.SetHeld, Rac1World.Weapons.Arsenal.AdmitUse)", weapons, StringComparison.Ordinal);
        Assert.Contains("setArsenalHeld(held)", Script("Rac1AdditionalUseHost.cs"), StringComparison.Ordinal);
        Assert.Contains("session.Step(held, admitArsenalUse())", Script("Rac1AdditionalUseHost.cs"), StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Arsenal.Fire(shot.Weapon)", weapons, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1WeaponInventory(", host, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1AdditionalWeaponSession _", host, StringComparison.Ordinal);
    }

    [Fact]
    public void HeldPresentationAndDisposalCannotRetainWorldState()
    {
        string host = Script("Rac1ArsenalUseHost.cs");
        string parent = Script("Rac1WeaponHost.cs");
        string root = Script("OBPGame.cs");
        string weapons = Script("OBPGame.Rac1Weapons.cs");

        Assert.Contains("internal bool Held => !_disposed && _held", host, StringComparison.Ordinal);
        Assert.Contains("if (!_disposed) _held = held", host, StringComparison.Ordinal);
        Assert.Contains("internal void ClearHeld() => _held = false", host, StringComparison.Ordinal);
        Assert.Contains("if (_disposed) return;", host, StringComparison.Ordinal);
        Assert.Contains("Arsenal.ClearHeld()", parent, StringComparison.Ordinal);
        Assert.Contains("Arsenal.Dispose()", parent, StringComparison.Ordinal);
        Assert.Contains("Weapons.Arsenal.SetHeld(false)", root, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Arsenal.Held", weapons, StringComparison.Ordinal);
        Assert.Contains("Rac1Gameplay?.Weapons.Arsenal.Held == true", weapons, StringComparison.Ordinal);
    }
}
