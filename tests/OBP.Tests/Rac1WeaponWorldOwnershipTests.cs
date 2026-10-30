using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

/// <summary>
/// First #367/#368 ownership seam. Retail-backed weapon and detector scenarios
/// remain responsible for functional parity; these source-free checks prevent
/// the engine root from reclaiming world-gadget and guided-camera state.
/// </summary>
public sealed class Rac1WeaponWorldOwnershipTests
{
    private static string Script(string file) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", file));

    private static string Method(string file, string name) =>
        HostOwnershipAudit.Host.Value.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/" + file)
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == name).ToString();

    [Fact]
    public void DetectorRunsInRac1WorldLeaseInsteadOfRootPartial()
    {
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Rac1MetalDetector.cs")));
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/Rac1MetalDetectorHost.cs.uid")));
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Rac1MetalDetector.cs.uid")));
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string field in new[]
        {
            "_rac1MetalDetector", "_rac1DetectorEquipped", "_rac1DetectorHeldTicks",
            "_rac1DetectorCarry", "_rac1DetectorNearest", "_rac1MissileCamera"
        })
            Assert.Empty(root.GetMembers(field));

        var module = compilation.GetTypeByMetadataName("OBP.Game.Rac1WorldModule")!;
        Assert.Equal("Rac1WeaponHost",
            Assert.Single(module.GetMembers("Weapons").OfType<IPropertySymbol>()).Type.Name);
        Assert.Contains("Rac1MetalDetectorHost Detector", Script("Rac1WeaponHost.cs"), StringComparison.Ordinal);
        Assert.Contains("Weapons.ResetWorld()", Method("Rac1WorldModule.cs", "ResetBindings"), StringComparison.Ordinal);
        Assert.Contains("Weapons.Configure(world)", Method("Rac1WorldModule.cs", "ConfigureActors"), StringComparison.Ordinal);
        Assert.Contains("Weapons.Tick(delta)", Method("Rac1WorldModule.cs", "Tick"), StringComparison.Ordinal);
        Assert.Contains("Weapons.Dispose()", Method("Rac1WorldModule.cs", "Dispose"), StringComparison.Ordinal);
    }

    [Fact]
    public void DetectorBorrowsNativeProgressAndEmitsPhysicalBolts()
    {
        string host = Script("Rac1MetalDetectorHost.cs");
        string root = Script("OBPGame.cs");
        Assert.Contains("Rac1MetalDetectorSession", host, StringComparison.Ordinal);
        Assert.Contains("Rac1BuriedBoltProgress.Restore(ports.BuriedBolts().Snapshot())", host, StringComparison.Ordinal);
        Assert.Contains("_session.FindNearest(nativePlayer)", host, StringComparison.Ordinal);
        Assert.Contains("_session.TryExtract(_nearest, held, _heldTicks)", host, StringComparison.Ordinal);
        Assert.Contains("ports.SpawnBursts(", host, StringComparison.Ordinal);
        Assert.Contains("Rac1MetalDetectorSession.BoltsPerBurst", root, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Loot.SpawnRac1BoltPickups", root, StringComparison.Ordinal);
        Assert.Contains("if (_disposed) return false;", Method("Rac1MetalDetectorHost.cs", "Toggle"), StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", host, StringComparison.Ordinal);
    }

    [Fact]
    public void ContactTargetGeometryAndOcclusionAreOwnedByTheSameWorldLease()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string member in new[]
        {
            "_rac1EquipmentGeometryFrame", "_rac1EquipmentGeometry",
            "_rac1BombOcclusionFrame", "_rac1BombOcclusionPlayer", "_rac1BombOcclusionExclusions",
            "EquipmentGeometry", "EquipmentTargets", "EquipmentContact", "EquipmentAim",
            "IsRac1WeaponAimTarget", "BombHasLineOfSight", "Rac1DamageableRayExclusions",
            "_rac1BombContactShape", "ProbeBombWorldContact"
        })
            Assert.Empty(root.GetMembers(member));

        var host = compilation.GetTypeByMetadataName("OBP.Game.Rac1WeaponHost")!;
        Assert.Equal("Rac1WeaponContactHost",
            Assert.Single(host.GetMembers("Contacts").OfType<IPropertySymbol>()).Type.Name);
        string weapon = Script("Rac1WeaponHost.cs");
        Assert.Contains("Contacts.Invalidate()", weapon, StringComparison.Ordinal);
        Assert.Contains("Contacts.Dispose()", weapon, StringComparison.Ordinal);
        string world = Script("Rac1WorldModule.cs");
        Assert.Contains("Weapons.ResetWorld()", world, StringComparison.Ordinal);
        Assert.Contains("Weapons.Dispose()", world, StringComparison.Ordinal);
        string application = Script("OBPGame.cs");
        Assert.Contains("new Rac1WeaponTargetPorts(", application, StringComparison.Ordinal);
        Assert.Contains("new Rac1WeaponContactPhysicsPorts(", application, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingNativeContactAndOcclusionPoliciesAreNotReplacedWithNewPhysics()
    {
        string contact = Script("Rac1WeaponContactHost.cs");
        Assert.Contains("Engine.GetPhysicsFrames()", contact, StringComparison.Ordinal);
        Assert.Contains("Rac1FrogQueenGroup.QueenClassId", contact, StringComparison.Ordinal);
        Assert.Contains("ActorContactGeometry.SegmentBoxFraction", contact, StringComparison.Ordinal);
        Assert.Contains("bounds.Grow(radius)", contact, StringComparison.Ordinal);
        Assert.Contains("player.GetWorld3D().DirectSpaceState.IntersectRay", contact, StringComparison.Ordinal);
        Assert.Contains("physics.AddRayExclusions(_occlusionExclusions)", contact, StringComparison.Ordinal);
        Assert.Contains("space.IntersectShape(query, 1)", contact, StringComparison.Ordinal);
        Assert.Contains("space.CastMotion(query)", contact, StringComparison.Ordinal);
        Assert.Contains("_bombContactShape.Dispose()", contact, StringComparison.Ordinal);
        Assert.Contains("targets.MorphView(node.Source.InstanceIndex) ?? node.Root", contact, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", contact, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyDamage(", contact, StringComparison.Ordinal);

        string arsenal = Script("OBPGame.Rac1Arsenal.cs");
        string projectile = Script("OBPGame.Rac1Projectiles.cs");
        string world = Script("OBPGame.Rac1WeaponWorld.cs");
        Assert.Contains("Rac1World.Weapons.Contacts.Contact(", arsenal, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Contacts.HasLineOfSight(", projectile, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Contacts.RayExclusions()", world, StringComparison.Ordinal);
    }

    [Fact]
    public void SuckCannonCargoAndCapturePresentationBelongToTheWorldLease()
    {
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Rac1SuckCannon.cs")));
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Rac1SuckCannon.cs.uid")));
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/Rac1SuckCannonHost.cs.uid")));

        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string member in new[]
        {
            "_rac1ChickenCargo", "_rac1Captures", "_rac1CargoModels", "HostedCapturedMoby",
            "StepRac1Vacuum", "RetireRac1CapturedCargoPresentation",
            "CloneEquipmentCargo", "FireRac1CapturedMoby"
        })
            Assert.Empty(root.GetMembers(member));

        string host = Script("Rac1SuckCannonHost.cs");
        string weapon = Script("Rac1WeaponHost.cs");
        string application = Script("OBPGame.cs");
        string restore = Script("Rac1MovementWorldStateAdapter.cs");
        Assert.Contains("Rac1AdditionalWeaponSession?", host, StringComparison.Ordinal);
        Assert.Contains("session.TryCapture(key)", host, StringComparison.Ordinal);
        Assert.Contains("Rac1MobyRuntimeKey", host, StringComparison.Ordinal);
        Assert.Contains("ports.LaunchCargo(shot, visual, visual.GlobalPosition, direction)", host, StringComparison.Ordinal);
        Assert.Contains("internal Rac1SuckCannonHost SuckCannon", weapon, StringComparison.Ordinal);
        Assert.Contains("SuckCannon.Reset()", weapon, StringComparison.Ordinal);
        Assert.Contains("SuckCannon.Dispose()", weapon, StringComparison.Ordinal);
        Assert.Contains("new Rac1SuckCannonPorts(", application, StringComparison.Ordinal);
        Assert.Contains("Action ClearChickenCargo", restore, StringComparison.Ordinal);
        Assert.Contains("Action<Rac1MobyRuntimeKey> RememberChickenCargo", restore, StringComparison.Ordinal);
        Assert.Contains("ports.ClearChickenCargo()", restore, StringComparison.Ordinal);
        Assert.Contains("ports.RememberChickenCargo(key)", restore, StringComparison.Ordinal);
        Assert.Contains("ClearChickenCargo: () => Rac1World.Weapons.SuckCannon.ClearChickenCargo()", application, StringComparison.Ordinal);
        Assert.Contains("RememberChickenCargo: key => Rac1World.Weapons.SuckCannon.RememberChickenCargo(key)", application, StringComparison.Ordinal);
        Assert.DoesNotContain("ISet<Rac1MobyRuntimeKey> ChickenCargo", host, StringComparison.Ordinal);
    }

    [Fact]
    public void CargoVisualRetirementDoesNotEraseNativeCargoOrSavedMorphMetadata()
    {
        string host = Script("Rac1SuckCannonHost.cs");
        string retirement = Method("Rac1SuckCannonHost.cs", "RetirePresentation");
        string reset = Method("Rac1SuckCannonHost.cs", "Reset");
        string fire = Method("Rac1SuckCannonHost.cs", "Fire");
        Assert.DoesNotContain("_chickenCargo.Clear()", retirement, StringComparison.Ordinal);
        Assert.Contains("_captures.Clear()", retirement, StringComparison.Ordinal);
        Assert.Contains("_models.Clear()", retirement, StringComparison.Ordinal);
        Assert.Contains("RetirePresentation()", reset, StringComparison.Ordinal);
        Assert.Contains("_chickenCargo.Clear()", reset, StringComparison.Ordinal);
        Assert.Contains("_chickenCargo.Remove(key)", fire, StringComparison.Ordinal);
        Assert.Contains("ports.EligibleTargets()", host, StringComparison.Ordinal);
        Assert.Contains("ports.PresentedTargets()", fire, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", host, StringComparison.Ordinal);
    }

    [Fact]
    public void ArsenalDeployableModelsAndEnemyAiBelongToTheWorldLease()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string member in new[]
        {
            "ArsenalDecoy", "ArsenalDrone", "_rac1Decoys", "_rac1Drones",
            "ArsenalEnemyGoal", "ArsenalEnemyHitsDecoy"
        })
            Assert.Empty(root.GetMembers(member));

        var owner = compilation.GetTypeByMetadataName("OBP.Game.Rac1WeaponHost")!;
        Assert.Equal("Rac1ArsenalDeployableHost",
            Assert.Single(owner.GetMembers("Deployables").OfType<IPropertySymbol>()).Type.Name);

        string host = Script("Rac1ArsenalDeployableHost.cs");
        string weapon = Script("Rac1WeaponHost.cs");
        string arsenal = Script("OBPGame.Rac1Arsenal.cs");
        string rootComposition = Script("OBPGame.cs");
        Assert.Contains("Deployables = new(deployables, NextArsenalId)", weapon, StringComparison.Ordinal);
        Assert.Contains("Deployables.Reset()", weapon, StringComparison.Ordinal);
        Assert.Contains("Deployables.Dispose()", weapon, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Deployables.Tick()", arsenal, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Deployables.FireDrones()", arsenal, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Deployables.FireDecoy()", arsenal, StringComparison.Ordinal);
        Assert.Contains("owner.Weapons.Deployables.EnemyGoal(enemy, player)", rootComposition, StringComparison.Ordinal);
        Assert.Contains("owner.Weapons.Deployables.EnemyHitsDecoy(enemy)", rootComposition, StringComparison.Ordinal);
        Assert.Contains("owner.Weapons.Deployables.IsDecoyNearby(enemy)", rootComposition, StringComparison.Ordinal);
        Assert.Contains("new Rac1ArsenalDeployablePorts(", rootComposition, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", host, StringComparison.Ordinal);
    }

    [Fact]
    public void ArsenalDeployablesPreserveNativeMovementAdmissionAndLiveDiagnostics()
    {
        string host = Script("Rac1ArsenalDeployableHost.cs");
        string root = Script("OBPGame.cs");
        Assert.Contains("Rac1ArsenalRules.DecoyGrowthPerTick", host, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalRules.DecoyLifetime", host, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalRules.DroneCapacity", host, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalRules.DroneDamage", host, StringComparison.Ordinal);
        Assert.Contains("ports.WorldTick() - decoy.LastHitTick >= 30", host, StringComparison.Ordinal);
        Assert.Contains("ports.IsTaunted(enemy.Node.Source.InstanceIndex)", host, StringComparison.Ordinal);
        Assert.Contains("ports.HasLineOfSight(", host, StringComparison.Ordinal);
        Assert.Contains("ports.Contact(from, next, .23f)", host, StringComparison.Ordinal);
        Assert.Contains("ports.Blast(Rac1WeaponId.DroneDevice", host, StringComparison.Ordinal);
        Assert.Contains("ports.Explosion(Rac1WeaponId.DroneDevice", host, StringComparison.Ordinal);
        Assert.Contains("ports.Sound(203, 0, decoy.View.GlobalPosition)", host, StringComparison.Ordinal);
        Assert.Contains("Func<(Rac1EquipmentView View, bool Grounded, int RetiringTicks)>", host, StringComparison.Ordinal);
        Assert.Contains("() => (decoy.View, decoy.Grounded, decoy.RetiringTicks)", host, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Deployables.ProjectDecoys((snapshot, forceExpire)", root, StringComparison.Ordinal);
        Assert.Contains("var state = snapshot()", root, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Deployables.RetireDrones()", root, StringComparison.Ordinal);
        Assert.Contains("void RetireDrones()", host, StringComparison.Ordinal);
        Assert.Contains("_decoys.Clear()", Method("Rac1ArsenalDeployableHost.cs", "Reset"), StringComparison.Ordinal);
        Assert.Contains("RetireDrones()", Method("Rac1ArsenalDeployableHost.cs", "Reset"), StringComparison.Ordinal);
        Assert.Contains("Reset()", Method("Rac1ArsenalDeployableHost.cs", "Dispose"), StringComparison.Ordinal);
    }

    [Fact]
    public void ArsenalMissileRuntimeAndRynoSessionBelongToWorldLease()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (var member in new[]
        {
            "ArsenalMissile", "_rac1ArsenalMissiles", "_rac1RynoSalvo",
            "_rac1RynoTargets", "_rac1ArsenalId", "SpawnArsenalMissile",
            "OrientArsenalView"
        })
            Assert.Empty(root.GetMembers(member));

        var owner = compilation.GetTypeByMetadataName("OBP.Game.Rac1WeaponHost")!;
        Assert.Equal("Rac1ArsenalMissileHost",
            Assert.Single(owner.GetMembers("Missiles").OfType<IPropertySymbol>()).Type.Name);
        string host = Script("Rac1ArsenalMissileHost.cs");
        string world = Script("Rac1WeaponHost.cs");
        string rootArsenal = Script("OBPGame.Rac1Arsenal.cs");
        Assert.Contains("Rac1RynoSalvoSession _ryno = new()", host, StringComparison.Ordinal);
        Assert.Contains("_ryno.Step(ports.Alive() && ports.Equipped() == Rac1WeaponId.Ryno)", host, StringComparison.Ordinal);
        Assert.Contains("_rynoTargets.Add(target.Source.InstanceIndex)", host, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalProjectile(", host, StringComparison.Ordinal);
        Assert.Contains("ports.Contact(from, to, .15f)", host, StringComparison.Ordinal);
        Assert.Contains("ports.Blast(missile.Weapon, position,", host, StringComparison.Ordinal);
        Assert.Contains("Missiles.Reset()", world, StringComparison.Ordinal);
        Assert.Contains("Missiles.Dispose()", world, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Missiles.Tick()", rootArsenal, StringComparison.Ordinal);
        Assert.Contains("nextId()", host, StringComparison.Ordinal);
        Assert.Contains("nextId()", Script("Rac1ArsenalDeployableHost.cs"), StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalViewPose.Orient(", rootArsenal, StringComparison.Ordinal);
        Assert.Contains("view.Rotation = new(0, MathF.Atan2(direction.Z, -direction.X)",
            Script("Rac1ArsenalViewPose.cs"), StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalViewPose.Orient(missile.View", host, StringComparison.Ordinal);
        Assert.Contains("Missiles = new(missiles, GuidedCamera, NextArsenalId)", world, StringComparison.Ordinal);
        Assert.Contains("internal long LastArsenalId => _arsenalId", world, StringComparison.Ordinal);
    }

    [Fact]
    public void ArsenalMissileCameraIsPassedDirectlyAndDiagnosticsProjectWitnesses()
    {
        string family = Script("Rac1ArsenalMissileHost.cs");
        string owner = Script("Rac1WeaponHost.cs");
        string application = Script("OBPGame.cs");
        Assert.Contains("Missiles = new(missiles, GuidedCamera, NextArsenalId)", owner, StringComparison.Ordinal);
        Assert.Contains("camera.Begin(ports.SceneRoot(), player)", family, StringComparison.Ordinal);
        Assert.Contains("camera.Step(to,", family, StringComparison.Ordinal);
        Assert.Contains("camera.End()", Method("Rac1ArsenalMissileHost.cs", "Reset"), StringComparison.Ordinal);
        Assert.Contains("if (guided) camera.End()", Method("Rac1ArsenalMissileHost.cs", "Tick"), StringComparison.Ordinal);
        Assert.DoesNotContain("Func<Rac1GuidedWeaponCamera>", family, StringComparison.Ordinal);
        Assert.Contains("new Rac1ArsenalMissilePorts(", application, StringComparison.Ordinal);
        Assert.Contains("(cls, sound, position) => FusionWeapons.PlayNativeSound(cls, sound, position)",
            application, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Missiles.Project((weapon, id, view)", application, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Missiles.SalvoRemaining", application, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.LastArsenalId", application, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", family, StringComparison.Ordinal);
    }

    [Fact]
    public void ContactCallbacksDoNotResolveTheWorldDuringHostConstruction()
    {
        var tree = HostOwnershipAudit.Host.Value.SyntaxTrees
            .Single(tree => tree.FilePath == "game/scripts/OBPGame.cs");
        foreach (string name in new[]
        {
            "CreateFusionWeaponHost", "CreateRac1WorldModule",
            "CreateRac1EnemyHost", "CreateActorHost", "CreateWeaponDiagnosticHarness"
        })
        {
            var factory = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == name);
            var contacts = factory.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Where(access => access.ToString().StartsWith("Rac1World.Weapons.Contacts.", StringComparison.Ordinal));
            Assert.All(contacts, access => Assert.Contains(access.Ancestors(),
                ancestor => ancestor is AnonymousFunctionExpressionSyntax));
        }
        string composition = Script("OBPGame.cs");
        Assert.Contains("node => Rac1World.Enemies.IsRac1GameplayActive(node)", composition, StringComparison.Ordinal);
        Assert.Contains("node => Rac1World.Enemies.Frogs.IsQueenNode(node)", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1World.Weapons.Contacts.RayExclusions,", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1World.Weapons.Contacts.HasLineOfSight,", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void GuidedCameraRestoresFocusOnlyWhenItStillOwnsTheCamera()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var module = compilation.GetTypeByMetadataName("OBP.Game.Rac1WorldModule")!;
        Assert.Equal("Rac1WeaponHost",
            Assert.Single(module.GetMembers("Weapons").OfType<IPropertySymbol>()).Type.Name);
        Assert.Contains("Rac1GuidedWeaponCamera GuidedCamera", Script("Rac1WeaponHost.cs"), StringComparison.Ordinal);
        var begin = Method("Rac1GuidedWeaponCamera.cs", "Begin");
        var end = Method("Rac1GuidedWeaponCamera.cs", "End");
        Assert.Contains("player.Rac1RemoteControlActive = true", begin, StringComparison.Ordinal);
        Assert.Contains("bool wasCurrent = camera?.Current == true", end, StringComparison.Ordinal);
        Assert.Contains("player.Rac1RemoteControlActive = false", end, StringComparison.Ordinal);
        Assert.Contains("if (wasCurrent && GodotObject.IsInstanceValid(player.Camera))", end, StringComparison.Ordinal);
        Assert.Contains("GuidedCamera.End()", Method("Rac1WeaponHost.cs", "ResetWorld"), StringComparison.Ordinal);
        Assert.Contains("GuidedCamera.Dispose()", Method("Rac1WeaponHost.cs", "Dispose"), StringComparison.Ordinal);
        Assert.DoesNotContain("partial class OBPGame", Script("Rac1GuidedWeaponCamera.cs"), StringComparison.Ordinal);
    }
}
