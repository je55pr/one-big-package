using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

/// <summary>
/// First #367/#368 ownership seam. Retail-backed weapon and detector scenarios
/// remain responsible for functional parity; these source-free checks prevent
/// the engine root from reclaiming world-gadget and guided-camera state.
/// </summary>
public sealed partial class Rac1WeaponWorldOwnershipTests
{
    private static string Script(string file) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", file));

    private static string Method(string file, string name) =>
        HostOwnershipAudit.Host.Value.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/" + file)
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == name).ToString();

    [Fact]
    public void WeaponDiagnosticCollectorsRejectRetiredGameplayBeforeEnumeratingProjectiles()
    {
        string root = Script("OBPGame.cs");
        int start = root.IndexOf("private GodotRac1AdditionalWeaponDiagnosticFixture CreateRac1AdditionalWeaponDiagnosticFixture", StringComparison.Ordinal);
        int end = root.IndexOf("private GodotRac1ArsenalDiagnosticFixture CreateRac1ArsenalDiagnosticFixture", StringComparison.Ordinal);
        string fixture = root[start..end];
        Assert.Contains("var scope = new GodotRac1DiagnosticWorldScope(worlds)", fixture, StringComparison.Ordinal);
        Assert.Equal(2, fixture.Split("scope.RequireCurrent()", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, fixture.Split("?? throw new DiagnosticUnavailableException", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, fixture.Split("new GodotWeaponDiagnosticWitness<", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("new GodotWeaponDiagnosticWitness<Rac1EquipmentShotDiagnostic>(new(worlds)", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1World.Weapons.Projectiles.Shots.Select", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1World.Weapons.Projectiles.DoomAgents.Select", fixture, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedWeaponWorldClockAndOrderedEffectsBelongToTheWorldLease()
    {
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        Assert.Empty(root.GetMembers("_rac1WeaponWorldTick"));
        string host = Script("Rac1WeaponHost.cs");
        string composition = Script("OBPGame.cs");
        string rootWorld = Script("Rac1WeaponEffectsHost.cs");
        string rootUse = Script("Rac1WeaponUsePresentationHost.cs");
        Assert.Contains("private ulong _worldTick;", host, StringComparison.Ordinal);
        Assert.Contains("internal ulong WorldTick => _worldTick;", host, StringComparison.Ordinal);
        Assert.Contains("ResetWorldTick();", Method("Rac1WeaponHost.cs", "ResetWorld"), StringComparison.Ordinal);
        Assert.Contains("world.Weapons.ResetWorldTick()", rootUse, StringComparison.Ordinal);
        Assert.Contains("world.Weapons.StepWorld(ports.PyroEmitting())", rootWorld, StringComparison.Ordinal);
        Assert.Contains("Weapons.WorldTick ?? 0", composition, StringComparison.Ordinal);
        Assert.Contains("Weapons.ResetWorldTick()", composition, StringComparison.Ordinal);
        string step = Method("Rac1WeaponHost.cs", "StepWorld");
        string[] order =
        [
            "_worldTick++", "Arsenal.Tick()", "Flames.Emit(pyroEmitting)",
            "Projectiles.StepMotes()", "Flames.StepContacts()", "SuckCannon.Step()",
            "Projectiles.StepShots()", "Projectiles.StepDoomAgents()", "Projectiles.StepFlashes()",
        ];
        int previous = -1;
        foreach (string call in order)
        {
            int position = step.IndexOf(call, StringComparison.Ordinal);
            Assert.True(position > previous, "Changed weapon world frame order: " + call);
            previous = position;
        }
    }


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
    public void RetiredContactLeaseRejectsLineOfSightBeforeBorrowingPlayerOrRayState()
    {
        // The previous combined guard returned true for a disposed host.
        // An in-flight callback could therefore treat the retired scene as
        // unobstructed. This fail-closed check must precede all borrowed ports.
        string sight = Method("Rac1WeaponContactHost.cs", "HasLineOfSight");
        int disposalGuard = sight.IndexOf("if (_disposed) return false;", StringComparison.Ordinal);
        int playerBorrow = sight.IndexOf("physics.Player()", StringComparison.Ordinal);
        Assert.True(disposalGuard >= 0 && playerBorrow > disposalGuard,
            "Disposed contact leases must reject LOS before borrowing the old player.");
        Assert.DoesNotContain("if (_disposed ||", sight, StringComparison.Ordinal);
        Assert.Contains("if (player is null || from.DistanceSquaredTo(to) < .0001f) return true;", sight,
            StringComparison.Ordinal); // Preserve live-world degenerate/no-player policy.
        Assert.Contains("query.Exclude = RayExclusions()", sight, StringComparison.Ordinal);
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

        string arsenal = Script("Rac1ArsenalUseHost.cs");
        string projectile = Script("Rac1BombProjectileHost.cs");
        string flames = Script("Rac1PyrocitorFlameHost.cs");
        Assert.Contains("weapons.Morph.Fire(from, ports.EquipmentForward())", arsenal, StringComparison.Ordinal);
        Assert.Contains("ports.Contact(from, end, .01f)", Script("Rac1MorphHost.cs"), StringComparison.Ordinal);
        Assert.Contains("Spawn(shot)", projectile, StringComparison.Ordinal);
        Assert.Contains("_weapons.Contacts.HasLineOfSight(", Script("Rac1BombProjectileHost.cs"), StringComparison.Ordinal);
        Assert.Contains("contacts.RayExclusions()", flames, StringComparison.Ordinal);
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
        string arsenal = Script("Rac1ArsenalUseHost.cs");
        Assert.Contains("Morph = new(arsenal.Morph, Beams)", parent, StringComparison.Ordinal);
        Assert.Contains("Morph.Reset()", parent, StringComparison.Ordinal);
        Assert.Contains("Morph.Dispose()", parent, StringComparison.Ordinal);
        Assert.Contains("weapons.Morph.TickChickens()", arsenal, StringComparison.Ordinal);
        Assert.Contains("weapons.Morph.TickExposure()", arsenal, StringComparison.Ordinal);
        Assert.Contains("weapons.Morph.Fire(from, ports.EquipmentForward())", arsenal, StringComparison.Ordinal);
        Assert.Contains("weapons.Morph.Reset()", arsenal, StringComparison.Ordinal);
        Assert.Contains("!targets.IsMorphed(node.Source.InstanceIndex)",
            Script("Rac1WeaponContactHost.cs"), StringComparison.Ordinal);
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
        Assert.Contains("Math.DivRem(chicken.Age, 90, out int directionPhase)", host, StringComparison.Ordinal);
        Assert.Contains("chicken.Age >= 0 && directionPhase == 1", host, StringComparison.Ordinal);
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
        string host = Script("Rac1MorphHost.cs");
        Assert.Contains("MorphedMobies: () => Rac1World.Weapons.Morph.ActiveKeys()", root, StringComparison.Ordinal);
        Assert.Contains(".Concat(Rac1World.Weapons.SuckCannon.ChickenCargoKeys).Distinct().ToArray()", root, StringComparison.Ordinal);
        Assert.Contains("MorphEnemy: node => Rac1World.Weapons.Morph.Morph(node)", root, StringComparison.Ordinal);
        Assert.Contains("id => Rac1World.Weapons.Morph.ViewFor(id)", root, StringComparison.Ordinal);
        Assert.Contains("id => Rac1World.Weapons.Morph.Contains(id)", root, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Morph.Project((enemy, view)", root, StringComparison.Ordinal);
        Assert.Contains("contactPorts.MorphView(target.Source.InstanceIndex)", Script("Rac1WrenchHost.cs"), StringComparison.Ordinal);
        Assert.Contains("_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.OnPhysicsUpdated(frame)", root, StringComparison.Ordinal);
        Assert.Contains("internal IReadOnlyList<Rac1MobyRuntimeKey> ActiveKeys()", host, StringComparison.Ordinal);
        Assert.Contains("!chicken.Enemy.RewardEmitted", host, StringComparison.Ordinal);
        Assert.Contains("internal Rac1EquipmentView? ViewFor(int instanceIndex)", host, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<int, Chicken> _chickens", root, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<int, int> _exposure", root, StringComparison.Ordinal);
    }

    [Fact]
    public void BeamNodesMaterialsAndVisualRandomBelongToWorldWeaponLease()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string member in new[]
        {
            "_rac1ArsenalBeams", "_rac1TeslaPresentation", "_rac1TeslaMaterials",
            "_rac1TeslaEffectRandom", "_rac1PlainBeamMaterial",
            "DrawArsenalBeam", "DrawRac1TeslaRibbons"
        })
            Assert.Empty(root.GetMembers(member));

        var weapon = compilation.GetTypeByMetadataName("OBP.Game.Rac1WeaponHost")!;
        Assert.Equal("Rac1ArsenalBeamPresenter",
            Assert.Single(weapon.GetMembers("Beams").OfType<IPropertySymbol>()).Type.Name);

        string presenter = Script("Rac1ArsenalBeamPresenter.cs");
        string parent = Script("Rac1WeaponHost.cs");
        string module = Script("Rac1WorldModule.cs");
        string arsenal = Script("Rac1ArsenalUseHost.cs");
        string application = Script("OBPGame.cs");

        Assert.Contains("Beams = new(sceneRoot, arsenal.Beams)", parent, StringComparison.Ordinal);
        Assert.Contains("Beams.Reset()", parent, StringComparison.Ordinal);
        Assert.Contains("Beams.Dispose()", parent, StringComparison.Ordinal);
        Assert.Contains("Weapons = new(scene.Root, detector", module, StringComparison.Ordinal);
        Assert.Contains("new Rac1ArsenalBeamPorts(", application, StringComparison.Ordinal);
        Assert.Contains("weapons.Beams.Reset()", arsenal, StringComparison.Ordinal);
        Assert.Contains("weapons.Beams.BeginFrame()", arsenal, StringComparison.Ordinal);
        Assert.Contains("weapons.Beams.AdvanceTesla()", arsenal, StringComparison.Ordinal);
        Assert.Contains("Rac1TeslaPresentation _teslaPresentation = new()", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", presenter, StringComparison.Ordinal);
    }

    [Fact]
    public void BeamFamiliesShareWorldPresenterWithoutRootDrawCallbacks()
    {
        string tesla = Script("Rac1TeslaHost.cs");
        string morph = Script("Rac1MorphHost.cs");
        string parent = Script("Rac1WeaponHost.cs");
        string application = Script("OBPGame.cs");
        string presenter = Script("Rac1ArsenalBeamPresenter.cs");

        Assert.Contains("Tesla = new(arsenal.Tesla, Contacts, Beams)", parent, StringComparison.Ordinal);
        Assert.Contains("Morph = new(arsenal.Morph, Beams)", parent, StringComparison.Ordinal);
        Assert.Contains("beams.DrawTesla(", tesla, StringComparison.Ordinal);
        Assert.Contains("beams.DrawMorph(", morph, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawBeam", tesla, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawBeam", morph, StringComparison.Ordinal);
        Assert.Contains("Draw(index, from, to, new(.3f, .6f, 1))", presenter, StringComparison.Ordinal);
        Assert.Contains("Draw(index, from, to, new(.4f, 1, .25f))", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawArsenalBeam", application, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Beams.VisibleSlots()", application, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Beams.ShaderSlots()", application, StringComparison.Ordinal);
    }

    [Fact]
    public void BeamPresenterPreservesNativeRibbonsMeshGeometryAndSeparateRandomStreams()
    {
        string presenter = Script("Rac1ArsenalBeamPresenter.cs");
        Assert.Contains("ports.Equipped() == Rac1WeaponId.TeslaClaw && ports.Effects() is { } effects", presenter, StringComparison.Ordinal);
        Assert.Contains("Rac1PickupRandom.FromSeed(unchecked((uint)(ports.FixedSeed() ?? System.Environment.TickCount)))", presenter, StringComparison.Ordinal);
        Assert.Contains("ports.SignedRandom(0, .13f)", presenter, StringComparison.Ordinal);
        Assert.Contains("_teslaPresentation.Advance()", presenter, StringComparison.Ordinal);
        Assert.Contains("_teslaPresentation.ProjectTargetLine(", presenter, StringComparison.Ordinal);
        Assert.Contains("Rac1LootPresentation.ToEffectNative(from)", presenter, StringComparison.Ordinal);
        Assert.Contains("ports.Camera()?.GlobalPosition ?? from + Vector3.Up * 5", presenter, StringComparison.Ordinal);
        Assert.Contains("render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled", presenter, StringComparison.Ordinal);
        Assert.Contains("material.SetShaderParameter(\"art\"", presenter, StringComparison.Ordinal);
        Assert.Contains("Vector3 extension = (pb - pa) * ribbon.EndExtension", presenter, StringComparison.Ordinal);
        Assert.Contains("float a = _teslaPresentation.TexturePhase, b = a + 1", presenter, StringComparison.Ordinal);
        Assert.Contains("EffectMeshSceneView.Upload(mesh, new(", presenter, StringComparison.Ordinal);
        Assert.Contains("mesh.ClearSurfaces()", presenter, StringComparison.Ordinal);
        Assert.Contains("beam.Visible = false", presenter, StringComparison.Ordinal);
        Assert.Contains("GodotObject.IsInstanceValid(beam)", presenter, StringComparison.Ordinal);
        Assert.Contains("_teslaMaterials.Clear()", presenter, StringComparison.Ordinal);
        Assert.Contains("_teslaEffectRandom = null", presenter, StringComparison.Ordinal);
        Assert.Contains("Reset()", Method("Rac1ArsenalBeamPresenter.cs", "Dispose"), StringComparison.Ordinal);
    }

}
