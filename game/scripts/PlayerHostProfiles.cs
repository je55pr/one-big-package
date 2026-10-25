using OBP.Core;
using OBP.Fusion;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OneBigPackage;

/// <summary>Composition-root registrations for the genuinely implemented controllers.</summary>
internal static class PlayerHostProfiles
{
    public const string Locomotion = "rac1/extended-locomotion/v1", Contact = "rac1/ordinary-contact/v1",
        Traversal = "rac1/recovered-traversal/v1", Camera = "rac1/recovered-camera/v1", Animation = "rac1/recovered-animation/v1";
    public static PlayerServiceBindings Services { get; } = new("obp/input/v1", "obp/equipment-actions/v1", "obp/rac1-derived-health/v1", "obp/player-camera/v1",
        "obp/player-hud/v1", "obp/pause/v1", "obp/quick-select/v1", "obp/player-audio/v1");
    public static SourceProfileKey NativeIdentity { get; } = new(ObpSourceGame.Rac1, Rac1PlayerAvatarProvider.Instance.BuildId, "rac1/ordinary-player/v1");
    public static SourceProfileKey DiagnosticIdentity { get; } = new(ObpSourceGame.Synthetic, DiagnosticAvatarProvider.Instance.BuildId, "obp/diagnostic-player/v1");
    public static PlayerControlProfile Native { get; } = new(NativeIdentity, Locomotion, Contact, Traversal, Camera,
        new(Rac1CollisionProfile.OrdinaryHorizontalRadius, Rac1CollisionProfile.OrdinaryUpperContactHeight + Rac1CollisionProfile.OrdinaryHorizontalRadius,
            0, (float)Rac1OrdinaryGroundContactMotion.OrdinarySupportMaxAngleRadians, (float)Rac1OrdinaryGroundContactMotion.OrdinarySupportContactMetricLimit),
        new(true), PlayerActionCapabilities.Wrench | PlayerActionCapabilities.Equipment,
        PlayerInterfaceCapabilities.QuickSelect | PlayerInterfaceCapabilities.EquipmentDevelopment | PlayerInterfaceCapabilities.MovementBookmarks | PlayerInterfaceCapabilities.RetailPresentation);
    public static PlayerControlProfile Diagnostic { get; } = new(DiagnosticIdentity,
        "obp/common-base-diagnostic/v1", "obp/godot-contact/v1", "obp/no-traversal/v1", "obp/diagnostic-camera/v1",
        new(.6f, 2.6f, 1.5f, MathF.PI / 3, .1f), new(false), PlayerActionCapabilities.None, PlayerInterfaceCapabilities.None);
    public static PlayerProfileRegistry Registry { get; } = new([Native, Diagnostic],
        [new(NativeIdentity, Animation, 60) { CompatibleAvatars = [new(ObpSourceGame.Rac1, NativeIdentity.BuildId,
            Rac1PlayerAvatarProvider.Instance.DefaultAvatarId, "rac1:ratchet:moby-class-0")] },
         new(DiagnosticIdentity, "obp/diagnostic-animation/v1", 60) { CompatibleAvatars = [new(ObpSourceGame.Synthetic,
            DiagnosticIdentity.BuildId, "capsule", "obp:diagnostic-capsule")] }], [Services]);
}
