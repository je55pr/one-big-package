using Microsoft.CodeAnalysis;

namespace OBP.Tests;

/// <summary>
/// #368: the recovered wrench throw, attack-generation, per-swing victim set
/// and six-segment host sweep must share a source-world lease. Native damage
/// remains with the existing crate, hostile and frog authorities.
/// </summary>
public sealed class Rac1WrenchWorldOwnershipTests
{
    private static string Script(string file) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", file));

    [Fact]
    public void WrenchStateAndContactBelongToTheWorldLeaseInsteadOfRoot()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (var name in new[]
        {
            "_rac1WrenchThrow", "_rac1WrenchHitTargets", "_rac1WrenchHostGeneration",
            "_rac1PreviousWrenchHand", "_rac1PreviousWrenchTip",
            "ResolveRac1AnimatedWrenchContacts", "WrenchSegmentIntersects",
            "WrenchWorldPosition", "ToWrenchNative"
        })
            Assert.Empty(root.GetMembers(name));

        var world = compilation.GetTypeByMetadataName("OBP.Game.Rac1WorldModule")!;
        Assert.Equal("Rac1WrenchHost",
            Assert.Single(world.GetMembers("Wrench").OfType<IPropertySymbol>()).Type.Name);

        string host = Script("Rac1WrenchHost.cs");
        string module = Script("Rac1WorldModule.cs");
        string entry = Script("OBPGame.Rac1Wrench.cs");

