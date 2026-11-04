
namespace OBP.Tests;

public sealed class AvatarWorldEntryOwnershipTests
{
    [Fact]
    public void AvatarPhysicsKeepsTheNativeUsePoseContactAudioAndClockOrder()
    {
        string owner = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/Player/PlayerAvatarPresentationHost.cs"));
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        Assert.DoesNotContain("private void StepPlayerAvatarPhysics", root, StringComparison.Ordinal);
        Assert.Contains("Weapons: () => PlayerServices.Fusion, CombatInput: () => PlayerServices.CombatInput", root, StringComparison.Ordinal);
        Assert.DoesNotContain("private void ResetPlayerAvatarPhysics", root, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", owner, StringComparison.Ordinal);

        string tick = Method(owner, "StepPlayerAvatarPhysics");
        Ordered(tick, "Controller is not Rac1PlayerAnimationPresentationController", "return;",
            "bindings.CombatInput().StepInput()", "Bombs.StepUse()", "UsePresentation.StepAdditional()",
            "bindings.Weapons().Step()", "ThrownAnimation.Select(input,", "StepNative(state, input)",
            "ApplyAvatarEquipmentPose(this,", "StepHandAttachments(controller,", "UsePresentation.StepViews(controller)",
            "bindings.Weapons().StepViews(controller, Avatar)", "GodotRac1PlayerAudioPlayback.Step(",
            "Bombs.StepPhysics()", "Effects.StepAdditionalWeaponWorld()", "Effects.StepBombEffectsPhysics()",
            "SampleClock()", "RefreshRac1HandVisibility(");
        Ordered(Method(owner, "ResetPlayerAvatarPhysics"), "bindings.Weapons().ClearTransients()",
            "bindings.Audio()?.Reset()", "Rac1WrenchHost.ResetOrFallback(", "ThrownAnimation.Stop()", "ResetClock()");
    }

    [Fact]
    public void VisualReplacementAndRetirementKeepSubscriptionsAndEquipmentInOneLifetime()
    {
        string owner = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/Player/PlayerAvatarPresentationHost.cs"));
        Ordered(Method(owner, "AttachWorldVisual"), "session.Capture().Configuration.Avatar",
            "selected.SourceGame == ObpSourceGame.Synthetic", "return;", "ClearWorldVisual(",
            "CreateWorldVisual(", "UsePresentation.AttachViews(view)", "bindings.Weapons().AttachViews(view)",
            "player.VisualRoot.ReplaceVisual(view)", "Subscribe(player,", "BindSelected(this,",
            "RefreshRac1HandVisibility(", "HideAuthoredWorldRatchetPresentation(");
        Ordered(Method(owner, "ClearWorldVisual"), "audio?.Reset()", "Retire()",
            "equipment.NativeViews.ClearViews()", "equipment.ClearBindings()");
        string retirement = Method(owner, "Retire");
        Assert.Contains("AnimationPhysicsUpdated -= _step", retirement, StringComparison.Ordinal);
        Assert.Contains("AnimationPhysicsReset -= _reset", retirement, StringComparison.Ordinal);
        Assert.Contains("_physicsPlayer = null; _step = null; _reset = null;", retirement, StringComparison.Ordinal);
    }

    private static string Method(string source, string name) => Assert.Single(
        Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>(),
        method => method.Identifier.ValueText == name).ToString();

    private static void Ordered(string source, params string[] operations)
    {
        int previous = -1;
        foreach (string operation in operations)
        {
            int position = source.IndexOf(operation, previous + 1, StringComparison.Ordinal);
            Assert.True(position > previous, $"Missing or reordered avatar operation: {operation}");
            previous = position;
        }
    }

    [Fact]
    public void WorldEntryKeepsPlayerSelectionWhileSourceOwnerVerifiesAndAvatarOwnerStages()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        string sources = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/Sources/SourceSetupCoordinator.cs"));
        string avatar = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/Player/PlayerAvatarPresentationHost.cs"));

        int start = root.IndexOf("private void PrepareSelectedPlayerAssets()", StringComparison.Ordinal);
        int end = root.IndexOf("private void AttachPlayerAvatarVisual(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        string preparation = root[start..end];
        Assert.Contains("EnsurePlayerSession();", preparation, StringComparison.Ordinal);
        Assert.Contains("RefreshPlayerSourceAvailability();", preparation, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.ResolvePresentationProfiles();", preparation, StringComparison.Ordinal);
        Assert.Contains("_avatarPresentation.PrepareSelectedWorldEntry(_playerSession!, _playerAnimationProfile!,", preparation, StringComparison.Ordinal);
        Assert.Contains("Engine.PhysicsTicksPerSecond, Sources.Setup.VerifySelectedSourceFile,", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("_playerSession.Availability(", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("identity.SourceGame", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("new FileInfo", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("FileRandomAccessReader", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareSelectedBank(", preparation, StringComparison.Ordinal);

        int admitStart = avatar.IndexOf("internal void PrepareSelectedWorldEntry(", StringComparison.Ordinal);
        int admitEnd = avatar.IndexOf("internal void PrepareSelected(PlayerAvatarIdentity identity", admitStart, StringComparison.Ordinal);
        Assert.True(admitStart >= 0 && admitEnd > admitStart);
        string admission = avatar[admitStart..admitEnd];
        Assert.Contains("session.Capture().Configuration.Avatar", admission, StringComparison.Ordinal);
        Assert.Contains("physicsTicksPerSecond != profile.NativeTicksPerSecond", admission, StringComparison.Ordinal);
        Assert.Contains("identity.SourceGame == ObpSourceGame.Synthetic", admission, StringComparison.Ordinal);
        Assert.Contains("session.Availability(identity.SourceGame, identity.BuildId, true, true)", admission, StringComparison.Ordinal);
        Assert.Contains("verifySource(identity.SourceGame, identity.BuildId)", admission, StringComparison.Ordinal);
        Assert.Contains("PrepareSelected(identity, source.Path, source.Stamp, equipment, ordinary)", admission, StringComparison.Ordinal);
        Assert.True(admission.IndexOf("session.Availability(", StringComparison.Ordinal)
            < admission.IndexOf("verifySource(", StringComparison.Ordinal));
        Assert.True(admission.IndexOf("verifySource(", StringComparison.Ordinal)
            < admission.IndexOf("PrepareSelected(identity,", StringComparison.Ordinal));
        Assert.DoesNotContain("FileInfo", admission, StringComparison.Ordinal);
        Assert.DoesNotContain("Library.Probe(", admission, StringComparison.Ordinal);

        int verify = sources.IndexOf("internal VerifiedSourceFile VerifySelectedSourceFile(", StringComparison.Ordinal);
        int existing = sources.IndexOf("public void Initialize()", verify, StringComparison.Ordinal);
        Assert.True(verify >= 0 && existing > verify);
        string sourceRead = sources[verify..existing];
        Assert.Contains("file.Length != source.SizeBytes", sourceRead, StringComparison.Ordinal);
        Assert.Contains("_storage.Open(source.Path)", sourceRead, StringComparison.Ordinal);
        Assert.Contains("Library.Probe(reader)", sourceRead, StringComparison.Ordinal);
        Assert.Contains("probe.Definition?.Authority.BuildId != buildId", sourceRead, StringComparison.Ordinal);
        Assert.Contains("new(source.Path, (file.Length, file.LastWriteTimeUtc.Ticks))", sourceRead, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerAvatarProvider", sourceRead, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareSelectedBank(", sourceRead, StringComparison.Ordinal);

        int stageStart = avatar.IndexOf("internal void PrepareSelected(PlayerAvatarIdentity identity", StringComparison.Ordinal);
        int stageEnd = avatar.IndexOf("internal void Prepare(PlayerAvatarIdentity identity", stageStart, StringComparison.Ordinal);
        Assert.True(stageStart >= 0 && stageEnd > stageStart);
        string staging = avatar[stageStart..stageEnd];
        Assert.Contains("Prepare(identity, path, stamp)", staging, StringComparison.Ordinal);
        Assert.Contains("equipment.PrepareSelectedBank(path, ordinary, PreparedSourceChanged)", staging, StringComparison.Ordinal);
        Assert.True(staging.IndexOf("Prepare(identity, path, stamp)", StringComparison.Ordinal)
            < staging.IndexOf("equipment.PrepareSelectedBank(", StringComparison.Ordinal));
        Assert.DoesNotContain("FileInfo", staging, StringComparison.Ordinal);
        Assert.DoesNotContain("Library.Probe", staging, StringComparison.Ordinal);
    }

    [Fact]
    public void EquipmentPoseSelectionBelongsToTheExistingBinderInsteadOfTheWorldRoot()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        string binder = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/Player/PlayerEquipmentPresentationBinder.cs"));
        string avatar = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/Player/PlayerAvatarPresentationHost.cs"));

        int start = avatar.IndexOf("private void StepPlayerAvatarPhysics(", StringComparison.Ordinal);
        int end = avatar.IndexOf("private void ResetPlayerAvatarPhysics(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        string tick = avatar[start..end];
        Assert.DoesNotContain("StepPlayerAvatarPhysics", root, StringComparison.Ordinal);
        Assert.Contains("_avatarPresentation.AttachWorldVisual(player, new(", root, StringComparison.Ordinal);

        Assert.Contains("bindings.CombatInput().StepInput()", tick, StringComparison.Ordinal);
        Assert.Contains("StepNative(state, input)", tick, StringComparison.Ordinal);
        Assert.Contains("bindings.Equipment.ApplyAvatarEquipmentPose(", tick, StringComparison.Ordinal);
        Assert.Contains("SampleClock()", tick, StringComparison.Ordinal);
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
