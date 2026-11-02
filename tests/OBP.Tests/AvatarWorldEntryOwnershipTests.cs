namespace OBP.Tests;

public sealed class AvatarWorldEntryOwnershipTests
{
    [Fact]
    public void WorldEntryDelegatesSourceValidationAndStagingToTheExistingAvatarOwner()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.WorldEntry.cs"));
        string avatar = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/PlayerAvatarPresentationHost.cs"));

        int start = root.IndexOf("private void PrepareSelectedPlayerAssets()", StringComparison.Ordinal);
        int end = root.IndexOf("private PlayerSessionConfiguration SelectedPlayerConfiguration", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        string preparation = root[start..end];

        Assert.Contains("EnsurePlayerSession();", preparation, StringComparison.Ordinal);
        Assert.Contains("RefreshPlayerSourceAvailability();", preparation, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.ResolvePresentationProfiles();", preparation, StringComparison.Ordinal);
        Assert.Contains("_avatarPresentation.PrepareSelected(", preparation, StringComparison.Ordinal);
        Assert.Contains("_playerSession!.Capture().Configuration.Avatar", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("new FileInfo", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("FileRandomAccessReader", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareSelectedBank(", preparation, StringComparison.Ordinal);

        int verify = avatar.IndexOf("internal void PrepareSelected(PlayerAvatarIdentity identity", StringComparison.Ordinal);
        int existing = avatar.IndexOf("internal void Prepare(PlayerAvatarIdentity identity", verify, StringComparison.Ordinal);
        Assert.True(verify >= 0 && existing > verify);
        string stage = avatar[verify..existing];
        Assert.True(stage.IndexOf("physicsTicks != nativeTicks", StringComparison.Ordinal)
            < stage.IndexOf("identity.SourceGame == ObpSourceGame.Synthetic", StringComparison.Ordinal));
        Assert.Contains("session.Availability(identity.SourceGame, identity.BuildId, true, true)", stage, StringComparison.Ordinal);
        Assert.Contains("file.Length != source.SizeBytes", stage, StringComparison.Ordinal);
        Assert.Contains("library.Probe(reader)", stage, StringComparison.Ordinal);
        Assert.Contains("Prepare(identity, source.Path", stage, StringComparison.Ordinal);
        Assert.Contains("equipment.PrepareSelectedBank(source.Path, ordinary, PreparedSourceChanged)", stage, StringComparison.Ordinal);
        Assert.True(stage.IndexOf("Prepare(identity, source.Path", StringComparison.Ordinal)
            < stage.IndexOf("equipment.PrepareSelectedBank(", StringComparison.Ordinal));
    }

    [Fact]
    public void EquipmentPoseSelectionBelongsToTheExistingBinderInsteadOfTheWorldRoot()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.WorldEntry.cs"));
        string binder = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/PlayerEquipmentPresentationBinder.cs"));

        int start = root.IndexOf("private void StepPlayerAvatarPhysics(", StringComparison.Ordinal);
        int end = root.IndexOf("private void ResetPlayerAvatarPhysics()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        string tick = root[start..end];

        Assert.Contains("Rac1CombatInput.StepInput()", tick, StringComparison.Ordinal);
        Assert.Contains("_avatarPresentation.StepNative(state, input)", tick, StringComparison.Ordinal);
        Assert.Contains("_equipmentPresentation.ApplyAvatarEquipmentPose(", tick, StringComparison.Ordinal);
        Assert.Contains("_avatarPresentation.SampleClock()", tick, StringComparison.Ordinal);
        Assert.DoesNotContain("bool holdGun", tick, StringComparison.Ordinal);
        Assert.DoesNotContain("bool fusionPose", tick, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplySourceEquipment(", tick, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyEquipmentPose(", tick, StringComparison.Ordinal);

        int compose = binder.IndexOf("internal void ApplyAvatarEquipmentPose(", StringComparison.Ordinal);
        int following = binder.IndexOf("internal void RefreshRac1HandVisibility(", compose, StringComparison.Ordinal);
        Assert.True(compose >= 0 && following > compose);
        string pose = binder[compose..following];

        Assert.Contains("Rac1WeaponProfiles.IsAdditional(hand.Inventory.Equipped)", pose, StringComparison.Ordinal);
        Assert.Contains("IsRac1HandEquipmentVisible", pose, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalRules.WalloperSequence", pose, StringComparison.Ordinal);
        Assert.Contains("Rac1ArsenalRules.MovingBodySequence", pose, StringComparison.Ordinal);
        Assert.Contains("hand.Fusion.Hand.IsHolding(hand.Fusion.Selected)", pose, StringComparison.Ordinal);
        Assert.Contains("ApplySourceEquipment(sourceAnimation", pose, StringComparison.Ordinal);
        Assert.Contains("ApplyEquipmentPose(equipmentPose", pose, StringComparison.Ordinal);
        Assert.Contains("walloper ? walloperAge / 60d : useAge / 60d", pose, StringComparison.Ordinal);
        Assert.True(pose.IndexOf("ApplySourceEquipment(", StringComparison.Ordinal)
            < pose.IndexOf("controller.ClearSourceEquipment()", StringComparison.Ordinal));
    }
}