        Assert.Contains("Wrench = new(wrenchFrame, wrenchContact)", module, StringComparison.Ordinal);
        Assert.Contains("Wrench.Reset()", module, StringComparison.Ordinal);
        Assert.Contains("Wrench.Dispose()", module, StringComparison.Ordinal);
        Assert.Contains("if (_disposed) return;", host, StringComparison.Ordinal);
        Assert.DoesNotContain("_reset.Wrench()", module, StringComparison.Ordinal);
        Assert.DoesNotContain("Action Wrench", Script("Rac1WorldPorts.cs"), StringComparison.Ordinal);
        Assert.Contains("gameplay.Wrench.Reset()", entry, StringComparison.Ordinal);
        Assert.Contains("GodotRac1PlayerAudioPlayback.ResetWrench(_playerAudio)", entry, StringComparison.Ordinal);
        Assert.Contains("if (_player is not null) _player.Rac1WrenchThrown = false", entry, StringComparison.Ordinal);
        Assert.Contains("_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.OnPhysicsUpdated(frame)", entry, StringComparison.Ordinal);
        Assert.Contains("Rac1WrenchThrowSession _throw = new()", host, StringComparison.Ordinal);
        Assert.Contains("HashSet<Rac1MobyRuntimeKey> _victims = []", host, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", host, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/Rac1WrenchHost.cs.uid")));
    }

    [Fact]
    public void NativeThrowFlightAndSwingClockRetainOriginalOrdering()
    {
        string host = Script("Rac1WrenchHost.cs");
        string factory = Script("OBPGame.cs");

        Assert.Contains("framePorts.HoldWrench();", host, StringComparison.Ordinal);
        Assert.Contains("_generation = frame.Generation;", host, StringComparison.Ordinal);
        Assert.Contains("_victims.Clear();", host, StringComparison.Ordinal);
        Assert.Contains("framePorts.ApplyNativeAttack(frame.ProfileId);", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WrenchAttackProfile.Profiles[profileId]", factory, StringComparison.Ordinal);
        Assert.Contains("CurrentNativeState is not (0x13 or 0x14 or 0x15)", factory, StringComparison.Ordinal);
        Assert.Contains("_throw.Begin(ToWrenchNative(hand)", host, StringComparison.Ordinal);
        Assert.Contains("if (_throw.NativeState == 10)", host, StringComparison.Ordinal);
        Assert.Contains("query.Exclude = new global::Godot.Collections.Array<Rid> { player.GetRid() }", host, StringComparison.Ordinal);
        Assert.Contains("_throw.Step(ToWrenchNative(hand), obstruction)", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WrenchThrowSession.CollisionRadius, damage: 1", host, StringComparison.Ordinal);
        Assert.Contains("_previousHand ?? hand, _previousTip ?? tip, hand, tip, .05f, frame.Damage", host, StringComparison.Ordinal);
        Assert.Contains("framePorts.WorldContactAudio(hit)", host, StringComparison.Ordinal);
        Assert.Contains("framePorts.StepAudio(frame, _throw, hand, WorldPosition)", host, StringComparison.Ordinal);
        Assert.Contains("player.Rac1WrenchThrown = _throw.Active", host, StringComparison.Ordinal);
        Assert.Contains("framePorts.RefreshVisibility()", host, StringComparison.Ordinal);
        Assert.Contains("framePorts.ResetAudio()", host, StringComparison.Ordinal);
    }

    [Fact]
    public void FourPointSweepCoalescesVictimsAndBorrowsDamageAuthority()
    {
        string host = Script("Rac1WrenchHost.cs");
        string factory = Script("OBPGame.cs");

        Assert.Contains("Rac1WrenchContactPath.AnimatedSweepHostGeometry", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WrenchAttackProfile.NativeDamageFlags", host, StringComparison.Ordinal);
        Assert.Contains("contactPorts.OtherContacts(previousHand, previousTip, hand, tip, radius, damage, result)", host, StringComparison.Ordinal);
        Assert.Contains("if (_victims.Contains(key)", host, StringComparison.Ordinal);
        Assert.Contains("contactPorts.Eligible(target)", host, StringComparison.Ordinal);
        Assert.Contains("contactPorts.MorphView(target.Source.InstanceIndex)", host, StringComparison.Ordinal);
        foreach (var segment in new[]
        {
            "previousHand, previousTip", "hand, tip", "previousHand, hand",
            "previousTip, tip", "previousHand, tip", "previousTip, hand"
        })
            Assert.Contains($"WrenchSegmentIntersects(bounds, {segment})", host, StringComparison.Ordinal);
        Assert.Contains("ActorContactGeometry.WrenchSegmentIntersects(bounds, from, to)", host, StringComparison.Ordinal);
        Assert.Contains("bool applied = contactPorts.Strike(target, result)", host, StringComparison.Ordinal);
        Assert.Contains("_victims.Add(key)", host, StringComparison.Ordinal);
        Assert.Contains("Rac1PlayerAudioSession.WrenchVictimSoftness(target.Source.NativeClassId)", host, StringComparison.Ordinal);

        Assert.Contains("Rac1World.Enemies.Frogs.ResolveRac1FrogCarrierWrenchContacts", factory, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Enemies.Frogs.ResolveRac1QueenWrenchContacts", factory, StringComparison.Ordinal);
        Assert.Contains("ActorHost.Combat.ResolveWrench", factory, StringComparison.Ordinal);
        Assert.Contains("TryStrikeRac1Crate(target, result)", factory, StringComparison.Ordinal);
        Assert.Contains("TryStrikeRac1Hostile(target, result)", factory, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Breakables.TryBreakNovalisProp(target", factory, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingActorAndDiagnosticsBorrowTheSameWrenchOwner()
    {
        string factory = Script("OBPGame.cs");
        string host = Script("Rac1WrenchHost.cs");

        Assert.Contains("owner.Wrench.HasVictim", factory, StringComparison.Ordinal);
        Assert.Contains("owner.Wrench.AdmitVictim(key)", factory, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Wrench.ForgetVictim(key)", factory, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Wrench.ResolveContacts(previousHand, previousTip, hand, tip, radius, damage)", factory, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Wrench.Generation", factory, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Wrench.WorldPosition", factory, StringComparison.Ordinal);
        Assert.Contains("internal bool HasVictim(Rac1MobyRuntimeKey key)", host, StringComparison.Ordinal);
        Assert.Contains("internal void AdmitVictim(Rac1MobyRuntimeKey key)", host, StringComparison.Ordinal);
        Assert.Contains("internal void ForgetVictim(Rac1MobyRuntimeKey key)", host, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac1WrenchThrow", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac1WrenchHitTargets", factory, StringComparison.Ordinal);
    }
}
