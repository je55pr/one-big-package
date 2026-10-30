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
        Assert.Contains("Rac1World.Weapons.Morph.Fire(from, EquipmentForward)", arsenal, StringComparison.Ordinal);
        Assert.Contains("ports.Contact(from, end, .01f)", Script("Rac1MorphHost.cs"), StringComparison.Ordinal);
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
    public void MorphExposureChickenIdentityAndCollidersBelongToOneWorldLease()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (var name in new[] { "ArsenalChicken", "_rac1Chickens", "_rac1MorphExposure", "MorphArsenalEnemy" })
            Assert.Empty(root.GetMembers(name));

        var weapon = compilation.GetTypeByMetadataName("OBP.Game.Rac1WeaponHost")!;
        Assert.Equal("Rac1MorphHost",
            Assert.Single(weapon.GetMembers("Morph").OfType<IPropertySymbol>()).Type.Name);

        string host = Script("Rac1MorphHost.cs");
        string parent = Script("Rac1WeaponHost.cs");
        string world = Script("OBPGame.cs");
        string arsenal = Script("OBPGame.Rac1Arsenal.cs");
        Assert.Contains("Morph = new(morph)", parent, StringComparison.Ordinal);
        Assert.Contains("Morph.Reset()", parent, StringComparison.Ordinal);
        Assert.Contains("Morph.Dispose()", parent, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Morph.TickChickens()", arsenal, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Morph.TickExposure()", arsenal, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Morph.Fire(from, EquipmentForward)", arsenal, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Morph.Reset()", arsenal, StringComparison.Ordinal);
        Assert.Contains("!Rac1World.Weapons.Morph.Contains(node.Source.InstanceIndex)", arsenal, StringComparison.Ordinal);
        Assert.Contains("new Rac1MorphPorts(", world, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac1Chickens", world, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", host, StringComparison.Ordinal);
    }

    [Fact]
    public void MorphRetainsNativeExposureChickenMotionAndColliderRestoration()
    {
        string host = Script("Rac1MorphHost.cs");
        Assert.Contains("if (--_exposure[key] <= 0) _exposure.Remove(key)", host, StringComparison.Ordinal);
        Assert.Contains("_exposure.GetValueOrDefault(target.Source.InstanceIndex) + 2", host, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalHostPolicy.MorphExposureTicks", host, StringComparison.Ordinal);
        Assert.Contains("ports.FindTarget(from, Rac1ArsenalHostPolicy.MorphRange, .5f)", host, StringComparison.Ordinal);
        Assert.Contains("ports.Contact(from, end, .01f)", host, StringComparison.Ordinal);
        Assert.Contains("ports.CreateView(270)", host, StringComparison.Ordinal);
        Assert.Contains("view.Scale = Vector3.One / target.Root.Scale", host, StringComparison.Ordinal);
        Assert.Contains("collider.Shape = new CapsuleShape3D { Radius = .24f, Height = .65f }", host, StringComparison.Ordinal);
        Assert.Contains("collider.Position = Vector3.Up * .325f", host, StringComparison.Ordinal);
        Assert.Contains("chicken.Age % 90 == 1", host, StringComparison.Ordinal);
        Assert.Contains("ports.SignedRandom(0, MathF.PI)", host, StringComparison.Ordinal);
        Assert.Contains("ports.Ground(body.GlobalPosition + chicken.Direction * .5f, .4f, .8f)", host, StringComparison.Ordinal);
        Assert.Contains("body.MoveAndSlide()", host, StringComparison.Ordinal);
        Assert.Contains("mesh.Visible = mesh.MaterialOverride is not null", host, StringComparison.Ordinal);
        Assert.Contains("collider.Shape = chicken.OriginalShape", host, StringComparison.Ordinal);
        Assert.Contains("collider.Position = chicken.OriginalShapePosition", host, StringComparison.Ordinal);
        Assert.Contains("_chickens.Clear()", host, StringComparison.Ordinal);
        Assert.Contains("_exposure.Clear()", host, StringComparison.Ordinal);
        Assert.Contains("Reset()", Method("Rac1MorphHost.cs", "Dispose"), StringComparison.Ordinal);
    }

    [Fact]
    public void MorphConsumersBorrowReadOnlyIdentitiesViewsAndDiagnostics()
    {
        string root = Script("OBPGame.cs");
        string wrench = Script("OBPGame.Rac1Wrench.cs");
        string host = Script("Rac1MorphHost.cs");
        Assert.Contains("MorphedMobies: () => Rac1World.Weapons.Morph.ActiveKeys()", root, StringComparison.Ordinal);
        Assert.Contains(".Concat(Rac1World.Weapons.SuckCannon.ChickenCargoKeys).Distinct().ToArray()", root, StringComparison.Ordinal);
        Assert.Contains("MorphEnemy: node => Rac1World.Weapons.Morph.Morph(node)", root, StringComparison.Ordinal);
        Assert.Contains("id => Rac1World.Weapons.Morph.ViewFor(id)", root, StringComparison.Ordinal);
        Assert.Contains("id => Rac1World.Weapons.Morph.Contains(id)", root, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Morph.Project((enemy, view)", root, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Morph.ViewFor(target.Source.InstanceIndex)", wrench, StringComparison.Ordinal);
        Assert.Contains("internal IReadOnlyList<Rac1MobyRuntimeKey> ActiveKeys()", host, StringComparison.Ordinal);
        Assert.Contains("!chicken.Enemy.RewardEmitted", host, StringComparison.Ordinal);
        Assert.Contains("internal Rac1EquipmentView? ViewFor(int instanceIndex)", host, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<int, Chicken> _chickens", root, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<int, int> _exposure", root, StringComparison.Ordinal);
    }

    [Fact]
    public void TeslaTargetLockAndDamageCadenceBelongToWorldWeaponLease()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        Assert.Empty(root.GetMembers("_rac1TeslaSoundTargets"));

        var weapon = compilation.GetTypeByMetadataName("OBP.Game.Rac1WeaponHost")!;
        Assert.Equal("Rac1TeslaHost",
            Assert.Single(weapon.GetMembers("Tesla").OfType<IPropertySymbol>()).Type.Name);

        string family = Script("Rac1TeslaHost.cs");
        string parent = Script("Rac1WeaponHost.cs");
        string arsenal = Script("OBPGame.Rac1Arsenal.cs");
        string rootComposition = Script("OBPGame.cs");

        Assert.Contains("Tesla = new(tesla, Contacts)", parent, StringComparison.Ordinal);
        Assert.Contains("Tesla.Reset()", parent, StringComparison.Ordinal);
        Assert.Contains("Tesla.Dispose()", parent, StringComparison.Ordinal);
        Assert.Contains("new Rac1TeslaPorts(", rootComposition, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Tesla.Release()", arsenal, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Tesla.Reset()", arsenal, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Tesla.Tick(from, EquipmentForward)", arsenal, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", family, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac1TeslaSoundTargets", arsenal, StringComparison.Ordinal);
    }

    [Fact]
    public void TeslaMaintainsNativeTargetLockAudioDamageAndShortBeam()
    {
        string family = Script("Rac1TeslaHost.cs");
        string arsenal = Script("OBPGame.Rac1Arsenal.cs");
        Assert.Contains("Rac1ArsenalRules.TeslaRange, .5f, null", family, StringComparison.Ordinal);
        Assert.Contains("[first.Source.InstanceIndex]", family, StringComparison.Ordinal);
        Assert.Contains("_locked.RemoveWhere(id => targets.All", family, StringComparison.Ordinal);
        Assert.Contains("if (_locked.Add(target.Source.InstanceIndex))", family, StringComparison.Ordinal);
        Assert.Contains("ports.Sound(177, 1, from)", family, StringComparison.Ordinal);
        Assert.Contains("ports.WorldTick() % 10 == 0", family, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalRules.TeslaDamage", family, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalRules.TeslaDamageFlags", family, StringComparison.Ordinal);
        Assert.Contains("contacts.Contact(from, end, .01f)", family, StringComparison.Ordinal);
        Assert.Contains("ports.DrawBeam(0, from, hit?.Point ?? end)", family, StringComparison.Ordinal);
        Assert.Contains("internal void Release() => _locked.Clear()", family, StringComparison.Ordinal);
        Assert.Contains("internal void Reset() => _locked.Clear()", family, StringComparison.Ordinal);
        Assert.Contains("_rac1TeslaPresentation.Advance()", arsenal, StringComparison.Ordinal);
        Assert.Contains("if (weapon == Rac1WeaponId.TeslaClaw && _rac1Weapons.GetAmmo(19) <= 0) return", arsenal, StringComparison.Ordinal);
        Assert.Contains("DrawArsenalBeam(slot, from, to, new(.3f, .6f, 1))", Script("OBPGame.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TaunterAttractionAndMineContactBelongToWorldWeaponLease()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        Assert.Empty(root.GetMembers("_rac1Taunts"));

        var weapon = compilation.GetTypeByMetadataName("OBP.Game.Rac1WeaponHost")!;
        Assert.Equal("Rac1TaunterHost",
            Assert.Single(weapon.GetMembers("Taunter").OfType<IPropertySymbol>()).Type.Name);

        string family = Script("Rac1TaunterHost.cs");
        string parent = Script("Rac1WeaponHost.cs");
        string rootArsenal = Script("OBPGame.Rac1Arsenal.cs");
        string deployable = Script("Rac1ArsenalDeployableHost.cs");
        string application = Script("OBPGame.cs");

        Assert.Contains("Taunter = new(taunter, Contacts)", parent, StringComparison.Ordinal);
        Assert.Contains("Deployables = new(deployables, NextArsenalId, Taunter)", parent, StringComparison.Ordinal);
        Assert.Contains("Taunter.Reset()", parent, StringComparison.Ordinal);
        Assert.Contains("Taunter.Dispose()", parent, StringComparison.Ordinal);
        Assert.Contains("taunter.IsTaunted(enemy.Node.Source.InstanceIndex)", deployable, StringComparison.Ordinal);
        Assert.DoesNotContain("ports.IsTaunted(", deployable, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Taunter.Tick(from, EquipmentForward, _player!.GlobalPosition)", rootArsenal, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Taunter.Reset()", rootArsenal, StringComparison.Ordinal);
        Assert.Contains("new Rac1TaunterPorts(", application, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac1Taunts", application, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", family, StringComparison.Ordinal);
    }

    [Fact]
    public void TaunterPreservesSourceNativeConeDurationAndHitCadence()
    {
        string family = Script("Rac1TaunterHost.cs");
        string application = Script("OBPGame.cs");
        Assert.Contains("_taunts.GetValueOrDefault(instanceIndex) > ports.WorldTick()", family, StringComparison.Ordinal);
        Assert.Contains("_taunts[node.Source.InstanceIndex] = tick + 60", family, StringComparison.Ordinal);
        Assert.Contains("tick % 8 == 0", family, StringComparison.Ordinal);
        Assert.Contains("tick % 30 == 0", family, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalRules.TaunterRange * Rac1ArsenalRules.TaunterRange", family, StringComparison.Ordinal);
        Assert.Contains("MathF.Cos(Rac1ArsenalRules.TaunterHalfAngle)", family, StringComparison.Ordinal);
        Assert.Contains("contacts.HasLineOfSight(from, node.Root.GlobalPosition + Vector3.Up * .4f)", family, StringComparison.Ordinal);
        Assert.Contains("mine.MarkTaunted()", family, StringComparison.Ordinal);
        Assert.Contains("Rac1ClassIds.WoodenCrate or Rac1ClassIds.NanotechCrate or 505 or Rac1ClassIds.AmmoCrate", family, StringComparison.Ordinal);
        Assert.Contains("new(crate.Source.NativeClassId, crate.Source.InstanceIndex), 1, 0x10000)", family, StringComparison.Ordinal);
        Assert.Contains("shot.Weapon == Rac1WeaponId.MineGlove", application, StringComparison.Ordinal);
        Assert.Contains("() => shot.Taunted = true", application, StringComparison.Ordinal);
        Assert.Contains("_rac1WeaponMotes.Add(new(ring, forward * .25f))", application, StringComparison.Ordinal);
        Assert.Contains("internal void Reset() => _taunts.Clear()", family, StringComparison.Ordinal);
        Assert.Contains("if (_disposed) return;", family, StringComparison.Ordinal);
    }

    [Fact]
    public void WalloperSwingAndVictimAdmissionBelongToTheWorldLease()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string member in new[] { "_rac1WalloperAge", "_rac1WalloperVictims", "StepRac1Walloper" })
            Assert.Empty(root.GetMembers(member));

        var weapon = compilation.GetTypeByMetadataName("OBP.Game.Rac1WeaponHost")!;
        Assert.Equal("Rac1WalloperHost",
            Assert.Single(weapon.GetMembers("Walloper").OfType<IPropertySymbol>()).Type.Name);

        string host = Script("Rac1WalloperHost.cs");
        string parent = Script("Rac1WeaponHost.cs");
        string world = Script("OBPGame.cs");
        string arsenal = Script("OBPGame.Rac1Arsenal.cs");
        Assert.Contains("Walloper = new(walloper, Contacts)", parent, StringComparison.Ordinal);
        Assert.Contains("Walloper.Reset()", parent, StringComparison.Ordinal);
        Assert.Contains("Walloper.Dispose()", parent, StringComparison.Ordinal);
        Assert.Contains("new Rac1WalloperPorts(", world, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Walloper.Begin()", arsenal, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Walloper.Tick()", arsenal, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", host, StringComparison.Ordinal);
    }

    [Fact]
    public void WalloperPreservesNativeLungeContactTimingAndBodyPoseAge()
    {
        string host = Script("Rac1WalloperHost.cs");
        string animation = Script("OBPGame.WorldEntry.cs");
        Assert.Contains("Rac1ArsenalRules.WalloperDurationTicks", host, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalRules.WalloperLungeFrom", host, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalRules.WalloperLungeTo", host, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalRules.WalloperContactTo", host, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalRules.WalloperLungeSpeed", host, StringComparison.Ordinal);
        Assert.Contains("age == 10", host, StringComparison.Ordinal);
        Assert.Contains("age == 16", host, StringComparison.Ordinal);
        Assert.Contains("new(node.Source.NativeClassId, node.Source.InstanceIndex), 3, 0x30000)", host, StringComparison.Ordinal);
        Assert.Contains("_victims.Add(node.Source.InstanceIndex)", host, StringComparison.Ordinal);
        Assert.Contains("contacts.Geometry", host, StringComparison.Ordinal);
        Assert.Contains("contacts.HasLineOfSight(centre, bounds.GetCenter())", host, StringComparison.Ordinal);
        Assert.Contains("internal int Age => _disposed ? 0 : _age", host, StringComparison.Ordinal);
        Assert.Contains("Rac1Gameplay?.Weapons.Walloper.Age ?? 0", animation, StringComparison.Ordinal);
        Assert.Contains("walloper ? walloperAge / 60d", animation, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac1WalloperAge", animation, StringComparison.Ordinal);
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
        Assert.Contains("Deployables = new(deployables, NextArsenalId, Taunter)", weapon, StringComparison.Ordinal);
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
        Assert.Contains("taunter.IsTaunted(enemy.Node.Source.InstanceIndex)", host, StringComparison.Ordinal);
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
        Assert.Contains("Rac1ArsenalViewPose.Orient(chicken.View, chicken.Direction)",
            Script("Rac1MorphHost.cs"), StringComparison.Ordinal);
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
